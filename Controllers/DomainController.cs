using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Contracts;
using PragmaticBot.Server.Data;

namespace PragmaticBot.Server.Controllers;

[Route("api/domain")]
public class DomainController(AppDbContext db) : ApiControllerBase
{
    /// <summary>Operator-wide provider settings (which casino domain the bot should open).</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string provider = "PragmaticPlay")
    {
        var setting = await db.DomainSettings.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Provider == provider);

        return Ok(setting is null
            ? new DomainSettingDto(null, null, provider)
            : new DomainSettingDto(setting.Domain, setting.WebSocketUrl, setting.Provider, setting.BalanceSelector));
    }

    [HttpGet("sites")]
    public async Task<IActionResult> GetSites([FromQuery] string provider = "PragmaticPlay")
    {
        var sites = await db.DomainSites.AsNoTracking()
            .Where(s => s.UserId == UserId && s.Provider == provider)
            .OrderBy(s => s.SiteSlot)
            .Select(s => new DomainSiteDto(
                s.SiteSlot, s.Domain, s.WebSocketUrl, s.BrowserType, s.DebugPort,
                s.SiteUrl, s.IsActive, s.Provider, s.BalanceSelector))
            .ToListAsync();
        return Ok(sites);
    }

    [HttpPost("sites")]
    public async Task<IActionResult> SaveSites([FromBody] List<DomainSiteDto> sites)
    {
        sites ??= [];
        var providers = sites.Select(s => s.Provider).Distinct().ToList();

        var existing = await db.DomainSites
            .Where(s => s.UserId == UserId && providers.Contains(s.Provider))
            .ToListAsync();

        foreach (var dto in sites)
        {
            var row = existing.FirstOrDefault(s => s.Provider == dto.Provider && s.SiteSlot == dto.SiteSlot);
            if (row is null)
            {
                row = new DomainSite { UserId = UserId, Provider = dto.Provider, SiteSlot = dto.SiteSlot };
                db.DomainSites.Add(row);
            }
            row.Domain = dto.Domain;
            row.WebSocketUrl = dto.WebSocketUrl;
            row.BrowserType = dto.BrowserType;
            row.DebugPort = dto.DebugPort;
            row.SiteUrl = dto.SiteUrl;
            row.IsActive = dto.IsActive;
            row.BalanceSelector = dto.BalanceSelector;
            row.UpdatedUtc = DateTime.UtcNow;
        }

        // slots the client dropped
        var keep = sites.Select(s => (s.Provider, s.SiteSlot)).ToHashSet();
        foreach (var row in existing.Where(r => !keep.Contains((r.Provider, r.SiteSlot))))
            db.DomainSites.Remove(row);

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>The bot reports the live game websocket URL it sniffed out.</summary>
    [HttpPut("websocket-url")]
    public async Task<IActionResult> UpdateWebSocketUrl(
        [FromBody] WebSocketUrlRequest request,
        [FromQuery] string provider = "PragmaticPlay",
        [FromQuery] int siteSlot = 0)
    {
        // Slot 0 used to land only in DomainSettings, which is a single row shared by every account —
        // each member's bot overwrote the previous one, so nobody could tell who was on which site.
        // That row is still written (other reads depend on it), but the per-user row is recorded too.
        var site = await db.DomainSites
            .FirstOrDefaultAsync(s => s.UserId == UserId && s.Provider == provider && s.SiteSlot == siteSlot);
        if (site is null)
        {
            site = new DomainSite { UserId = UserId, Provider = provider, SiteSlot = siteSlot };
            db.DomainSites.Add(site);
        }
        site.WebSocketUrl = request.WebSocketUrl;
        site.Domain = HostOf(request.WebSocketUrl) ?? site.Domain;
        site.UpdatedUtc = DateTime.UtcNow;

        if (siteSlot == 0)
        {
            var setting = await EnsureSettingAsync(provider);
            setting.WebSocketUrl = request.WebSocketUrl;
            setting.UpdatedUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Host of a ws(s):// URL, so the admin board can show a domain instead of a long query string.</summary>
    static string? HostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : null;
    }

    /// <summary>The bot reports the CSS selector it found the balance in.</summary>
    [HttpPut("balance-selector")]
    public async Task<IActionResult> UpdateBalanceSelector(
        [FromBody] BalanceSelectorRequest request,
        [FromQuery] string provider = "PragmaticPlay")
    {
        var setting = await EnsureSettingAsync(provider);
        setting.BalanceSelector = request.Selector;
        setting.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    async Task<DomainSetting> EnsureSettingAsync(string provider)
    {
        var setting = await db.DomainSettings.FirstOrDefaultAsync(d => d.Provider == provider);
        if (setting is null)
        {
            setting = new DomainSetting { Provider = provider };
            db.DomainSettings.Add(setting);
        }
        return setting;
    }
}
