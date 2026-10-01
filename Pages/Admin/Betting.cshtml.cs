using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

public class BettingModel(AppDbContext db, HierarchyService tree) : AdminPageModel(db, tree)
{
    /// <summary>Row counts offered in the page-size selector.</summary>
    public static readonly int[] PageSizeChoices = [50, 100, 200, 500, 1000];

    [BindProperty(SupportsGet = true)] public int? UserId { get; set; }

    /// <summary>Limit to one 총판/대리점 and everything under it.</summary>
    [BindProperty(SupportsGet = true)] public int? BranchOf { get; set; }

    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public bool IncludeVirtual { get; set; }
    /// <summary>Query key is "p": Razor Pages reserves "page" for the page path itself.</summary>
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;

    /// <summary>Rows per page, clamped to <see cref="PageSizeChoices"/> so a crafted URL cannot ask for everything.</summary>
    [BindProperty(SupportsGet = true, Name = "size")] public int PageSize { get; set; } = 100;

    public List<BettingHistory> Rows { get; set; } = [];
    public List<User> MemberChoices { get; set; } = [];
    public List<User> BranchChoices { get; set; } = [];
    public Dictionary<int, string> UserNames { get; set; } = [];

    /// <summary>Branch id → subtree path prefix, used to filter the member list in the browser.</summary>
    public Dictionary<int, string> BranchPrefixes { get; set; } = [];

    /// <summary>Member id → its TreePath, matched against a branch prefix.</summary>
    public Dictionary<int, string> MemberPaths { get; set; } = [];
    public string? BranchLabel { get; set; }

    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;
    public decimal TotalBet { get; set; }
    public decimal TotalProfit { get; set; }
    public int WinCount { get; set; }
    public int LossCount { get; set; }
    public int TieCount { get; set; }

    /// <summary>Rounds that never got a result — they carry no profit, so they must not read as losses.</summary>
    public int PendingCount { get; set; }

    /// <summary>One plotted point of the cumulative-profit line.</summary>
    public record ChartPoint(string Label, decimal Cumulative, decimal Delta, double X, double Y);

    public List<ChartPoint> Series { get; set; } = [];

    /// <summary>Most points the chart will draw; beyond this, consecutive bets are merged.</summary>
    const int MaxPoints = 400;

    /// <summary>How many bets the line covers, before any merging.</summary>
    public int TotalPoints { get; set; }

    /// <summary>True when bets had to be merged to stay under <see cref="MaxPoints"/>.</summary>
    public bool IsCondensed { get; set; }

    /// <summary>Individual dots get unreadable past this many points, so only the line is drawn.</summary>
    public bool ShowDots => Series.Count <= 120;

    /// <summary>Wallet balance before the first bet in range — where the line starts.</summary>
    public decimal StartBalance { get; set; }

    /// <summary>Balance after the last bet settles. Start + total profit.</summary>
    public decimal EndBalance { get; set; }

    /// <summary>
    /// 봇의 현재 잔액을, 세션 종료값이나 이익누적이 아니라 "가장 최근 베팅이력 한 줄"에서 바로 복원한 값.
    /// BalanceAfter 는 스테이크 차감 직후(당첨 미반영) 값이라, 정산금(WinAmount: 당첨=원금+상금, 무=원금,
    /// 패=0)을 더하면 정산 후 실제 잔액이 된다 — Won/Lost/Tie 모두 정확히 맞는다. 날짜/상태 필터와
    /// 무관하게 그 회원의 최신 1건을 쓰므로 봇의 지금 잔액에 가장 근접한다(단일 회원 선택 시에만).
    /// </summary>
    public decimal? CurrentBalance { get; set; }

    public DateTime? FirstBetUtc { get; set; }
    public DateTime? LastBetUtc { get; set; }

