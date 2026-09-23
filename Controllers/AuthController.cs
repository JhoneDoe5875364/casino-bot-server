using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Contracts;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Hubs;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Controllers;

[Route("api/auth")]
public class AuthController(
    AppDbContext db,
    TokenService tokens,
    SessionNotifier notifier,
    Microsoft.Extensions.Caching.Memory.IMemoryCache cache,
    ILogger<AuthController> log) : ApiControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Fail(StatusCodes.Status401Unauthorized, "사용자명 또는 비밀번호가 올바르지 않습니다.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        if (user is null || !PasswordHasher.Verify(request.Password, user.PasswordHash))
            return Fail(StatusCodes.Status401Unauthorized, "사용자명 또는 비밀번호가 올바르지 않습니다.");

        if (!user.IsActive)
            return Fail(StatusCodes.Status401Unauthorized, "비활성화된 계정입니다. 관리자에게 문의하세요.");

        if (user.ExpirationDate is { } expiry && expiry < DateTime.UtcNow)
            return Fail(StatusCodes.Status401Unauthorized,
                $"사용 기간이 만료되었습니다. (만료일: {expiry.ToLocalTime():yyyy-MM-dd})");

        // 봇 버전 게이트: 서버가 정한 버전과 정확히 일치해야 한다. 버전을 안 보내는 옛 봇은 여기서 막힌다.
        var requiredVersion = await VersionGate.RequiredAsync(cache, db);
        if (!VersionGate.IsAllowed(requiredVersion, request.ClientVersion))
            return Fail(StatusCodes.Status426UpgradeRequired,
                $"봇 버전이 서버와 일치하지 않습니다. 필요 버전 {requiredVersion}, 현재 {(string.IsNullOrEmpty(request.ClientVersion) ? "없음" : request.ClientVersion)}. 최신 봇으로 업데이트하세요.");

        // Single login: the newest device wins and the previous one is told to quit.
        var previousSession = user.CurrentSessionId;
        var sessionId = Guid.NewGuid().ToString("N");
        user.CurrentSessionId = sessionId;
        user.LastLoginUtc = DateTime.UtcNow;
        user.LastHeartbeatUtc = DateTime.UtcNow;
        user.LastIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        await db.SaveChangesAsync();
        // 새 세션을 즉시 인식하도록 재검증 캐시를 비운다(옛 스냅샷이 새 토큰을 잠깐 거부하는 것 방지).
        cache.Remove("authchk:" + user.Id);

        if (!string.IsNullOrEmpty(previousSession))
        {
            await notifier.ForceLogoutAsync(user.Id, "다른 장치에서 로그인되어 현재 세션이 종료되었습니다.");
            log.LogInformation("User {Username} logged in from a new device; previous session evicted.", user.Username);
        }

        var token = tokens.Create(user, sessionId, request.ClientVersion);
        return Ok(new LoginResponse(
            token,
            user.Id.ToString(),
            user.DisplayName,
            user.IsActive,
            user.ExpirationDate,
            sessionId,
            (int)user.Providers));
    }

    [HttpGet("session/check")]
    public async Task<IActionResult> CheckSession()
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId);
        var valid = user is not null
            && user.IsActive
            && (user.ExpirationDate is null || user.ExpirationDate > DateTime.UtcNow)
            && user.CurrentSessionId == SessionId;
        // the bot polls this; it is also how it learns whether to upload Info-level logs
        return Ok(new SessionCheckResponse(valid, user?.DiagnosticLogging ?? false));
    }

    /// <summary>
    /// Called every 10s by the bot. Answering 409 is how a displaced device learns to stop.
    /// </summary>
    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat()
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
        if (user is null)
            return Fail(StatusCodes.Status401Unauthorized, "계정을 찾을 수 없습니다.");

        if (!user.IsActive)
            return Fail(StatusCodes.Status401Unauthorized, "비활성화된 계정입니다. 관리자에게 문의하세요.");

        if (user.ExpirationDate is { } expiry && expiry < DateTime.UtcNow)
            return Fail(StatusCodes.Status401Unauthorized, "사용 기간이 만료되었습니다.");

        if (user.CurrentSessionId != SessionId)
            return Fail(StatusCodes.Status409Conflict, "다른 장치에서 로그인되었습니다. 현재 세션을 종료합니다.");

        user.LastHeartbeatUtc = DateTime.UtcNow;
        // 실제 접속 IP를 매 하트비트마다 갱신(리버스 프록시의 X-Forwarded-For 반영, 10초 주기).
        user.LastIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        await db.SaveChangesAsync();

        // B) 토큰 갱신: 하트비트마다 같은 세션으로 수명을 새로 발급한다. 덕분에 토큰 수명을 짧게(2h)
        //    둬도 켜져 있는 봇은 끊기지 않고, 하트비트가 멈춘(중지·차단된) 봇은 짧은 수명 뒤 자동 만료.
        var freshToken = tokens.Create(user, SessionId!, User.FindFirst(TokenService.VersionClaim)?.Value);
        return Ok(new HeartbeatResponse(true, freshToken));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == UserId);
        if (user is not null && user.CurrentSessionId == SessionId)
        {
            user.CurrentSessionId = null;
            await db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
