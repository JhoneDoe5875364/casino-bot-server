using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PragmaticBot.Server.Data;

/// <summary>
/// Where an account sits in the 본사 → 총판 → 대리점 → 회원 chain. Higher value = more reach.
/// </summary>
public enum UserRole
{
    /// <summary>봇을 돌리는 실사용자. 관리 화면에 들어올 수 없다.</summary>
    Member = 0,

    /// <summary>대리점. 자기가 만든 회원만 관리한다.</summary>
    Agency = 10,

    /// <summary>총판. 자기 밑의 대리점과 그 회원까지 관리한다.</summary>
    Distributor = 20,

    /// <summary>본사. 전체를 관리하고 도메인·게임·공지를 설정한다.</summary>
    Owner = 30
}

/// <summary>
/// Which live-casino providers an account may use. A flags enum rather than a single choice so
/// "둘 다" is one value instead of a special case, and a third provider costs one more bit.
/// </summary>
[Flags]
public enum GameProviders
{
    None = 0,
    PragmaticPlay = 1,
    Evolution = 2,
    All = PragmaticPlay | Evolution
}

public class User
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string Username { get; set; } = "";

    [MaxLength(200)]
    public string PasswordHash { get; set; } = "";

    /// <summary>Display name shown in the bot's title bar.</summary>
    [MaxLength(64)]
    public string DisplayName { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public UserRole Role { get; set; } = UserRole.Member;

    /// <summary>The 총판/대리점 that created this account. Null only for the root 본사.</summary>
    public int? ParentId { get; set; }

    public User? Parent { get; set; }

    public ICollection<User> Children { get; set; } = new List<User>();

    /// <summary>
    /// Materialised ancestor path, e.g. "/1/4/9/" for user 9 under 4 under 1. Lets a subtree be
    /// fetched with one LIKE instead of walking parents row by row. Always starts and ends with "/".
    /// </summary>
    [MaxLength(255)]
    public string TreePath { get; set; } = "/";

    /// <summary>True for anything that can sign in to the admin site.</summary>
    public bool IsAdmin => Role > UserRole.Member;

    /// <summary>When the account stops being allowed to log in. Null = no expiry.</summary>
    public DateTime? ExpirationDate { get; set; }

    /// <summary>Session id of the one device currently allowed to run. Enforces single-login.</summary>
    [MaxLength(64)]
    public string? CurrentSessionId { get; set; }

    public DateTime? LastLoginUtc { get; set; }

    public DateTime? LastHeartbeatUtc { get; set; }

    [MaxLength(64)]
    public string? LastIpAddress { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Providers this account is allowed to play. Defaults to Pragmatic Play, which is what every
    /// account was implicitly limited to before Evolution existed.
    /// </summary>
    public GameProviders Providers { get; set; } = GameProviders.PragmaticPlay;

    /// <summary>
    /// When on, this account's bot also uploads Info-level logs.
    /// <para>
    /// Off by default: Info was 99% of everything stored (63 MB from a single bot in thirteen hours)
    /// and almost all of it raw WebSocket frame dumps nobody reads. Switch it on for one account while
    /// reproducing a problem, then switch it back off.
    /// </para>
    /// </summary>
    public bool DiagnosticLogging { get; set; }

    public ICollection<DomainSite> DomainSites { get; set; } = new List<DomainSite>();
}

/// <summary>Operator-wide provider settings. One row per provider.</summary>
public class DomainSetting
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = "PragmaticPlay";

    [MaxLength(255)]
    public string? Domain { get; set; }

    [MaxLength(512)]
    public string? WebSocketUrl { get; set; }

    [MaxLength(512)]
    public string? BalanceSelector { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Per-user betting site slot (the bot drives one browser per slot).</summary>
public class DomainSite
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = "PragmaticPlay";

    public int SiteSlot { get; set; }

    [MaxLength(255)]
    public string? Domain { get; set; }

    [MaxLength(512)]
    public string? WebSocketUrl { get; set; }

    [MaxLength(32)]
    public string BrowserType { get; set; } = "Chrome";

    public int DebugPort { get; set; }

    [MaxLength(512)]
    public string? SiteUrl { get; set; }

    public bool IsActive { get; set; }

    [MaxLength(512)]
    public string? BalanceSelector { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A numbered betting pattern ("서식").</summary>
public class FormatSetting
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Number { get; set; }

    [MaxLength(512)]
    public string? Text { get; set; }
}

public class ExceptionFormat
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Number { get; set; }

    [MaxLength(512)]
    public string? Pattern { get; set; }
}

public class RoomGateFormat
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int Number { get; set; }

    /// <summary>0 = gate on, 1 = gate off.</summary>
    public int GateType { get; set; }

    [MaxLength(512)]
    public string? Pattern { get; set; }
}

public class BetAmountSetting
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int BetAmountType { get; set; }
    public int Number { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Amount { get; set; }

    public int? BettingMethod { get; set; }
}

public class MatrixCell
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int BetAmountType { get; set; }
    public int RowIndex { get; set; }
    public int ColIndex { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }
}

