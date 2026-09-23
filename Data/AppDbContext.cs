using Microsoft.EntityFrameworkCore;

namespace PragmaticBot.Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<DomainSetting> DomainSettings => Set<DomainSetting>();
    public DbSet<DomainSite> DomainSites => Set<DomainSite>();
    public DbSet<FormatSetting> FormatSettings => Set<FormatSetting>();
    public DbSet<ExceptionFormat> ExceptionFormats => Set<ExceptionFormat>();
    public DbSet<RoomGateFormat> RoomGateFormats => Set<RoomGateFormat>();
    public DbSet<BetAmountSetting> BetAmountSettings => Set<BetAmountSetting>();
    public DbSet<MatrixCell> MatrixCells => Set<MatrixCell>();
    public DbSet<MatrixRowRule> MatrixRowRules => Set<MatrixRowRule>();
    public DbSet<LimitSetting> LimitSettings => Set<LimitSetting>();
    public DbSet<RoomCooldownSetting> RoomCooldownSettings => Set<RoomCooldownSetting>();
    public DbSet<StrategyChain> StrategyChains => Set<StrategyChain>();
    public DbSet<StrategyChainStep> StrategyChainSteps => Set<StrategyChainStep>();
    public DbSet<BettingHistory> BettingHistories => Set<BettingHistory>();
    public DbSet<BettingAllocation> BettingAllocations => Set<BettingAllocation>();
    public DbSet<BettingSession> BettingSessions => Set<BettingSession>();
    public DbSet<Game> Games => Set<Game>();
    public DbSet<UserGameSelection> UserGameSelections => Set<UserGameSelection>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<AppLog> AppLogs => Set<AppLog>();
    public DbSet<ServerConfig> ServerConfigs => Set<ServerConfig>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.TreePath);
            e.HasIndex(x => new { x.ParentId, x.Role });
            e.Ignore(x => x.IsAdmin);
            // Deleting a 총판 must not silently drop its whole branch — reassignment is explicit.
            e.HasOne(x => x.Parent).WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DomainSetting>(e =>
        {
            e.HasIndex(x => x.Provider).IsUnique();
        });

        b.Entity<DomainSite>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Provider, x.SiteSlot }).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.DomainSites)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FormatSetting>(e => e.HasIndex(x => new { x.UserId, x.Number }).IsUnique());
        b.Entity<ExceptionFormat>(e => e.HasIndex(x => new { x.UserId, x.Number }).IsUnique());
        b.Entity<RoomGateFormat>(e => e.HasIndex(x => new { x.UserId, x.GateType, x.Number }).IsUnique());
        b.Entity<BetAmountSetting>(e => e.HasIndex(x => new { x.UserId, x.BetAmountType, x.Number }).IsUnique());
        b.Entity<MatrixCell>(e => e.HasIndex(x => new { x.UserId, x.BetAmountType, x.RowIndex, x.ColIndex }).IsUnique());
        b.Entity<MatrixRowRule>(e => e.HasIndex(x => new { x.UserId, x.BetAmountType, x.RowIndex }).IsUnique());
        b.Entity<LimitSetting>(e => e.HasIndex(x => x.UserId).IsUnique());
        b.Entity<RoomCooldownSetting>(e => e.HasIndex(x => x.UserId).IsUnique());

        b.Entity<StrategyChain>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.IsActive });
            e.HasMany(x => x.Steps).WithOne(x => x.StrategyChain)
                .HasForeignKey(x => x.StrategyChainId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BettingHistory>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CreatedUtc });
            // settle/cancel look a pending row up by this exact combination
            e.HasIndex(x => new { x.UserId, x.TableId, x.GameId, x.Status });
            e.HasMany(x => x.Allocations).WithOne(x => x.BettingHistory)
                .HasForeignKey(x => x.BettingHistoryId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BettingSession>(e => e.HasIndex(x => new { x.UserId, x.CreatedUtc }));

        b.Entity<Game>(e =>
        {
            e.HasIndex(x => x.GameCode).IsUnique();
            e.HasIndex(x => x.Provider);
        });

        b.Entity<UserGameSelection>(e => e.HasIndex(x => new { x.UserId, x.GameId }).IsUnique());

        b.Entity<Notice>(e => e.HasIndex(x => new { x.IsActive, x.CreatedUtc }));

        b.Entity<AppLog>(e => e.HasIndex(x => new { x.UserId, x.CreatedUtc }));
    }
}
