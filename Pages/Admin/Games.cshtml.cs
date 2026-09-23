using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

public class GamesModel(AppDbContext db, HierarchyService tree) : AdminPageModel(db, tree)
{
    public List<Game> Games { get; set; } = [];
    public Dictionary<int, int> SelectionCounts { get; set; } = [];

    /// <summary>Row counts offered in the page-size selector.</summary>
    public static readonly int[] PageSizeChoices = [25, 50, 100, 200, 500];

    /// <summary>Which provider's tab is open. Kept in the URL so an action can return to it.</summary>
    [BindProperty(SupportsGet = true)] public string? Provider { get; set; }

    /// <summary>Query key is "p": Razor Pages reserves "page" for the page path itself.</summary>
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;

    /// <summary>Rows per page, clamped to <see cref="PageSizeChoices"/>.</summary>
    [BindProperty(SupportsGet = true, Name = "size")] public int PageSize { get; set; } = 50;

    /// <summary>Tables in the open tab, before paging.</summary>
    public int TotalCount { get; set; }

    public int TotalPages { get; set; } = 1;

    public string PageUrl(IUrlHelper url, int page) =>
        url.Page("/Admin/Games", new { provider = Provider, size = PageSize, p = page })!;

    /// <summary>Provider key → table count, for the tab strip.</summary>
    public List<(string Key, int Count)> Tabs { get; set; } = [];

    /// <summary>Only the open tab's tables. One long mixed list made the page unreadable.</summary>
    public List<Game> Visible { get; set; } = [];

    static string KeyOf(Game g) => string.IsNullOrWhiteSpace(g.Provider) ? "기타" : g.Provider!;

    /// <summary>Korean name for a provider key; unknown keys are shown as-is.</summary>
    public static string ProviderLabel(string key) => key switch
    {
        "PragmaticPlay" => "프라그마틱",
        "Evolution" => "에볼루션",
        _ => key
    };

    public async Task<IActionResult> OnGetAsync()
    {
        if (RequireOwner() is { } deny)
            return deny;
        await LoadAsync();
        return Page();
    }

    [OwnerOnly]
    public async Task<IActionResult> OnPostCreateAsync(
        string gameCode, string title, string? titleKo, string? ppTableId, string? provider, int sortOrder)
    {
        gameCode = (gameCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(gameCode) || string.IsNullOrWhiteSpace(title))
        {
            TempData["Err"] = "게임 코드와 제목은 필수입니다.";
            return RedirectToPage(new { provider = Provider, size = PageSize, p = PageNo });
        }

        if (await Db.Games.AnyAsync(g => g.GameCode == gameCode))
        {
            TempData["Err"] = $"이미 존재하는 게임 코드입니다: {gameCode}";
            return RedirectToPage(new { provider = Provider, size = PageSize, p = PageNo });
        }

        Db.Games.Add(new Game
        {
            GameCode = gameCode,
            Title = title.Trim(),
            TitleKo = Blank(titleKo),
            PpTableId = Blank(ppTableId),
            Provider = Blank(provider) ?? "PragmaticPlay",
            Type = "baccarat",
            SortOrder = sortOrder,
            IsEnabled = true
        });
        await Db.SaveChangesAsync();

        TempData["Ok"] = $"게임을 추가했습니다: {title}";
        return RedirectToPage(new { provider = Provider, size = PageSize, p = PageNo });
    }

    [OwnerOnly]
    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var game = await Db.Games.FindAsync(id);
        if (game is null)
            return NotFound();

        game.IsEnabled = !game.IsEnabled;
        await Db.SaveChangesAsync();
        TempData["Ok"] = $"{game.Title}: {(game.IsEnabled ? "노출" : "숨김")}으로 변경했습니다.";
        return RedirectToPage(new { provider = Provider, size = PageSize, p = PageNo });
    }

    [OwnerOnly]
    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var game = await Db.Games.FindAsync(id);
        if (game is null)
            return NotFound();

        await Db.UserGameSelections.Where(s => s.GameId == id).ExecuteDeleteAsync();
        Db.Games.Remove(game);
        await Db.SaveChangesAsync();

        TempData["Ok"] = $"게임을 삭제했습니다: {game.Title}";
        return RedirectToPage(new { provider = Provider, size = PageSize, p = PageNo });
    }

    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    async Task LoadAsync()
    {
        Games = await Db.Games.AsNoTracking()
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
            .ToListAsync();

        SelectionCounts = await Db.UserGameSelections.AsNoTracking()
            .Where(s => s.IsSelected)
            .GroupBy(s => s.GameId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        Tabs = Games.GroupBy(KeyOf)
                    .Select(g => (Key: g.Key, Count: g.Count()))
                    .OrderBy(t => t.Key, StringComparer.Ordinal)
                    .ToList();

        // fall back to the first tab when the URL names a provider that has no tables
        if (string.IsNullOrWhiteSpace(Provider) || Tabs.All(t => t.Key != Provider))
            Provider = Tabs.Count > 0 ? Tabs[0].Key : null;

        if (!PageSizeChoices.Contains(PageSize))
            PageSize = 50;

        var inTab = Games.Where(g => KeyOf(g) == Provider)
                         .OrderBy(g => g.SortOrder).ThenBy(g => g.Id)
                         .ToList();

        TotalCount = inTab.Count;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        PageNo = Math.Clamp(PageNo, 1, TotalPages);

        Visible = inTab.Skip((PageNo - 1) * PageSize).Take(PageSize).ToList();
    }
}
