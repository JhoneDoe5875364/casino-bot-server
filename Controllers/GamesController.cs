using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Contracts;
using PragmaticBot.Server.Data;

namespace PragmaticBot.Server.Controllers;

[Route("api/games")]
public class GamesController(AppDbContext db) : ApiControllerBase
{
    /// <summary>
    /// Tables for 룸 설정. <paramref name="provider"/> accepts a comma-separated list so an account
    /// granted both providers gets one merged catalog; it used to be a single value defaulting to
    /// Pragmatic Play, which is why an Evolution-only account was still shown Pragmatic tables.
    /// </summary>
    [HttpGet("baccarat")]
    public async Task<IActionResult> GetBaccaratGames([FromQuery] string provider = "PragmaticPlay")
    {
        var wanted = (provider ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (wanted.Count == 0)
            wanted.Add("PragmaticPlay");

        return Ok(await db.Games.AsNoTracking()
            .Where(g => g.IsEnabled && g.Provider != null && wanted.Contains(g.Provider))
            .OrderBy(g => g.Provider).ThenBy(g => g.SortOrder).ThenBy(g => g.Id)
            .Select(g => new GameDto(g.Id, g.GameCode, g.Title, g.TitleKo, g.Type, g.Vendor, g.Provider, g.PpTableId))
            .ToListAsync());
    }

    /// <summary>
    /// The bot reports tables it saw in live lobby traffic. New ones are added to the catalog so
    /// a room the bot can actually see is never missing from 룸 설정; existing ones are left alone
    /// unless they are still auto-discovered and we now have a better title.
    /// </summary>
    [HttpPost("discovered")]
    public async Task<IActionResult> ReportDiscovered(
        [FromBody] List<DiscoveredGameDto> games,
        [FromQuery] string provider = "PragmaticPlay")
    {
        // whichever site the bot was watching when it saw these tables
        if (string.IsNullOrWhiteSpace(provider))
            provider = "PragmaticPlay";

        if (games is null || games.Count == 0)
            return Ok(new DiscoveredGamesResult(0, 0, await db.Games.CountAsync()));

        var incoming = games
            .Where(g => !string.IsNullOrWhiteSpace(g.PpTableId))
            .GroupBy(g => g.PpTableId.Trim())
            .Select(grp => grp.Last())
            .ToList();

        var ids = incoming.Select(g => g.PpTableId.Trim()).ToList();
        var existing = await db.Games
            .Where(g => g.PpTableId != null && ids.Contains(g.PpTableId) && g.Provider == provider)
            .ToListAsync();

        var maxOrder = await db.Games.AnyAsync() ? await db.Games.MaxAsync(g => g.SortOrder) : 0;
        int added = 0, updated = 0;

        foreach (var dto in incoming)
        {
            var id = dto.PpTableId.Trim();
            var title = string.IsNullOrWhiteSpace(dto.Title) ? null : dto.Title.Trim();
            var row = existing.FirstOrDefault(g => g.PpTableId == id);

            if (row is null)
            {
                db.Games.Add(new Game
                {
                    // GameCode is unique table-wide, so it is namespaced by provider
                    GameCode = provider == "PragmaticPlay" ? id : $"{provider}:{id}",
                    PpTableId = id,
                    Title = title ?? id,
                    TitleKo = title,
                    Type = string.IsNullOrWhiteSpace(dto.TableType) ? "baccarat" : dto.TableType.Trim(),
                    Provider = provider,
                    SortOrder = ++maxOrder,
                    IsEnabled = true,
                    IsAutoDiscovered = true,
                    DiscoveredUtc = DateTime.UtcNow
                });
                added++;
            }
            else if (row.IsAutoDiscovered && title is not null && row.Title == row.GameCode)
            {
                // first time we learn the real name of a table we only knew by id
                row.Title = title;
                row.TitleKo = title;
                updated++;
            }
        }

        if (added > 0 || updated > 0)
            await db.SaveChangesAsync();

        return Ok(new DiscoveredGamesResult(added, updated, await db.Games.CountAsync()));
    }

    [HttpGet("selections")]
    public async Task<IActionResult> GetSelections() =>
        Ok(await db.UserGameSelections.AsNoTracking()
            .Where(s => s.UserId == UserId)
            .OrderBy(s => s.GameId)
            .Select(s => new GameSelectionDto(s.GameId, s.IsSelected))
            .ToListAsync());

    [HttpPost("selections")]
    public async Task<IActionResult> SaveSelections([FromBody] List<GameSelectionDto> selections)
    {
        selections ??= [];
        var existing = await db.UserGameSelections.Where(s => s.UserId == UserId).ToListAsync();

        foreach (var dto in selections)
        {
            var row = existing.FirstOrDefault(s => s.GameId == dto.GameId);
            if (row is null)
                db.UserGameSelections.Add(new UserGameSelection
                {
                    UserId = UserId,
                    GameId = dto.GameId,
                    IsSelected = dto.IsSelected
                });
            else
                row.IsSelected = dto.IsSelected;
        }

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }
}

[Route("api/notice")]
public class NoticeController(AppDbContext db) : ApiControllerBase
{
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var notice = await db.Notices.AsNoTracking()
            .Where(n => n.IsActive)
            .OrderByDescending(n => n.CreatedUtc)
            .FirstOrDefaultAsync();

        return Ok(new ActiveNoticeResponse(
            notice is not null,
            notice is null ? null : new NoticeDto(notice.Id, notice.Message, notice.CreatedUtc)));
    }
}

[Route("api/logs")]
public class LogsController(AppDbContext db) : ApiControllerBase
{
    /// <summary>The bot batches its own log lines here every few seconds.</summary>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] List<AppLogDto> logs)
    {
        if (logs is null || logs.Count == 0)
            return Ok(new { ok = true });

        db.AppLogs.AddRange(logs.Select(l => new AppLog
        {
            UserId = UserId,
            Level = Trim(l.Level, 32) ?? "",
            Category = Trim(l.Category, 64),
            Message = Trim(l.Message, 2000) ?? "",
            StackTrace = Trim(l.StackTrace, 4000),
            AdditionalData = Trim(l.AdditionalData, 4000)
        }));

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    static string? Trim(string? s, int max) =>
        s is null ? null : s.Length <= max ? s : s[..max];
}
