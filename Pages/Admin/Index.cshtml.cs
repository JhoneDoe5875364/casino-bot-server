using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

public class IndexModel(AppDbContext db, HierarchyService tree) : AdminPageModel(db, tree)
{
    public int TotalMembers { get; set; }
    public int ActiveMembers { get; set; }
    public int OnlineMembers { get; set; }
    public int ExpiringSoon { get; set; }
    public int Distributors { get; set; }
    public int Agencies { get; set; }
    public int TodayBets { get; set; }
    public decimal TodayProfit { get; set; }
    public List<User> Online { get; set; } = [];
    public List<AppLog> RecentErrors { get; set; } = [];
    public Dictionary<int, string> UserNames { get; set; } = [];

    public async Task OnGetAsync()
    {
        var now = DateTime.UtcNow;
        var onlineSince = now.AddMinutes(-5);
        var todayStart = PragmaticBot.Server.Services.KoreaTime.DateToUtc(PragmaticBot.Server.Services.KoreaTime.Now.Date);

        // everything below me — my own row never counts as a managed member
        var branch = Tree.Descendants(Me).AsNoTracking();

        TotalMembers = await branch.CountAsync(u => u.Role == UserRole.Member);
        ActiveMembers = await branch.CountAsync(u => u.Role == UserRole.Member && u.IsActive);
        Distributors = await branch.CountAsync(u => u.Role == UserRole.Distributor);
        Agencies = await branch.CountAsync(u => u.Role == UserRole.Agency);

        Online = await branch
            .Where(u => u.Role == UserRole.Member && u.CurrentSessionId != null && u.LastHeartbeatUtc >= onlineSince)
            .OrderByDescending(u => u.LastHeartbeatUtc)
            .ToListAsync();
        OnlineMembers = Online.Count;

        ExpiringSoon = await branch.CountAsync(u =>
            u.Role == UserRole.Member && u.IsActive &&
            u.ExpirationDate != null && u.ExpirationDate > now && u.ExpirationDate <= now.AddDays(7));

        var visibleIds = await Tree.VisibleUserIdsAsync(Me);

        var todayBets = Db.BettingHistories.AsNoTracking()
            .Where(b => visibleIds.Contains(b.UserId) && b.CreatedUtc >= todayStart && !b.IsVirtual);
        TodayBets = await todayBets.CountAsync();
        TodayProfit = await todayBets.SumAsync(b => (decimal?)b.Profit) ?? 0m;

        RecentErrors = await Db.AppLogs.AsNoTracking()
            .Where(l => l.UserId != null && visibleIds.Contains(l.UserId.Value))
            .Where(l => l.Level == "Error" || l.Level == "Critical" || l.Level == "Fatal")
            .OrderByDescending(l => l.Id)
            .Take(10)
            .ToListAsync();

        var ids = RecentErrors.Select(l => l.UserId ?? 0).Distinct().ToList();
        UserNames = await Db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username);
    }
}
