using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PragmaticBot.Server.Data;

namespace PragmaticBot.Server.Services;

/// <summary>
/// 봇 버전 게이트: 서버가 정한 RequiredBotVersion 과 <b>정확히 일치</b>하는 봇만 허용한다.
/// 버전을 안 보내는(옛) 봇은 자동 차단. RequiredBotVersion 이 비어 있으면 게이트 비활성(모두 허용).
/// 매 요청 DB 조회를 피하려 30초 캐시한다(보안 재검증과 동일 패턴).
/// </summary>
public static class VersionGate
{
    public const string CacheKey = "server:reqver";

    /// <summary>현재 요구 버전(캐시). 없으면 빈 문자열.</summary>
    public static async Task<string> RequiredAsync(IMemoryCache cache, AppDbContext db)
    {
        return await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            var cfg = await db.ServerConfigs.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync();
            return cfg?.RequiredBotVersion ?? string.Empty;
        }) ?? string.Empty;
    }

    /// <summary>required 가 비면 통과(게이트 off), 아니면 정확 일치해야 통과.</summary>
    public static bool IsAllowed(string required, string? clientVersion) =>
        string.IsNullOrEmpty(required)
        || string.Equals(required, clientVersion, StringComparison.Ordinal);

    /// <summary>요구 버전을 바꾼 뒤 캐시를 비워 즉시 반영한다.</summary>
    public static void Evict(IMemoryCache cache) => cache.Remove(CacheKey);
}
