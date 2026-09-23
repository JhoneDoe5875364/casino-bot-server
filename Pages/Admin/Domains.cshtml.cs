using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

/// <summary>
/// Read-only view of where each member's bot is currently connected.
/// <para>
/// This page used to offer edit boxes for the domain, WebSocket URL and balance selector, but the bot
/// discovers all three itself and overwrites whatever is typed here the moment it connects. What an
/// operator can actually use is the opposite: which site each account is playing on, and when it was
/// last seen. Split-betting slots belong in the bot instead, since only the member knows which casino
/// accounts they hold and only they can log in to them.
/// </para>
/// </summary>
public class DomainsModel(AppDbContext db, HierarchyService tree) : AdminPageModel(db, tree)
{
    /// <summary>A bot that has not reported in for this long is treated as offline.</summary>
    static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

    public record Row(
        string Username,
        string DisplayName,
        UserRole Role,
        int SiteSlot,
        string? Domain,
        string? WebSocketUrl,
        DateTime? UpdatedUtc,
        DateTime? LastHeartbeatUtc)
    {
        public bool IsOnline => LastHeartbeatUtc is { } h && DateTime.UtcNow - h < OnlineWindow;
    }

    public List<Row> Rows { get; set; } = [];

    public int MemberCount { get; set; }

    public int OnlineCount => Rows.Where(r => r.SiteSlot == 0).Count(r => r.IsOnline);

    public async Task OnGetAsync()
    {
        var users = await Tree.VisibleUsers(Me).AsNoTracking()
            .OrderBy(u => u.TreePath).ThenBy(u => u.Username).ToListAsync();
        MemberCount = users.Count;

        var ids = users.Select(u => u.Id).ToList();
        var sites = await Db.DomainSites.AsNoTracking()
            .Where(s => ids.Contains(s.UserId))
            .ToListAsync();

        Rows = users.SelectMany(u =>
        {
            var mine = sites.Where(s => s.UserId == u.Id).OrderBy(s => s.SiteSlot).ToList();
            if (mine.Count == 0)
            {
                // never connected — still worth a row so the account is not simply absent
                return new[] { new Row(u.Username, u.DisplayName, u.Role, 0, null, null, null, u.LastHeartbeatUtc) };
            }
            return mine.Select(s => new Row(
                u.Username, u.DisplayName, u.Role, s.SiteSlot, s.Domain, s.WebSocketUrl,
                s.UpdatedUtc, u.LastHeartbeatUtc));
        }).ToList();
    }

    public static string Ago(DateTime? utc)
    {
        if (utc is not { } t)
            return "기록 없음";
        var d = DateTime.UtcNow - t;
        if (d < TimeSpan.FromMinutes(1)) return "방금 전";
        if (d < TimeSpan.FromHours(1)) return $"{(int)d.TotalMinutes}분 전";
        if (d < TimeSpan.FromDays(1)) return $"{(int)d.TotalHours}시간 전";
        return PragmaticBot.Server.Services.KoreaTime.Fmt(t, "yyyy-MM-dd HH:mm");
    }
}
