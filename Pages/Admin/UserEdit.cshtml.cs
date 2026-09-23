using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Hubs;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

public class UserEditModel(AppDbContext db, HierarchyService tree, SessionNotifier notifier)
    : AdminPageModel(db, tree)
{
    public User? Target { get; set; }
    public bool CanManage { get; set; }
    public bool IsOnline { get; set; }
    public List<User> Ancestors { get; set; } = [];
    public List<User> MoveTargets { get; set; } = [];
    public int DirectChildren { get; set; }
    public int BranchMembers { get; set; }
    public List<DomainSite> Sites { get; set; } = [];
    public List<BettingHistory> RecentBets { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await LoadAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(
        int id, string displayName, DateTime? expirationDate, bool isActive = false, bool diagnosticLogging = false,
        bool useProviderPragmatic = false, bool useProviderEvolution = false, string username = "", string? returnTo = null)
    {
        var target = await LoadManageableAsync(id);
        if (target is null)
            return Denied(id);

        // 아이디(로그인명) 변경: 비어있지 않고 바뀌었으면 중복 검사 후 반영.
        if (!string.IsNullOrWhiteSpace(username))
        {
            var newUsername = username.Trim();
            if (newUsername.Length < 2)
            {
                TempData["Err"] = "아이디는 2자 이상이어야 합니다.";
                return Back(id, returnTo);
            }
            if (!string.Equals(newUsername, target.Username, StringComparison.Ordinal))
            {
                if (await Db.Users.AnyAsync(u => u.Username == newUsername && u.Id != id))
                {
                    TempData["Err"] = $"이미 사용 중인 아이디입니다: {newUsername}";
                    return Back(id, returnTo);
                }
                target.Username = newUsername;
            }
        }

        var wasActive = target.IsActive;
        target.DisplayName = string.IsNullOrWhiteSpace(displayName) ? target.Username : displayName.Trim();
        target.ExpirationDate = (expirationDate.HasValue ? PragmaticBot.Server.Services.KoreaTime.DateToUtc(expirationDate.Value.Date.AddDays(1).AddSeconds(-1)) : (System.DateTime?)null);
        target.IsActive = isActive;
        target.DiagnosticLogging = diagnosticLogging;

        // capped by whoever owns the branch above this account, and never left empty
        var owner = target.ParentId is { } pid ? await Db.Users.FindAsync(pid) : null;
        var ceiling = owner?.Providers ?? GameProviders.All;
        var wantedProviders =
            (useProviderPragmatic ? GameProviders.PragmaticPlay : GameProviders.None) |
            (useProviderEvolution ? GameProviders.Evolution : GameProviders.None);
        target.Providers = HierarchyService.GrantableProviders(ceiling, wantedProviders);

        await Db.SaveChangesAsync();

        // A deactivated branch head should stop its whole branch, not just itself.
        if (wasActive && !isActive)
            await DeactivateBranchAsync(target);

        TempData["Ok"] = "저장했습니다.";
        return Back(id, returnTo);
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int newParentId, string? returnTo = null)
    {
        var target = await LoadManageableAsync(id);
        if (target is null)
            return Denied(id);

        var newParent = newParentId == Me.Id ? Me : await Tree.FindVisibleAsync(Me, newParentId);
        if (newParent is null || newParent.Role <= target.Role)
        {
            TempData["Err"] = "옮길 수 없는 상위 조직입니다.";
            return Back(id, returnTo);
        }

        try
        {
            await Tree.MoveAsync(target, newParent);
            TempData["Ok"] = $"{newParent.Username} 아래로 옮겼습니다.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["Err"] = ex.Message;
        }

        return Back(id, returnTo);
    }

    public async Task<IActionResult> OnPostForceLogoutAsync(int id, string? returnTo = null)
    {
        var target = await LoadManageableAsync(id);
        if (target is null)
            return Denied(id);

        target.CurrentSessionId = null;
        await Db.SaveChangesAsync();
        await notifier.ForceLogoutAsync(target.Id, "관리자에 의해 세션이 종료되었습니다.");

        TempData["Ok"] = "강제 로그아웃했습니다.";
        return Back(id, returnTo);
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int id, string newPassword, string? returnTo = null)
    {
        var target = await LoadManageableAsync(id);
        if (target is null)
            return Denied(id);

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
        {
            TempData["Err"] = "비밀번호는 4자 이상이어야 합니다.";
            return Back(id, returnTo);
        }

        target.PasswordHash = PasswordHasher.Hash(newPassword);
        target.CurrentSessionId = null;
        await Db.SaveChangesAsync();
        await notifier.ForceLogoutAsync(target.Id, "비밀번호가 변경되어 세션이 종료되었습니다.");

        TempData["Ok"] = "비밀번호를 변경했습니다. 기존 세션은 종료됩니다.";
        return Back(id, returnTo);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, string? returnTo = null)
    {
        var target = await LoadManageableAsync(id);
        if (target is null)
            return Denied(id);

        if (await Db.Users.AnyAsync(u => u.ParentId == id))
        {
            TempData["Err"] = "하위 조직이 남아 있어 삭제할 수 없습니다. 먼저 옮기거나 삭제하세요.";
            return Back(id, returnTo);
        }

        await PurgeUserDataAsync(id);
        Db.Users.Remove(target);
        await Db.SaveChangesAsync();

        TempData["Ok"] = $"계정을 삭제했습니다: {target.Username}";
        return RedirectToPage("/Admin/Users");
    }

    IActionResult Denied(int id)
    {
        TempData["Err"] = "이 계정을 관리할 권한이 없습니다.";
        return RedirectToPage("/Admin/Users");
    }

    /// <summary>목록에서 모달로 들어온 요청(returnTo=users)은 목록으로, 상세페이지 요청은 상세로 복귀.</summary>
    IActionResult Back(int id, string? returnTo) =>
        string.Equals(returnTo, "users", StringComparison.OrdinalIgnoreCase)
            ? RedirectToPage("/Admin/Users")
            : RedirectToPage(new { id });

    /// <summary>사용기간 연장: 개월수(+N, 현재 만료일에 이어붙임/만료면 오늘부터) · 날짜 지정 · 무제한.</summary>
    public async Task<IActionResult> OnPostExtendAsync(
        int id, string? mode = null, int? months = null, DateTime? untilDate = null, bool unlimited = false, string? returnTo = null)
    {
        var target = await LoadManageableAsync(id);
        if (target is null)
            return Denied(id);

        if (unlimited)
        {
            target.ExpirationDate = null;
            await Db.SaveChangesAsync();
            TempData["Ok"] = $"{target.Username}: 무제한으로 변경했습니다.";
            return Back(id, returnTo);
        }

        if (string.Equals(mode, "date", StringComparison.OrdinalIgnoreCase))
        {
            if (untilDate is not { } d)
            {
                TempData["Err"] = "연장할 날짜를 선택하세요.";
                return Back(id, returnTo);
            }
            target.ExpirationDate = KoreaTime.DateToUtc(d.Date.AddDays(1).AddSeconds(-1));
        }
        else // months
        {
            int n = months ?? 0;
            if (n <= 0)
            {
                TempData["Err"] = "연장 개월수를 입력하세요.";
                return Back(id, returnTo);
            }
            // 아직 유효하면 현재 만료일(KST)에 이어붙이고, 이미 만료(또는 무제한 아님·과거)면 오늘(KST)부터.
            var baseDate = (target.ExpirationDate is { } exp && exp > DateTime.UtcNow)
                ? KoreaTime.KstDateOf(exp)
                : KoreaTime.Now.Date;
            var newDate = baseDate.AddMonths(n);
            target.ExpirationDate = KoreaTime.DateToUtc(newDate.AddDays(1).AddSeconds(-1));
        }

        await Db.SaveChangesAsync();
        TempData["Ok"] = $"{target.Username}: 만료일을 {KoreaTime.Fmt(target.ExpirationDate!.Value, "yyyy-MM-dd")} 로 설정했습니다.";
        return Back(id, returnTo);
    }

    async Task<User?> LoadManageableAsync(int id)
    {
        var target = await Db.Users.FirstOrDefaultAsync(u => u.Id == id);
        return target is not null && Tree.CanManage(Me, target) ? target : null;
    }

    /// <summary>Turning off a 총판/대리점 stops every bot underneath it too.</summary>
    async Task DeactivateBranchAsync(User head)
    {
        var prefix = HierarchyService.SubtreePrefixOf(head);
        var branch = await Db.Users.Where(u => u.TreePath.StartsWith(prefix)).ToListAsync();

        foreach (var u in branch.Append(head))
        {
            if (u.CurrentSessionId is null)
                continue;
            u.CurrentSessionId = null;
            await notifier.ForceLogoutAsync(u.Id, "상위 조직이 비활성화되어 세션이 종료되었습니다.");
        }
        await Db.SaveChangesAsync();
    }

    /// <summary>Per-user settings have no FK to Users, so clear them explicitly.</summary>
    async Task PurgeUserDataAsync(int userId)
    {
        await Db.FormatSettings.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.ExceptionFormats.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.RoomGateFormats.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.BetAmountSettings.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.MatrixCells.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.MatrixRowRules.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.LimitSettings.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.RoomCooldownSettings.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.StrategyChains.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.BettingSessions.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.UserGameSelections.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.AppLogs.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await Db.DomainSites.Where(x => x.UserId == userId).ExecuteDeleteAsync();

        var betIds = await Db.BettingHistories.Where(x => x.UserId == userId).Select(x => x.Id).ToListAsync();
        await Db.BettingAllocations.Where(a => betIds.Contains(a.BettingHistoryId)).ExecuteDeleteAsync();
        await Db.BettingHistories.Where(x => x.UserId == userId).ExecuteDeleteAsync();
    }

    async Task LoadAsync(int id)
    {
        Target = await Tree.FindVisibleAsync(Me, id);
        if (Target is null)
            return;

        CanManage = Tree.CanManage(Me, Target);
        IsOnline = Target.CurrentSessionId is not null
            && Target.LastHeartbeatUtc >= DateTime.UtcNow.AddMinutes(-5);

        Ancestors = await Tree.AncestorsOfAsync(Target);

        var prefix = HierarchyService.SubtreePrefixOf(Target);
        DirectChildren = await Db.Users.CountAsync(u => u.ParentId == Target.Id);
        BranchMembers = await Db.Users
            .CountAsync(u => u.Role == UserRole.Member && u.TreePath.StartsWith(prefix));

        // valid new parents: anything I manage that outranks the target, plus me
        MoveTargets = await Tree.VisibleUsers(Me).AsNoTracking()
            .Where(u => u.Role > Target.Role && u.Id != Target.Id)
            .OrderBy(u => u.TreePath).ThenBy(u => u.Username)
            .ToListAsync();

        Sites = await Db.DomainSites.AsNoTracking()
            .Where(s => s.UserId == id)
            .OrderBy(s => s.SiteSlot)
            .ToListAsync();

        RecentBets = await Db.BettingHistories.AsNoTracking()
            .Where(b => b.UserId == id)
            .OrderByDescending(b => b.Id)
            .Take(20)
            .ToListAsync();
    }
}
