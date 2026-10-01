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

    // ---------- 회원 개인 설정(서식·베팅금) 조회·관리 ----------
    public bool ShowSettings { get; set; }
    public int SettingsBetType { get; set; }
    public string SettingsFormatText { get; set; } = "";
    public string SettingsBetText { get; set; } = "";

    /// <summary>관리자 편집이 가능한 (행렬이 아닌) 베팅금 타입 목록.</summary>
    public static readonly (int Value, string Label)[] BetTypeOptions =
    {
        (0, "마틴"), (1, "홍콩크루즈"), (4, "달랑베르"), (5, "마틴+달랑"),
        (6, "1-3-2-6"), (7, "오스카그라인드"), (8, "플랫"), (9, "라부셰르"), (10, "달멘징"), (3, "혼합")
    };

    public string BetTypeLabel(int t)
    {
        foreach (var o in BetTypeOptions)
            if (o.Value == t) return o.Label;
        return $"타입{t}";
    }

    // ---------- 행렬(2차원) 설정 조회·관리 ----------
    public int SettingsMatrixType { get; set; }
    public string SettingsMatrixCellsText { get; set; } = "";
    public string SettingsMatrixRulesText { get; set; } = "";

    // 타입 전환을 새로고침 없이 하려고 모든 타입의 텍스트를 미리 담아 클라이언트로 내려보낸다.
    public Dictionary<int, string> BetTextByType { get; set; } = new();
    public Dictionary<int, string> MatrixCellsByType { get; set; } = new();
    public Dictionary<int, string> MatrixRulesByType { get; set; } = new();

    public Dictionary<int, string> BetTypeOptionsMap => BetTypeOptions.ToDictionary(o => o.Value, o => o.Label);
    public Dictionary<int, string> MatrixTypeOptionsMap => MatrixTypeOptions.ToDictionary(o => o.Value, o => o.Label);

    // 행렬 표 입력용(봇과 동일: 15차 × 20단계). 그리드[타입]["행_열"]=금액, 규칙[타입][행]=[당첨,이동,달랑,쉬는분,상한]
    public const int MatrixRows = 15;
    public const int MatrixCols = 20;
    public Dictionary<int, Dictionary<string, string>> MatrixCellGrid { get; set; } = new();
    public Dictionary<int, Dictionary<int, string[]>> MatrixRuleGrid { get; set; } = new();

    /// <summary>초기 렌더용: 현재 선택된 행렬 타입의 (행,열) 셀 금액.</summary>
    public string MxCell(int row, int col)
        => MatrixCellGrid.GetValueOrDefault(SettingsMatrixType)?.GetValueOrDefault($"{row}_{col}") ?? "";

    /// <summary>초기 렌더용: 현재 타입 행 규칙 필드(0=당첨,1=이동차,2=달랑,3=쉬는분,4=상한).</summary>
    public string MxRule(int row, int idx)
    {
        var rg = MatrixRuleGrid.GetValueOrDefault(SettingsMatrixType);
        if (rg != null && rg.TryGetValue(row, out var arr) && idx < arr.Length)
            return arr[idx];
        return "";
    }

    /// <summary>행렬 계열 베팅금 타입.</summary>
    public static readonly (int Value, string Label)[] MatrixTypeOptions =
    {
        (11, "행렬"), (12, "행렬달랑베르"), (13, "스위칭행렬(마틴+달랑)"), (14, "달랑행렬")
    };

    public string MatrixTypeLabel(int t)
    {
        foreach (var o in MatrixTypeOptions)
            if (o.Value == t) return o.Label;
        return $"행렬{t}";
    }

    /// <summary>행렬 셀 목록을 "한 줄 = 한 행, 열은 쉼표" 텍스트로 만든다.</summary>
    static string BuildMatrixCellsText(List<MatrixCell> cells)
    {
        var cellLines = new List<string>();
        int maxRow = cells.Count > 0 ? cells.Max(c => c.RowIndex) : 0;
        for (int r = 1; r <= maxRow; r++)
        {
            var rowCells = cells.Where(c => c.RowIndex == r).ToList();
            if (rowCells.Count == 0) { cellLines.Add(""); continue; }
            int maxCol = rowCells.Max(c => c.ColIndex);
            var tokens = new List<string>();
            for (int cIdx = 1; cIdx <= maxCol; cIdx++)
            {
                var cell = rowCells.FirstOrDefault(c => c.ColIndex == cIdx);
                tokens.Add(cell is null ? "" : cell.Amount.ToString("0.##"));
            }
            cellLines.Add(string.Join(",", tokens));
        }
        return string.Join("\n", cellLines);
    }

    // ---------- 예외서식 · 방게이트 · 한도 · 방쿨다운 ----------
    public string SettingsExceptionText { get; set; } = "";
    public string SettingsRoomGateOnText { get; set; } = "";
    public string SettingsRoomGateOffText { get; set; } = "";
    public LimitSetting? SettingsLimits { get; set; }
    public RoomCooldownSetting? SettingsCooldown { get; set; }
    public List<StrategyChain> SettingsChains { get; set; } = [];

    /// <summary>전략 스텝의 타입 라벨(베팅금/행렬 타입 코드를 공유).</summary>
    public string StrategyTypeLabel(int t)
    {
        foreach (var o in BetTypeOptions) if (o.Value == t) return o.Label;
        foreach (var o in MatrixTypeOptions) if (o.Value == t) return o.Label;
        return $"타입{t}";
    }

    public async Task<IActionResult> OnGetAsync(int id, int settingsBetType = 0, int settingsMatrixType = 11)
    {
        SettingsBetType = settingsBetType;
        SettingsMatrixType = settingsMatrixType;
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

    /// <summary>회원 서식을 통째로 교체 저장(한 줄 = 서식 하나, 번호는 1..n 순서).</summary>
    public async Task<IActionResult> OnPostSaveSettingsFormatsAsync(int id, string? formatText)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var lines = (formatText ?? "").Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        await Db.FormatSettings.Where(f => f.UserId == id).ExecuteDeleteAsync();
        for (int i = 0; i < lines.Count; i++)
            Db.FormatSettings.Add(new FormatSetting { UserId = id, Number = i + 1, Text = lines[i] });
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"서식 {lines.Count}개를 저장했습니다.";
        return RedirectToPage(new { id });
    }

    /// <summary>선택한 타입의 베팅금만 교체 저장(한 줄 = 번호=금액[,방식]).</summary>
    public async Task<IActionResult> OnPostSaveSettingsBetAmountsAsync(int id, int settingsBetType, string? betText)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var parsed = new List<BetAmountSetting>();
        foreach (var raw in (betText ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var eq = line.Split('=', 2);
            if (eq.Length != 2 || !int.TryParse(eq[0].Trim(), out var num)) continue;
            var rhs = eq[1].Split(',');
            decimal? amount = decimal.TryParse(rhs[0].Trim(), out var a) ? a : null;
            int? method = rhs.Length > 1 && int.TryParse(rhs[1].Trim(), out var m) ? m : null;
            parsed.Add(new BetAmountSetting { UserId = id, BetAmountType = settingsBetType, Number = num, Amount = amount, BettingMethod = method });
        }
        await Db.BetAmountSettings.Where(b => b.UserId == id && b.BetAmountType == settingsBetType).ExecuteDeleteAsync();
        Db.BetAmountSettings.AddRange(parsed);
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"{BetTypeLabel(settingsBetType)} 베팅금 {parsed.Count}개를 저장했습니다.";
        return RedirectToPage(new { id, settingsBetType });
    }

    /// <summary>행렬 셀 교체 저장(한 줄 = 한 행, 열 금액을 쉼표로).</summary>
    public async Task<IActionResult> OnPostSaveSettingsMatrixCellsAsync(int id, int settingsMatrixType, string? cellsText)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var parsed = new List<MatrixCell>();
        var lines = (cellsText ?? "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int rowIndex = i + 1;
            var tokens = lines[i].Split(',');
            for (int c = 0; c < tokens.Length; c++)
            {
                var tok = tokens[c].Trim();
                if (tok.Length == 0) continue;
                if (!decimal.TryParse(tok, out var amount)) continue;
                parsed.Add(new MatrixCell { UserId = id, BetAmountType = settingsMatrixType, RowIndex = rowIndex, ColIndex = c + 1, Amount = amount });
            }
        }
        await Db.MatrixCells.Where(c => c.UserId == id && c.BetAmountType == settingsMatrixType).ExecuteDeleteAsync();
        Db.MatrixCells.AddRange(parsed);
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"{MatrixTypeLabel(settingsMatrixType)} 행렬 셀 {parsed.Count}개를 저장했습니다.";
        return RedirectToPage(new { id, settingsMatrixType });
    }

    /// <summary>행렬 행규칙 교체 저장(한 줄 = 행=당첨수,이동차,모드,쉬는분,첫행상한).</summary>
    public async Task<IActionResult> OnPostSaveSettingsMatrixRulesAsync(int id, int settingsMatrixType, string? rulesText)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var parsed = new List<MatrixRowRule>();
        foreach (var raw in (rulesText ?? "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var eq = line.Split('=', 2);
            if (eq.Length != 2 || !int.TryParse(eq[0].Trim(), out var rowIndex)) continue;
            var f = eq[1].Split(',');
            int Wi(int idx) => idx < f.Length && int.TryParse(f[idx].Trim(), out var v) ? v : 0;
            decimal Wd(int idx) => idx < f.Length && decimal.TryParse(f[idx].Trim(), out var v) ? v : 0m;
            parsed.Add(new MatrixRowRule
            {
                UserId = id,
                BetAmountType = settingsMatrixType,
                RowIndex = rowIndex,
                WinCount = Wi(0),
                TargetRow = Wi(1),
                WinMode = Wi(2),
                RestMinutes = Wi(3),
                FirstRowCapAmount = Wd(4)
            });
        }
        await Db.MatrixRowRules.Where(r => r.UserId == id && r.BetAmountType == settingsMatrixType).ExecuteDeleteAsync();
        Db.MatrixRowRules.AddRange(parsed);
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"{MatrixTypeLabel(settingsMatrixType)} 행렬 행규칙 {parsed.Count}개를 저장했습니다.";
        return RedirectToPage(new { id, settingsMatrixType });
    }

    /// <summary>행렬 셀 + 행규칙을 표 입력에서 한 번에 저장(봇과 동일 형식).</summary>
    public async Task<IActionResult> OnPostSaveSettingsMatrixAsync(int id, int settingsMatrixType)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }

        var cells = new List<MatrixCell>();
        for (int r = 1; r <= MatrixRows; r++)
            for (int c = 1; c <= MatrixCols; c++)
            {
                var raw = Request.Form[$"c_{r}_{c}"].ToString().Trim();
                if (raw.Length == 0) continue;
                if (!decimal.TryParse(raw, out var amount)) continue;
                cells.Add(new MatrixCell { UserId = id, BetAmountType = settingsMatrixType, RowIndex = r, ColIndex = c, Amount = amount });
            }

        var rules = new List<MatrixRowRule>();
        for (int r = 1; r <= MatrixRows; r++)
        {
            int w = ParseInt(Request.Form[$"w_{r}"]);
            int t = ParseInt(Request.Form[$"t_{r}"]);
            int m = Request.Form[$"m_{r}"].Count > 0 ? 1 : 0;
            int rm = ParseInt(Request.Form[$"rm_{r}"]);
            decimal cap = ParseDec(Request.Form[$"cap_{r}"]);
            // 봇과 동일: 당첨수+이동차가 있거나, 달랑/쉬는분/1차상한 중 하나라도 있으면 규칙으로 저장.
            bool meaningful = (w >= 1 && t >= 1) || m == 1 || rm >= 1 || cap >= 1m;
            if (!meaningful) continue;
            rules.Add(new MatrixRowRule { UserId = id, BetAmountType = settingsMatrixType, RowIndex = r, WinCount = w, TargetRow = t, WinMode = m, RestMinutes = rm, FirstRowCapAmount = cap });
        }

        await Db.MatrixCells.Where(c => c.UserId == id && c.BetAmountType == settingsMatrixType).ExecuteDeleteAsync();
        await Db.MatrixRowRules.Where(r => r.UserId == id && r.BetAmountType == settingsMatrixType).ExecuteDeleteAsync();
        Db.MatrixCells.AddRange(cells);
        Db.MatrixRowRules.AddRange(rules);
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"{MatrixTypeLabel(settingsMatrixType)} 행렬을 저장했습니다 (셀 {cells.Count} · 규칙 {rules.Count}).";
        return RedirectToPage(new { id, settingsMatrixType });
    }

    static int ParseInt(Microsoft.Extensions.Primitives.StringValues v) => int.TryParse(v.ToString().Trim(), out var x) ? x : 0;
    static decimal ParseDec(Microsoft.Extensions.Primitives.StringValues v) => decimal.TryParse(v.ToString().Trim(), out var x) ? x : 0m;

    /// <summary>예외서식 교체 저장(한 줄 = 하나).</summary>
    public async Task<IActionResult> OnPostSaveSettingsExceptionAsync(int id, string? exceptionText)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var lines = (exceptionText ?? "").Replace("\r\n", "\n").Split('\n')
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        await Db.ExceptionFormats.Where(f => f.UserId == id).ExecuteDeleteAsync();
        for (int i = 0; i < lines.Count; i++)
            Db.ExceptionFormats.Add(new ExceptionFormat { UserId = id, Number = i + 1, Pattern = lines[i] });
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"예외서식 {lines.Count}개를 저장했습니다.";
        return RedirectToPage(new { id });
    }

    /// <summary>방게이트 서식 교체 저장(열기=GateType0, 닫기=GateType1).</summary>
    public async Task<IActionResult> OnPostSaveSettingsRoomGateAsync(int id, string? onText, string? offText)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var onLines = (onText ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var offLines = (offText ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        await Db.RoomGateFormats.Where(f => f.UserId == id).ExecuteDeleteAsync();
        int n = 1;
        foreach (var pat in onLines)
            Db.RoomGateFormats.Add(new RoomGateFormat { UserId = id, Number = n++, GateType = 0, Pattern = pat });
        n = 1;
        foreach (var pat in offLines)
            Db.RoomGateFormats.Add(new RoomGateFormat { UserId = id, Number = n++, GateType = 1, Pattern = pat });
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"방게이트 서식(열기 {onLines.Count} · 닫기 {offLines.Count})을 저장했습니다.";
        return RedirectToPage(new { id });
    }

    /// <summary>한도 설정 저장(단일 객체).</summary>
    public async Task<IActionResult> OnPostSaveSettingsLimitsAsync(int id,
        decimal? upperLimitPercent, bool isUpperLimitEnabled, decimal? lowerLimitPercent, bool isLowerLimitEnabled,
        int? maxActiveBettingRooms, int upperLimitMode, decimal? targetProfitAmount, bool isTargetCycleEnabled,
        int? restIntervalMinutes, int? sessionCount)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var limit = await Db.LimitSettings.FirstOrDefaultAsync(l => l.UserId == id);
        if (limit is null)
        {
            limit = new LimitSetting { UserId = id };
            Db.LimitSettings.Add(limit);
        }
        limit.UpperLimitPercent = upperLimitPercent;
        limit.IsUpperLimitEnabled = isUpperLimitEnabled;
        limit.LowerLimitPercent = lowerLimitPercent;
        limit.IsLowerLimitEnabled = isLowerLimitEnabled;
        limit.MaxActiveBettingRooms = maxActiveBettingRooms;
        limit.UpperLimitMode = upperLimitMode;
        limit.TargetProfitAmount = targetProfitAmount;
        limit.IsTargetCycleEnabled = isTargetCycleEnabled;
        limit.RestIntervalMinutes = restIntervalMinutes;
        limit.SessionCount = sessionCount;
        await Db.SaveChangesAsync();
        TempData["Ok"] = "한도 설정을 저장했습니다.";
        return RedirectToPage(new { id });
    }

    /// <summary>방 쿨다운 설정 저장(단일 객체).</summary>
    public async Task<IActionResult> OnPostSaveSettingsCooldownAsync(int id, bool isEnabled, int lossThreshold, int cooldownMinutes)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var cd = await Db.RoomCooldownSettings.FirstOrDefaultAsync(c => c.UserId == id);
        if (cd is null)
        {
            cd = new RoomCooldownSetting { UserId = id };
            Db.RoomCooldownSettings.Add(cd);
        }
        cd.IsEnabled = isEnabled;
        cd.LossThreshold = lossThreshold;
        cd.CooldownMinutes = cooldownMinutes;
        await Db.SaveChangesAsync();
        TempData["Ok"] = "방 쿨다운 설정을 저장했습니다.";
        return RedirectToPage(new { id });
    }

    /// <summary>전략 체인 활성화(하나만 활성, 나머지는 비활성).</summary>
    public async Task<IActionResult> OnPostActivateChainAsync(int id, long chainId)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var chains = await Db.StrategyChains.Where(c => c.UserId == id).ToListAsync();
        if (!chains.Any(c => c.Id == chainId))
        {
            TempData["Err"] = "전략 체인을 찾을 수 없습니다.";
            return RedirectToPage(new { id });
        }
        foreach (var c in chains)
            c.IsActive = c.Id == chainId;
        await Db.SaveChangesAsync();
        TempData["Ok"] = "전략 체인을 활성화했습니다.";
        return RedirectToPage(new { id });
    }

    /// <summary>전략 체인 비활성화(활성 체인을 꺼서 활성 체인 없음 상태로).</summary>
    public async Task<IActionResult> OnPostDeactivateChainAsync(int id, long chainId)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var chain = await Db.StrategyChains.FirstOrDefaultAsync(c => c.Id == chainId && c.UserId == id);
        if (chain is not null)
        {
            chain.IsActive = false;
            await Db.SaveChangesAsync();
            TempData["Ok"] = "전략 체인을 비활성화했습니다.";
        }
        return RedirectToPage(new { id });
    }

    /// <summary>전략 체인 삭제(스텝은 FK로 함께 삭제).</summary>
    public async Task<IActionResult> OnPostDeleteChainAsync(int id, long chainId)
    {
        var target = await LoadManageableAsync(id);
        if (target is null || target.Role != UserRole.Member)
        {
            TempData["Err"] = "이 회원의 설정을 수정할 권한이 없습니다.";
            return RedirectToPage(new { id });
        }
        var chain = await Db.StrategyChains.FirstOrDefaultAsync(c => c.Id == chainId && c.UserId == id);
        if (chain is not null)
        {
            Db.StrategyChains.Remove(chain);
            await Db.SaveChangesAsync();
            TempData["Ok"] = "전략 체인을 삭제했습니다.";
        }
        return RedirectToPage(new { id });
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

        // 설정은 봇을 돌리는 회원 계정에만 있고, 내가 관리할 수 있는 대상일 때만 편집 노출
        ShowSettings = Target.Role == UserRole.Member && CanManage;
        if (ShowSettings)
        {
            var formats = await Db.FormatSettings.AsNoTracking()
                .Where(f => f.UserId == id).OrderBy(f => f.Number)
                .Select(f => f.Text).ToListAsync();
            SettingsFormatText = string.Join("\n", formats.Where(t => !string.IsNullOrWhiteSpace(t)));

            // 베팅금: 모든 편집 타입의 텍스트를 미리 만들어 둔다(클라이언트 전환용).
            foreach (var (val, _) in BetTypeOptions)
            {
                var bets = await Db.BetAmountSettings.AsNoTracking()
                    .Where(b => b.UserId == id && b.BetAmountType == val)
                    .OrderBy(b => b.Number).ToListAsync();
                BetTextByType[val] = string.Join("\n", bets.Select(b =>
                    b.BettingMethod is { } m ? $"{b.Number}={b.Amount},{m}" : $"{b.Number}={b.Amount}"));
            }
            SettingsBetText = BetTextByType.GetValueOrDefault(SettingsBetType, "");

            // 행렬: 모든 타입의 셀·행규칙 텍스트를 미리 만들어 둔다.
            foreach (var (val, _) in MatrixTypeOptions)
            {
                var cells = await Db.MatrixCells.AsNoTracking()
                    .Where(c => c.UserId == id && c.BetAmountType == val)
                    .OrderBy(c => c.RowIndex).ThenBy(c => c.ColIndex).ToListAsync();
                MatrixCellsByType[val] = BuildMatrixCellsText(cells);

                var rules = await Db.MatrixRowRules.AsNoTracking()
                    .Where(r => r.UserId == id && r.BetAmountType == val)
                    .OrderBy(r => r.RowIndex).ToListAsync();
                MatrixRulesByType[val] = string.Join("\n", rules.Select(rr =>
                    $"{rr.RowIndex}={rr.WinCount},{rr.TargetRow},{rr.WinMode},{rr.RestMinutes},{rr.FirstRowCapAmount:0.##}"));

                // 표 입력용 그리드 데이터
                var cg = new Dictionary<string, string>();
                foreach (var c in cells)
                    cg[$"{c.RowIndex}_{c.ColIndex}"] = c.Amount.ToString("0.##");
                MatrixCellGrid[val] = cg;

                var rg = new Dictionary<int, string[]>();
                foreach (var r2 in rules)
                    rg[r2.RowIndex] = new[]
                    {
                        r2.WinCount > 0 ? r2.WinCount.ToString() : "",
                        r2.TargetRow > 0 ? r2.TargetRow.ToString() : "",
                        r2.WinMode == 1 ? "1" : "",
                        r2.RestMinutes > 0 ? r2.RestMinutes.ToString() : "",
                        r2.FirstRowCapAmount > 0 ? r2.FirstRowCapAmount.ToString("0.##") : ""
                    };
                MatrixRuleGrid[val] = rg;
            }
            SettingsMatrixCellsText = MatrixCellsByType.GetValueOrDefault(SettingsMatrixType, "");
            SettingsMatrixRulesText = MatrixRulesByType.GetValueOrDefault(SettingsMatrixType, "");

            // 예외서식(한 줄 = 하나)
            var exFormats = await Db.ExceptionFormats.AsNoTracking()
                .Where(f => f.UserId == id).OrderBy(f => f.Number)
                .Select(f => f.Pattern).ToListAsync();
            SettingsExceptionText = string.Join("\n", exFormats.Where(t => !string.IsNullOrWhiteSpace(t)));

            // 방게이트: GateType 0=열기(On), 1=닫기(Off)
            var gates = await Db.RoomGateFormats.AsNoTracking()
                .Where(f => f.UserId == id).OrderBy(f => f.Number).ToListAsync();
            SettingsRoomGateOnText = string.Join("\n", gates.Where(g => g.GateType == 0).Select(g => g.Pattern).Where(p => !string.IsNullOrWhiteSpace(p)));
            SettingsRoomGateOffText = string.Join("\n", gates.Where(g => g.GateType == 1).Select(g => g.Pattern).Where(p => !string.IsNullOrWhiteSpace(p)));

            // 한도 · 방쿨다운(단일 객체)
            SettingsLimits = await Db.LimitSettings.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == id);
            SettingsCooldown = await Db.RoomCooldownSettings.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == id);

            // 전략 체인(스텝 포함)
            SettingsChains = await Db.StrategyChains.AsNoTracking()
                .Include(c => c.Steps)
                .Where(c => c.UserId == id).OrderBy(c => c.Id).ToListAsync();
        }
    }
}
