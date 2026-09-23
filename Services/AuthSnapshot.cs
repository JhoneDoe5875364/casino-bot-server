namespace PragmaticBot.Server.Services;

/// <summary>
/// JWT 매 요청 재검증(OnTokenValidated)에서 쓰는 계정 상태 스냅샷.
/// 매 요청 DB 조회를 피하려고 IMemoryCache 에 30초 캐시한다.
/// </summary>
public record AuthSnapshot(bool IsActive, System.DateTime? ExpirationDate, string? CurrentSessionId);
