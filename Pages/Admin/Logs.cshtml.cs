using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

public class LogsModel(AppDbContext db, HierarchyService tree) : AdminPageModel(db, tree)
{
    /// <summary>Row counts offered in the page-size selector.</summary>
    public static readonly int[] PageSizeChoices = [50, 100, 200, 500, 1000];

    [BindProperty(SupportsGet = true)] public int? UserId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Level { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    /// <summary>Query key is "p": Razor Pages reserves "page" for the page path itself.</summary>
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;

    /// <summary>Rows per page, clamped to <see cref="PageSizeChoices"/> so a crafted URL cannot ask for everything.</summary>
    [BindProperty(SupportsGet = true, Name = "size")] public int PageSize { get; set; } = 100;

    public List<AppLog> Rows { get; set; } = [];
    public List<User> AllUsers { get; set; } = [];
    public Dictionary<int, string> UserNames { get; set; } = [];
    public int TotalCount { get; set; }
    public int TotalPages { get; set; } = 1;

    public string PageUrl(IUrlHelper url, int page) => url.Page("/Admin/Logs", RouteValues(page))!;

    object RouteValues(int? page = null) => new
    {
        userId = UserId,
        level = Level,
        category = Category,
        q = Q,
        size = PageSize,
        p = page ?? PageNo
    };

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        if (!PageSizeChoices.Contains(PageSize))
            PageSize = 100;

        AllUsers = await Tree.VisibleUsers(Me).AsNoTracking()
            .OrderBy(u => u.TreePath).ThenBy(u => u.Username).ToListAsync();
        UserNames = AllUsers.ToDictionary(u => u.Id, u => u.Username);

        var visibleIds = AllUsers.Select(u => u.Id).ToList();
        var q = Db.AppLogs.AsNoTracking()
            .Where(l => l.UserId != null && visibleIds.Contains(l.UserId.Value));

        if (UserId is { } uid && visibleIds.Contains(uid))
            q = q.Where(l => l.UserId == uid);
        if (!string.IsNullOrEmpty(Level))
            q = q.Where(l => l.Level == Level);
        if (!string.IsNullOrWhiteSpace(Category))
            q = q.Where(l => l.Category != null && l.Category.Contains(Category));
        if (!string.IsNullOrWhiteSpace(Q))
            q = q.Where(l => l.Message.Contains(Q));

        TotalCount = await q.CountAsync();
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        PageNo = Math.Clamp(PageNo, 1, TotalPages);

        Rows = await q.OrderByDescending(l => l.Id)
            .Skip((PageNo - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }

    /// <summary>
    /// Deletes the ticked rows. Restricted to logs belonging to accounts the caller can see, so a
    /// 대리점 cannot reach another branch's records by posting arbitrary ids.
    /// </summary>
    public async Task<IActionResult> OnPostDeleteAsync(long[] ids)
    {
        return await DeleteByIdsAsync(ids, "선택한 로그가 없습니다.");
    }

    public async Task<IActionResult> OnPostDeleteOneAsync(long id)
    {
        return await DeleteByIdsAsync([id], "삭제할 로그를 찾지 못했습니다.");
    }

    async Task<IActionResult> DeleteByIdsAsync(long[] ids, string emptyMessage)
    {
        if (ids is null || ids.Length == 0)
        {
            TempData["Err"] = emptyMessage;
            return RedirectToPage(RouteValues());
        }

        var visibleIds = await Tree.VisibleUsers(Me).AsNoTracking().Select(u => u.Id).ToListAsync();
        var removed = await Db.AppLogs
            .Where(l => ids.Contains(l.Id) && l.UserId != null && visibleIds.Contains(l.UserId.Value))
            .ExecuteDeleteAsync();

        if (removed < ids.Length)
            TempData["Err"] = $"{ids.Length - removed}건은 권한이 없어 삭제하지 못했습니다.";
        TempData["Ok"] = $"{removed:N0}건을 삭제했습니다.";
        return RedirectToPage(RouteValues());
    }

    /// <summary>Deletes everything the current filter matches, not just the page on screen.</summary>
    public async Task<IActionResult> OnPostDeleteFilteredAsync()
    {
        var visibleIds = await Tree.VisibleUsers(Me).AsNoTracking().Select(u => u.Id).ToListAsync();
        var q = Db.AppLogs.Where(l => l.UserId != null && visibleIds.Contains(l.UserId.Value));

        if (UserId is { } uid && visibleIds.Contains(uid))
            q = q.Where(l => l.UserId == uid);
        if (!string.IsNullOrEmpty(Level))
            q = q.Where(l => l.Level == Level);
        if (!string.IsNullOrWhiteSpace(Category))
            q = q.Where(l => l.Category != null && l.Category.Contains(Category));
        if (!string.IsNullOrWhiteSpace(Q))
            q = q.Where(l => l.Message.Contains(Q));

        var removed = await DeleteInBatchesAsync(q);
        TempData["Ok"] = $"조건에 맞는 로그 {removed:N0}건을 삭제했습니다.";
        return RedirectToPage(RouteValues(1));
    }

    public async Task<IActionResult> OnPostPurgeAsync()
    {
        if (!IsOwner)
        {
            TempData["Err"] = "로그 정리는 본사 계정만 할 수 있습니다.";
            return RedirectToPage(RouteValues());
        }

        var cutoff = DateTime.UtcNow.AddDays(-30);
        var removed = await DeleteInBatchesAsync(Db.AppLogs.Where(l => l.CreatedUtc < cutoff));
        TempData["Ok"] = $"{removed:N0}건의 오래된 로그를 삭제했습니다.";
        return RedirectToPage(RouteValues());
    }

    /// <summary>
    /// 조건에 맞는 로그를 PK(Id) 순서로 <paramref name="batchSize"/>건씩 나눠 삭제한다.
    /// <para>
    /// AppLogs 가 수백만 건일 때 단일 <c>ExecuteDeleteAsync()</c> 는 한 문장으로 표 전체를 훑어
    /// 30초 CommandTimeout 을 넘겨 실패했다("Command Timeout expired"). ID 목록을 한 덩어리씩
    /// 뽑아 PK 로 지우면 각 DELETE 가 인덱스로 빠르게 끝나 타임아웃에 걸리지 않는다.
    /// </para>
    /// </summary>
    async Task<int> DeleteInBatchesAsync(IQueryable<AppLog> q, int batchSize = 5000)
    {
        // 배치 자체는 짧지만, 큰 표에서 다음 배치 ID 를 뽑는 SELECT 가 스캔일 수 있어 넉넉히 둔다.
        Db.Database.SetCommandTimeout(180);
        var total = 0;
        while (true)
        {
            var ids = await q.OrderBy(l => l.Id).Select(l => l.Id).Take(batchSize).ToListAsync();
            if (ids.Count == 0)
                break;
            total += await Db.AppLogs.Where(l => ids.Contains(l.Id)).ExecuteDeleteAsync();
            if (ids.Count < batchSize)
                break;
        }
        return total;
    }
}