public class MatrixRowRule
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int BetAmountType { get; set; }
    public int RowIndex { get; set; }
    public int WinCount { get; set; }
    public int TargetRow { get; set; }
    public int WinMode { get; set; }
    public int RestMinutes { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal FirstRowCapAmount { get; set; }
}

public class LimitSetting
{
    public int Id { get; set; }
    public int UserId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? UpperLimitPercent { get; set; }

    public bool IsUpperLimitEnabled { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? LowerLimitPercent { get; set; }

    public bool IsLowerLimitEnabled { get; set; }

    public int? MaxActiveBettingRooms { get; set; }

    public int UpperLimitMode { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? TargetProfitAmount { get; set; }

    public bool IsTargetCycleEnabled { get; set; }

    public int? RestIntervalMinutes { get; set; }

    public int? SessionCount { get; set; }

    [MaxLength(128)]
    public string? KeepAliveTableId { get; set; }
}

public class RoomCooldownSetting
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool IsEnabled { get; set; }
    public int LossThreshold { get; set; }
    public int CooldownMinutes { get; set; }
}

public class StrategyChain
{
    public long Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = "";

    public int CompletionAction { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<StrategyChainStep> Steps { get; set; } = new List<StrategyChainStep>();
}

public class StrategyChainStep
{
    public long Id { get; set; }
    public long StrategyChainId { get; set; }
    public StrategyChain? StrategyChain { get; set; }

    public int Order { get; set; }
    public int StrategyType { get; set; }
    public int? MaxStepTransition { get; set; }
    public int? ConsecutiveLossTransition { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? LossAmountTransition { get; set; }

    public bool StrategyCompleteTransition { get; set; }
}

public class BettingHistory
{
    public long Id { get; set; }
    public int UserId { get; set; }

    [MaxLength(32)]
    public string BetType { get; set; } = "";

    [Column(TypeName = "decimal(18,2)")]
    public decimal BetAmount { get; set; }

    /// <summary>Pending / Win / Loss / Tie / Cancelled.</summary>
    [MaxLength(32)]
    public string Status { get; set; } = "";

    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceBefore { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal BalanceAfter { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? WinAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Profit { get; set; }

    public bool IsVirtual { get; set; }

    [MaxLength(128)]
    public string? TableId { get; set; }

    [MaxLength(128)]
    public string? GameId { get; set; }

    public int? BetAmountType { get; set; }
    public int? MatrixRow { get; set; }
    public int? MatrixCol { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SettledUtc { get; set; }

    public ICollection<BettingAllocation> Allocations { get; set; } = new List<BettingAllocation>();
}

public class BettingAllocation
{
    public long Id { get; set; }
    public long BettingHistoryId { get; set; }
    public BettingHistory? BettingHistory { get; set; }

    public int SiteSlot { get; set; }

    [MaxLength(255)]
    public string Domain { get; set; } = "";

    [Column(TypeName = "decimal(18,2)")]
    public decimal AllocatedAmount { get; set; }

    public bool IsSent { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "";
}

public class BettingSession
{
    public long Id { get; set; }
    public int UserId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal StartBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal EndBalance { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalBetAmount { get; set; }

    public bool IsVirtual { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Catalog of baccarat tables the bot can play, managed by the operator.</summary>
public class Game
{
    public int Id { get; set; }

    [MaxLength(64)]
    public string GameCode { get; set; } = "";

    [MaxLength(128)]
    public string Title { get; set; } = "";

    [MaxLength(128)]
    public string? TitleKo { get; set; }

    [MaxLength(64)]
    public string? Type { get; set; }

    [MaxLength(64)]
    public string? Vendor { get; set; }

    [MaxLength(64)]
    public string? Provider { get; set; }

    /// <summary>Pragmatic Play table id the websocket traffic uses.</summary>
    [MaxLength(64)]
    public string? PpTableId { get; set; }

    public int SortOrder { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// True when the row came from the bot spotting the table in live traffic rather than from
    /// an operator adding it. Auto-discovered rows may have their title refined later; a row an
    /// operator created or renamed is never touched by discovery.
    /// </summary>
    public bool IsAutoDiscovered { get; set; }

    public DateTime? DiscoveredUtc { get; set; }
}

public class UserGameSelection
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GameId { get; set; }
    public bool IsSelected { get; set; }
}

public class Notice
{
    public long Id { get; set; }

    [MaxLength(2000)]
    public string Message { get; set; } = "";

    public bool IsActive { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class AppLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }

    [MaxLength(32)]
    public string Level { get; set; } = "";

    [MaxLength(64)]
    public string? Category { get; set; }

    [MaxLength(2000)]
    public string Message { get; set; } = "";

    [MaxLength(4000)]
    public string? StackTrace { get; set; }

    [MaxLength(4000)]
    public string? AdditionalData { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>서버 전역 설정(싱글턴 한 행). 지금은 봇 버전 게이트만 담는다.</summary>
public class ServerConfig
{
    public int Id { get; set; }

    /// <summary>봇이 이 값과 <b>정확히 일치</b>해야 서버 사용 가능. 비어 있으면 게이트 비활성(모두 허용).</summary>
    [MaxLength(32)]
    public string RequiredBotVersion { get; set; } = "";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