    /// <summary>Wall-clock span from the first bet to the last, as "3시간 25분".</summary>
    public string ElapsedText
    {
        get
        {
            if (FirstBetUtc is not { } a || LastBetUtc is not { } b || b <= a)
                return "-";
            var d = b - a;
            if (d.TotalMinutes < 1) return "1분 미만";
            if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}분";
            if (d.TotalDays < 1) return $"{(int)d.TotalHours}시간 {d.Minutes}분";
            return $"{(int)d.TotalDays}일 {d.Hours}시간 {d.Minutes}분";
        }
    }

    /// <summary>
    /// 베팅 이익률 = 베팅 순이익 ÷ 실질 원금. 예전엔 '첫 베팅 잔액'(계정 초기의 아주 작은 값,
    /// 예 34,978원)을 분모로 써서 9,600% 같은 비현실적 값이 나왔다. 실질 원금은 '현재 잔액 − 베팅
    /// 순이익'(= 입금 등으로 넣은 자본)으로 잡아, 넣은 돈 대비 얼마 벌었는지를 나타낸다. 필터를 걸면
    /// 그 구간의 자본 대비 수익률이 된다(입금 없는 구간이면 곧 '구간 시작잔액 대비 수익률'과 같다).
    /// </summary>
    public decimal? ProfitPercent
    {
        get
        {
            decimal capital = EndBalance - TotalProfit;   // 베팅이익을 뺀 실질 원금(입금 포함)
            return capital > 0m ? Math.Round(TotalProfit / capital * 100m, 2) : null;
        }
    }

    /// <summary>
    /// The plotted points as JSON for the hover crosshair. Handed to the browser in a data attribute
    /// rather than an inline script so nothing has to be escaped twice.
    /// </summary>
    public string SeriesJson => System.Text.Json.JsonSerializer.Serialize(
        Series.Select(p => new { l = p.Label, c = p.Cumulative, d = p.Delta, x = p.X, y = p.Y }));

    public decimal SeriesMin { get; set; }
    public decimal SeriesMax { get; set; }

    /// <summary>Y of the zero line in the same 0-100 space as the points, for the baseline.</summary>
    public double ZeroY { get; set; }

    public string PageUrl(IUrlHelper url, int page) => url.Page("/Admin/Betting", RouteValues(page))!;

    object RouteValues(int? page = null) => new
    {
        userId = UserId,
        branchOf = BranchOf,
        from = From?.ToString("yyyy-MM-dd"),
        to = To?.ToString("yyyy-MM-dd"),
        status = Status,
        includeVirtual = IncludeVirtual,
        size = PageSize,
        p = page ?? PageNo
    };

    public async Task OnGetAsync()
    {
        if (!PageSizeChoices.Contains(PageSize))
            PageSize = 100;

        var visible = await Tree.VisibleUsers(Me).AsNoTracking()
            .OrderBy(u => u.TreePath).ThenBy(u => u.Username)
            .ToListAsync();

        UserNames = visible.ToDictionary(u => u.Id, u => u.Username);
        MemberChoices = visible.Where(u => u.Role == UserRole.Member).ToList();
        BranchChoices = visible.Where(u => u.Role > UserRole.Member && u.Id != Me.Id).ToList();

        // Each branch's subtree prefix, so the 회원 dropdown can be narrowed to the chosen 조직.
        // The two lists used to be independent, which let an operator pick a member from outside the
        // selected branch and get an empty result with no hint as to why.
        BranchPrefixes = BranchChoices.ToDictionary(
            b => b.Id, HierarchyService.SubtreePrefixOf);
        MemberPaths = MemberChoices.ToDictionary(u => u.Id, u => u.TreePath);

        // start from everything I can see, then narrow
        var allowed = visible.Select(u => u.Id).ToHashSet();

        if (BranchOf is { } branchId && visible.FirstOrDefault(u => u.Id == branchId) is { } head)
        {
            var prefix = HierarchyService.SubtreePrefixOf(head);
            allowed = visible
                .Where(u => u.Id == head.Id || u.TreePath.StartsWith(prefix))
                .Select(u => u.Id)
                .ToHashSet();
            BranchLabel = $"{HierarchyService.Label(head.Role)} {head.Username} 산하";
        }

        if (UserId is { } uid && allowed.Contains(uid))
            allowed = [uid];
        else
            // a member from outside the chosen 조직 (or a stale URL): drop it rather than filter by a
            // combination that can never match, which would look like "no data" instead of a bad filter
            UserId = null;

        var ids = allowed.ToList();
        var q = Db.BettingHistories.AsNoTracking().Where(b => ids.Contains(b.UserId));

        if (!IncludeVirtual)
            q = q.Where(b => !b.IsVirtual);
        if (From is { } from)
            q = q.Where(b => b.CreatedUtc >= PragmaticBot.Server.Services.KoreaTime.DateToUtc(from.Date));
        if (To is { } to)
            q = q.Where(b => b.CreatedUtc < PragmaticBot.Server.Services.KoreaTime.DateToUtc(to.Date.AddDays(1)));
        if (!string.IsNullOrEmpty(Status))
            q = q.Where(b => b.Status == Status);

        TotalCount = await q.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        PageNo = Math.Clamp(PageNo, 1, TotalPages);

        TotalBet = await q.SumAsync(b => (decimal?)b.BetAmount) ?? 0m;
        TotalProfit = await q.SumAsync(b => (decimal?)b.Profit) ?? 0m;
        // The bot writes "Won"/"Lost" (PragmaticAutoBettingService), not "Win"/"Loss" — counting the
        // latter is why 승/패/무 read 1/0/38 against 464 rows. "Win"/"Loss" stay in the test so a
        // handful of rows left by an older build still count.
        WinCount = await q.CountAsync(b => b.Status == "Won" || b.Status == "Win");
        LossCount = await q.CountAsync(b => b.Status == "Lost" || b.Status == "Loss");
        TieCount = await q.CountAsync(b => b.Status == "Tie");
        PendingCount = await q.CountAsync(b => b.Status == "Pending");

        Rows = await q.OrderByDescending(b => b.Id)
            .Skip((PageNo - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();

        // 현재 잔액: 단일 회원 선택 시, 그 회원의 최신 베팅이력 1건에서 정산 후 잔액을 복원한다.
        // 날짜/상태 필터를 타지 않고(그래야 '지금'을 보여줌) 가상 여부만 따른다.
        if (UserId is { } singleUid)
        {
            var latest = await Db.BettingHistories.AsNoTracking()
                .Where(b => b.UserId == singleUid && (IncludeVirtual || !b.IsVirtual))
                .OrderByDescending(b => b.Id)
                .Select(b => new { b.BalanceAfter, b.WinAmount })
                .FirstOrDefaultAsync();
            if (latest is not null)
                CurrentBalance = latest.BalanceAfter + (latest.WinAmount ?? 0m);
        }

        await BuildSeriesAsync(q);
    }

    /// <summary>
    /// Builds the wallet-balance line over every bet the filter matches, in order.
    /// <para>
    /// It used to plot cumulative profit, which starts at zero and says nothing about how much money
    /// was actually in play. The balance is reconstructed as the first bet's BalanceBefore plus the
    /// running profit — BalanceAfter cannot be used directly because the bot records it at stake time,
    /// before any payout lands. Above <see cref="MaxPoints"/> consecutive bets are merged into equal
    /// chunks so the line stays drawable; the curve still passes through the true value at every edge.
    /// </para>
    /// </summary>
    async Task BuildSeriesAsync(IQueryable<BettingHistory> q)
    {
        var bets = await q
            .OrderBy(b => b.CreatedUtc).ThenBy(b => b.Id)
            .Select(b => new { b.CreatedUtc, b.BalanceBefore, b.BalanceAfter, b.WinAmount })
            .ToListAsync();

        if (bets.Count == 0)
            return;

        TotalPoints = bets.Count;
        FirstBetUtc = bets[0].CreatedUtc;
        LastBetUtc = bets[^1].CreatedUtc;
        // 실제 기록된 잔액을 그대로 쓴다. 예전에는 '첫 베팅 잔액 + Σ이익' 으로 재구성했는데, 이 방식은
        // 입금 등 '베팅 외 잔액 변동'을 무시해 실제와 크게 어긋났다(예: 첫 베팅 34천원 + 이익 329만
        // = 332만으로 표시되지만 실제 잔액은 995만). 각 점의 잔액 = 그 베팅 정산 후 잔액(BalanceAfter +
        // 당첨금)이고, 이는 봇 내부원장(=실제 잔액, 입금 포함)을 그대로 따른다.
        StartBalance = bets[0].BalanceBefore;

        int chunk = (int)Math.Ceiling(bets.Count / (double)MaxPoints);
        IsCondensed = chunk > 1;

        var raw = new List<(string Label, decimal Balance, decimal Delta)>();
        decimal prevEdge = StartBalance;

        for (int i = 0; i < bets.Count; i++)
        {
            decimal pointBalance = bets[i].BalanceAfter + (bets[i].WinAmount ?? 0m);   // 정산 후 실제 잔액

            bool last = i == bets.Count - 1;
            if ((i + 1) % chunk == 0 || last)
            {
                raw.Add((PragmaticBot.Server.Services.KoreaTime.Fmt(bets[i].CreatedUtc, "MM-dd HH:mm:ss"), pointBalance, pointBalance - prevEdge));
                prevEdge = pointBalance;
            }
        }

        EndBalance = bets[^1].BalanceAfter + (bets[^1].WinAmount ?? 0m);

        // the starting balance is part of the story, so keep it inside the visible range
        SeriesMin = Math.Min(StartBalance, raw.Min(r => r.Balance));
        SeriesMax = Math.Max(StartBalance, raw.Max(r => r.Balance));
        var range = SeriesMax - SeriesMin;
        if (range == 0m)
            range = 1m;

        double Scale(decimal v) => 100d - (double)((v - SeriesMin) / range) * 100d;

        // the guide line is the starting balance: above it is profit, below it is loss
        ZeroY = Scale(StartBalance);
        Series = raw.Select((r, i) => new ChartPoint(
            r.Label, r.Balance, r.Delta,
            raw.Count == 1 ? 50d : i * 100d / (raw.Count - 1),
            Scale(r.Balance))).ToList();
    }

    /// <summary>
    /// Deletes the ticked rows, limited to accounts the caller can see so one branch cannot reach
    /// another's records by posting arbitrary ids.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteAsync(long[] ids)
        => await DeleteByIdsAsync(ids, "선택한 내역이 없습니다.");

    public async Task<IActionResult> OnPostDeleteOneAsync(long id)
        => await DeleteByIdsAsync([id], "삭제할 내역을 찾지 못했습니다.");

    async Task<IActionResult> DeleteByIdsAsync(long[] ids, string emptyMessage)
    {
        if (ids is null || ids.Length == 0)
        {
            TempData["Err"] = emptyMessage;
            return RedirectToPage(RouteValues());
        }

        var visibleIds = await Tree.VisibleUsers(Me).AsNoTracking().Select(u => u.Id).ToListAsync();
        var removed = await Db.BettingHistories
            .Where(b => ids.Contains(b.Id) && visibleIds.Contains(b.UserId))
            .ExecuteDeleteAsync();

        if (removed < ids.Length)
            TempData["Err"] = $"{ids.Length - removed}건은 권한이 없어 삭제하지 못했습니다.";
        TempData["Ok"] = $"{removed:N0}건을 삭제했습니다.";
        return RedirectToPage(RouteValues());
    }

    /// <summary>현재 조회 조건(조직/회원/기간/결과/가상)에 맞는 베팅 내역 전체를 삭제한다(내 산하만).</summary>
    public async Task<IActionResult> OnPostDeleteFilteredAsync()
    {
        var q = await BuildFilteredBetQueryAsync();
        var removed = await DeleteBetsInBatchesAsync(q);
        TempData["Ok"] = $"조건에 맞는 베팅 내역 {removed:N0}건을 삭제했습니다.";
        return RedirectToPage(RouteValues(1));
    }

    /// <summary>30일 이전 베팅 내역 정리(본사 전용).</summary>
    public async Task<IActionResult> OnPostPurgeAsync()
    {
        if (!IsOwner)
        {
            TempData["Err"] = "베팅 내역 정리는 본사 계정만 할 수 있습니다.";
            return RedirectToPage(RouteValues());
        }
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var removed = await DeleteBetsInBatchesAsync(Db.BettingHistories.Where(b => b.CreatedUtc < cutoff));
        TempData["Ok"] = $"{removed:N0}건의 오래된 베팅 내역을 삭제했습니다.";
        return RedirectToPage(RouteValues());
    }

    /// <summary>목록 필터와 동일한 조건으로 삭제 대상 쿼리를 만든다(권한 범위 안에서만).</summary>
    async Task<IQueryable<BettingHistory>> BuildFilteredBetQueryAsync()
    {
        var visible = await Tree.VisibleUsers(Me).AsNoTracking().ToListAsync();
        var allowed = visible.Select(u => u.Id).ToHashSet();
        if (BranchOf is { } branchId && visible.FirstOrDefault(u => u.Id == branchId) is { } head)
        {
            var prefix = HierarchyService.SubtreePrefixOf(head);
            allowed = visible.Where(u => u.Id == head.Id || u.TreePath.StartsWith(prefix)).Select(u => u.Id).ToHashSet();
        }
        if (UserId is { } uid && allowed.Contains(uid))
            allowed = [uid];

        var ids = allowed.ToList();
        var q = Db.BettingHistories.Where(b => ids.Contains(b.UserId));
        if (!IncludeVirtual)
            q = q.Where(b => !b.IsVirtual);
        if (From is { } from)
            q = q.Where(b => b.CreatedUtc >= PragmaticBot.Server.Services.KoreaTime.DateToUtc(from.Date));
        if (To is { } to)
            q = q.Where(b => b.CreatedUtc < PragmaticBot.Server.Services.KoreaTime.DateToUtc(to.Date.AddDays(1)));
        if (!string.IsNullOrEmpty(Status))
            q = q.Where(b => b.Status == Status);
        return q;
    }

    /// <summary>PK(Id) 순서로 배치 삭제(큰 표에서 타임아웃 방지). BettingAllocation은 FK Cascade로 자동 정리.</summary>
    async Task<int> DeleteBetsInBatchesAsync(IQueryable<BettingHistory> q, int batchSize = 5000)
    {
        Db.Database.SetCommandTimeout(180);
        var total = 0;
        while (true)
        {
            var ids = await q.OrderBy(b => b.Id).Select(b => b.Id).Take(batchSize).ToListAsync();
            if (ids.Count == 0)
                break;
            total += await Db.BettingHistories.Where(b => ids.Contains(b.Id)).ExecuteDeleteAsync();
            if (ids.Count < batchSize)
                break;
        }
        return total;
    }
}
