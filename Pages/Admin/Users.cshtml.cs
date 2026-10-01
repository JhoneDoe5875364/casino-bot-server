using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Hubs;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

public class UsersModel(AppDbContext db, HierarchyService tree, SessionNotifier notifier) : AdminPageModel(db, tree)
{
    /// <summary>One row of the org chart, already flattened for rendering.</summary>
    public record Node(User User, int Depth, int MemberCount, int BetCount, bool Online, string? ParentName);

    public List<Node> Rows { get; set; } = [];
    public List<User> ParentChoices { get; set; } = [];
    public IReadOnlyList<UserRole> CreatableRoles { get; set; } = [];

    [BindProperty(SupportsGet = true, Name = "q")] public string? Query { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostCreateAsync(
        string username, string displayName, string password, int role, int parentId, DateTime? expirationDate,
        bool useProviderPragmatic = false, bool useProviderEvolution = false)
    {
        username = (username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Err"] = "아이디와 비밀번호는 필수입니다.";
            return RedirectToPage();
        }

        if (await Db.Users.AnyAsync(u => u.Username == username))
        {
            TempData["Err"] = $"이미 존재하는 아이디입니다: {username}";
            return RedirectToPage();
        }

        // The parent must be the viewer or somebody in their branch — never a peer's account.
        var parent = parentId == Me.Id ? Me : await Tree.FindVisibleAsync(Me, parentId);
        if (parent is null || (parent.Id != Me.Id && !Tree.CanManage(Me, parent)))
        {
            TempData["Err"] = "선택한 상위 조직에 계정을 만들 권한이 없습니다.";
            return RedirectToPage();
        }

        var wanted = (UserRole)role;
        if (!HierarchyService.CreatableRoles(Me.Role).Contains(wanted))
        {
            TempData["Err"] = $"{RoleLabel(Me.Role)}은(는) {RoleLabel(wanted)} 계정을 만들 수 없습니다.";
            return RedirectToPage();
        }

        try
        {
            // nothing ticked means "same as the parent" rather than an account that can play nothing
            var wantedProviders =
                (useProviderPragmatic ? GameProviders.PragmaticPlay : GameProviders.None) |
                (useProviderEvolution ? GameProviders.Evolution : GameProviders.None);
            if (wantedProviders == GameProviders.None)
                wantedProviders = parent.Providers;

            var created = await Tree.CreateUnderAsync(
                parent, username, displayName, password, wanted,
                (expirationDate.HasValue ? PragmaticBot.Server.Services.KoreaTime.DateToUtc(expirationDate.Value.Date.AddDays(1).AddSeconds(-1)) : (System.DateTime?)null),
                wantedProviders);

            TempData["Ok"] = $"{RoleLabel(created.Role)} 계정을 만들었습니다: {created.Username}" +
                $" · {HierarchyService.ProvidersLabel(created.Providers)}" +
                (parent.Id == Me.Id ? "" : $" (상위: {parent.Username})");
        }
        catch (InvalidOperationException ex)
        {
            TempData["Err"] = ex.Message;
        }

        return RedirectToPage();
    }

    // ── 목록에서 바로 쓰는 관리 액션들(모달·행 버튼). 상세페이지(UserEdit)와 같은 동작을 여기서 수행하고
    //    끝나면 목록으로 돌아온다. 크로스 페이지 폼 POST가 라우트 문제로 안 돼, 같은 페이지 핸들러로 처리한다.

    public async Task<IActionResult> OnPostSaveAsync(
        int id, string displayName, DateTime? expirationDate, bool diagnosticLogging = false,
        bool useProviderPragmatic = false, bool useProviderEvolution = false, string username = "")
    {
        var target = await LoadManageableAsync(id);
        if (target is null) return Denied();

        if (!string.IsNullOrWhiteSpace(username))
        {
            var newUsername = username.Trim();
            if (newUsername.Length < 2) { TempData["Err"] = "아이디는 2자 이상이어야 합니다."; return RedirectToPage(); }
            if (!string.Equals(newUsername, target.Username, StringComparison.Ordinal))
            {
                if (await Db.Users.AnyAsync(u => u.Username == newUsername && u.Id != id))
                { TempData["Err"] = $"이미 사용 중인 아이디입니다: {newUsername}"; return RedirectToPage(); }
                target.Username = newUsername;
            }
        }

        // 활성/비활성은 별도 토글 버튼(OnPostToggleActive)이 담당한다. 여기선 건드리지 않는다.
        target.DisplayName = string.IsNullOrWhiteSpace(displayName) ? target.Username : displayName.Trim();
        target.ExpirationDate = expirationDate.HasValue ? KoreaTime.DateToUtc(expirationDate.Value.Date.AddDays(1).AddSeconds(-1)) : (DateTime?)null;
        target.DiagnosticLogging = diagnosticLogging;

        var owner = target.ParentId is { } pid ? await Db.Users.FindAsync(pid) : null;
        var ceiling = owner?.Providers ?? GameProviders.All;
        var wantedProviders =
            (useProviderPragmatic ? GameProviders.PragmaticPlay : GameProviders.None) |
            (useProviderEvolution ? GameProviders.Evolution : GameProviders.None);
        target.Providers = HierarchyService.GrantableProviders(ceiling, wantedProviders);

        await Db.SaveChangesAsync();
        TempData["Ok"] = $"{target.Username}: 저장했습니다.";
        return RedirectToPage();
    }

    /// <summary>활성/비활성 토글(목록 외부 버튼). 비활성화 시 하위 조직까지 봇 종료.</summary>
    public async Task<IActionResult> OnPostToggleActiveAsync(int id, bool active)
    {
        var target = await LoadManageableAsync(id);
        if (target is null) return Denied();

        var wasActive = target.IsActive;
        target.IsActive = active;
        await Db.SaveChangesAsync();
        if (wasActive && !active) await DeactivateBranchAsync(target);

        TempData["Ok"] = $"{target.Username}: {(active ? "활성화" : "비활성화")}했습니다.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostExtendAsync(
        int id, string? mode = null, int? months = null, DateTime? untilDate = null, bool unlimited = false)
    {
        var target = await LoadManageableAsync(id);
        if (target is null) return Denied();

        if (unlimited)
        {
            target.ExpirationDate = null;
            await Db.SaveChangesAsync();
            TempData["Ok"] = $"{target.Username}: 무제한으로 변경했습니다.";
            return RedirectToPage();
        }
        if (string.Equals(mode, "date", StringComparison.OrdinalIgnoreCase))
        {
            if (untilDate is not { } d) { TempData["Err"] = "연장할 날짜를 선택하세요."; return RedirectToPage(); }
            target.ExpirationDate = KoreaTime.DateToUtc(d.Date.AddDays(1).AddSeconds(-1));
        }
        else
        {
            int n = months ?? 0;
            if (n <= 0) { TempData["Err"] = "연장 개월수를 입력하세요."; return RedirectToPage(); }
            var baseDate = (target.ExpirationDate is { } exp && exp > DateTime.UtcNow)
                ? KoreaTime.KstDateOf(exp) : KoreaTime.Now.Date;
            target.ExpirationDate = KoreaTime.DateToUtc(baseDate.AddMonths(n).AddDays(1).AddSeconds(-1));
        }
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"{target.Username}: 만료일을 {KoreaTime.Fmt(target.ExpirationDate!.Value, "yyyy-MM-dd")} 로 설정했습니다.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync(int id, string newPassword)
    {
        var target = await LoadManageableAsync(id);
        if (target is null) return Denied();
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
        { TempData["Err"] = "비밀번호는 4자 이상이어야 합니다."; return RedirectToPage(); }

        target.PasswordHash = PasswordHasher.Hash(newPassword);
        target.CurrentSessionId = null;
        await Db.SaveChangesAsync();
        await notifier.ForceLogoutAsync(target.Id, "비밀번호가 변경되어 세션이 종료되었습니다.");
        TempData["Ok"] = $"{target.Username}: 비밀번호를 변경했습니다. 기존 세션은 종료됩니다.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostForceLogoutAsync(int id)
    {
        var target = await LoadManageableAsync(id);
        if (target is null) return Denied();
        target.CurrentSessionId = null;
        await Db.SaveChangesAsync();
        await notifier.ForceLogoutAsync(target.Id, "관리자에 의해 세션이 종료되었습니다.");
        TempData["Ok"] = $"{target.Username}: 강제 로그아웃했습니다.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int newParentId)
    {
        var target = await LoadManageableAsync(id);
        if (target is null) return Denied();
        var newParent = newParentId == Me.Id ? Me : await Tree.FindVisibleAsync(Me, newParentId);
        if (newParent is null || newParent.Role <= target.Role)
        { TempData["Err"] = "옮길 수 없는 상위 조직입니다."; return RedirectToPage(); }
        try { await Tree.MoveAsync(target, newParent); TempData["Ok"] = $"{newParent.Username} 아래로 옮겼습니다."; }
        catch (InvalidOperationException ex) { TempData["Err"] = ex.Message; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var target = await LoadManageableAsync(id);
        if (target is null) return Denied();
        if (await Db.Users.AnyAsync(u => u.ParentId == id))
        { TempData["Err"] = "하위 조직이 남아 있어 삭제할 수 없습니다. 먼저 옮기거나 삭제하세요."; return RedirectToPage(); }
        await PurgeUserDataAsync(id);
        Db.Users.Remove(target);
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"계정을 삭제했습니다: {target.Username}";
        return RedirectToPage();
    }

    IActionResult Denied() { TempData["Err"] = "이 계정을 관리할 권한이 없습니다."; return RedirectToPage(); }

    async Task<User?> LoadManageableAsync(int id)
    {
        var target = await Db.Users.FirstOrDefaultAsync(u => u.Id == id);
        return target is not null && Tree.CanManage(Me, target) ? target : null;
    }

    async Task DeactivateBranchAsync(User head)
    {
        var prefix = HierarchyService.SubtreePrefixOf(head);
        var branch = await Db.Users.Where(u => u.TreePath.StartsWith(prefix)).ToListAsync();
        foreach (var u in branch.Append(head))
        {
            if (u.CurrentSessionId is null) continue;
            u.CurrentSessionId = null;
            await notifier.ForceLogoutAsync(u.Id, "상위 조직이 비활성화되어 세션이 종료되었습니다.");
        }
        await Db.SaveChangesAsync();
    }

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

    async Task LoadAsync()
    {
        CreatableRoles = HierarchyService.CreatableRoles(Me.Role);

        // Anything in my branch that can hold children is a valid parent for a new account.
        ParentChoices = await Tree.VisibleUsers(Me)
            .Where(u => u.Role > UserRole.Member)
            .OrderBy(u => u.TreePath).ThenBy(u => u.Username)
            .AsNoTracking()
            .ToListAsync();

        var all = await Tree.VisibleUsers(Me).AsNoTracking()
            .OrderBy(u => u.TreePath).ThenBy(u => u.Username)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(Query))
        {
            var q = Query.Trim();
            all = all.Where(u =>
                u.Username.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                u.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var ids = all.Select(u => u.Id).ToList();

        var betCounts = await Db.BettingHistories.AsNoTracking()
            .Where(b => ids.Contains(b.UserId))
            .GroupBy(b => b.UserId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var since = DateTime.UtcNow.AddMinutes(-5);
        var rootDepth = Me.Role == UserRole.Owner ? 0 : Depth(Me.TreePath);

        // members sitting under each管理 account, counted across the whole branch
        var memberCounts = all
            .Where(u => u.Role == UserRole.Member)
            .SelectMany(m => m.TreePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse))
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

        // 상위 조직 이름 표시용 — 보이는 목록 안에서 부모 아이디를 이름으로 해석한다.
        var nameById = all.ToDictionary(u => u.Id, u => u.Username);

        Rows = all.Select(u => new Node(
            u,
            Math.Max(0, Depth(u.TreePath) - rootDepth),
            u.Role == UserRole.Member ? 0 : memberCounts.GetValueOrDefault(u.Id),
            betCounts.GetValueOrDefault(u.Id),
            u.CurrentSessionId is not null && u.LastHeartbeatUtc >= since,
            u.ParentId is { } pid && nameById.TryGetValue(pid, out var pn) ? pn : null)).ToList();
    }

    static int Depth(string treePath) =>
        treePath.Count(c => c == '/') - 1;
}
