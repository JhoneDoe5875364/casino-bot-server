namespace PragmaticBot.Server.Contracts;

// These records mirror PragmaticBot/Models/Api/ApiDtos.cs on the client, field for field.
// The client deserializes with camelCase + case-insensitive matching, so member NAMES are the
// contract — do not rename one side without the other.

public record LoginRequest(string Username, string Password, string? ClientVersion = null);

public record LoginResponse(
    string Token,
    string UserId,
    string UserName,
    bool IsActive,
    DateTime? ExpirationDate,
    string SessionId,
    /// <summary>Bit flags: 1 = Pragmatic Play, 2 = Evolution. Defaults to Pragmatic only.</summary>
    int Providers = 1);

public record SessionCheckResponse(bool IsValid, bool DiagnosticLogging = false);

/// <summary>하트비트 응답. Token 이 있으면 클라이언트는 그것으로 토큰을 갱신한다(수명 연장).</summary>
public record HeartbeatResponse(bool Ok, string? Token = null);

public record DomainSettingDto(
    string? Domain,
    string? WebSocketUrl,
    string Provider = "Evolution",
    string? BalanceSelector = null);

public record DomainSiteDto(
    int SiteSlot,
    string? Domain,
    string? WebSocketUrl,
    string BrowserType,
    int DebugPort,
    string? SiteUrl,
    bool IsActive,
    string Provider = "PragmaticPlay",
    string? BalanceSelector = null);

public record WebSocketUrlRequest(string? WebSocketUrl);

public record BalanceSelectorRequest(string? Selector);

public record BettingAllocationDto(
    int SiteSlot,
    string Domain,
    decimal AllocatedAmount,
    bool IsSent,
    string Status);

public record BettingHistoryWithAllocationsDto(
    string BetType,
    decimal BetAmount,
    string Status,
    decimal BalanceBefore,
    decimal BalanceAfter,
    List<BettingAllocationDto> Allocations,
    string? TableId = null,
    string? GameId = null,
    int? BetAmountType = null,
    int? MatrixRow = null,
    int? MatrixCol = null,
    bool IsVirtual = false);

public record BettingHistoryDto(
    string BetType,
    decimal BetAmount,
    string Status,
    decimal BalanceBefore,
    decimal BalanceAfter,
    bool IsVirtual = false,
    string? TableId = null,
    string? GameId = null,
    int? BetAmountType = null,
    int? MatrixRow = null,
    int? MatrixCol = null);

public record SettleBettingRequest(
    string BetType,
    decimal BetAmount,
    string Status,
    decimal? WinAmount,
    decimal? Profit,
    bool IsVirtual = false,
    string? TableId = null,
    string? GameId = null);

public record CancelBettingRequest(
    string BetType,
    decimal BetAmount,
    string? TableId = null,
    string? GameId = null);

public record BettingSessionRequest(
    decimal StartBalance,
    decimal EndBalance,
    decimal TotalBetAmount,
    bool IsVirtual = false);

public record MatrixStatDto(
    int BetAmountType,
    int MatrixRow,
    int MatrixCol,
    int WinCount,
    int LossCount,
    int TieCount,
    decimal BetAmountSum,
    decimal ProfitSum);

public record AppLogDto(
    string Level,
    string? Category,
    string Message,
    string? StackTrace,
    string? AdditionalData);

public record FormatSettingDto(int Number, string? Text);

public record BetAmountSettingDto(
    int Number,
    decimal? Amount,
    int BetAmountType = 0,
    int? BettingMethod = null);

public record MatrixCellDto(int RowIndex, int ColIndex, decimal Amount);

public record MatrixRowRuleDto(
    int RowIndex,
    int WinCount,
    int TargetRow,
    int WinMode = 0,
    int RestMinutes = 0,
    decimal FirstRowCapAmount = 0m);

public record LimitSettingDto(
    decimal? UpperLimitPercent,
    bool IsUpperLimitEnabled,
    decimal? LowerLimitPercent,
    bool IsLowerLimitEnabled,
    int? MaxActiveBettingRooms = null,
    int UpperLimitMode = 0,
    decimal? TargetProfitAmount = null,
    bool IsTargetCycleEnabled = false,
    int? RestIntervalMinutes = null,
    int? SessionCount = null,
    string? KeepAliveTableId = null);

public record ExceptionFormatDto(int Number, string? Pattern);

public record RoomGateFormatDto(int Number, int GateType, string? Pattern);

/// <summary>
/// A table the bot ran into in the live lobby traffic. The operator can rename it later;
/// the point of reporting it is that a room the bot can see is never missing from the catalog.
/// </summary>
public record DiscoveredGameDto(string PpTableId, string? Title, string? TableType);

public record DiscoveredGamesResult(int Added, int Updated, int Total);

public record GameSelectionDto(int GameId, bool IsSelected);

public record GameDto(
    int Id,
    string GameCode,
    string Title,
    string? TitleKo,
    string? Type,
    string? Vendor,
    string? Provider,
    string? PpTableId);

public record StrategyChainDto(
    long Id,
    string Name,
    int CompletionAction,
    bool IsActive,
    List<StrategyChainStepDto> Steps);

public record StrategyChainStepDto(
    int Order,
    int StrategyType,
    int? MaxStepTransition,
    int? ConsecutiveLossTransition,
    decimal? LossAmountTransition,
    bool StrategyCompleteTransition);

public record ApiErrorResponse(string Message);

public record ActiveNoticeResponse(bool Success, NoticeDto? Notice);

public record NoticeDto(long Id, string Message, DateTime CreatedAt);

public record RoomCooldownSettingDto(bool IsEnabled, int LossThreshold, int CooldownMinutes);
