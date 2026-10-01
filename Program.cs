using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PragmaticBot.Server.Contracts;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Hubs;
using PragmaticBot.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- configuration ----------

// Secrets (DB password, JWT key, seeded admin password) live here and stay out of source control,
// mirroring how the bot client layers its own appsettings.local.json.
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key 가 설정되지 않았거나 너무 짧습니다(32자 이상). appsettings.json 을 확인하세요.");

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default 가 설정되지 않았습니다.");

// ---------- services ----------

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddSingleton(jwt);
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<SessionNotifier>();
builder.Services.AddScoped<HierarchyService>();
builder.Services.AddHostedService<LogRetentionService>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
    });

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
    o.Conventions.AllowAnonymousToPage("/Admin/Login");
});

builder.Services.AddSignalR();

// A) JWT 매 요청 DB 재검증용 캐시. 차단/만료/세션축출된 유저의 토큰이 만료 전까지 우회하던 문제 대응.
builder.Services.AddMemoryCache();

// D) 로그인 무차별 대입 제한: 인증 POST(봇/관리자 로그인)만 IP당 5분에 10회로 제한.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = System.Threading.RateLimiting.PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        string path = ctx.Request.Path.Value ?? string.Empty;
        bool isAuthPost = HttpMethods.IsPost(ctx.Request.Method)
            && (path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/Admin/Login", StringComparison.OrdinalIgnoreCase));
        if (!isAuthPost)
        {
            return System.Threading.RateLimiting.RateLimitPartition.GetNoLimiter("nolimit");
        }
        string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter("auth:" + ip,
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            });
    });
});

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new TokenService(jwt).SigningKey,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        // SignalR's websocket transport cannot send an Authorization header
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            // A) 서명·수명만 보던 것을 넘어, 매 요청마다 DB의 계정 상태를 확인한다(30초 캐시).
            //    차단(IsActive=false)·만료·다른 기기 로그인(세션 축출)된 토큰이면 즉시 거부 →
            //    변조 클라이언트가 유효 토큰으로 최대 24시간 우회하던 구멍을 30초로 좁힌다.
            OnTokenValidated = async ctx =>
            {
                var principal = ctx.Principal;
                string? sub = principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                    ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                string? sid = principal?.FindFirstValue(TokenService.SessionIdClaim);
                if (!int.TryParse(sub, out int uid))
                {
                    ctx.Fail("토큰 주체가 올바르지 않습니다.");
                    return;
                }

                var cache = ctx.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                var snap = await cache.GetOrCreateAsync("authchk:" + uid, async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
                    var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                    return await db.Users.AsNoTracking()
                        .Where(u => u.Id == uid)
                        .Select(u => new AuthSnapshot(u.IsActive, u.ExpirationDate, u.CurrentSessionId, u.Role))
                        .FirstOrDefaultAsync();
                });

                if (snap is null) { ctx.Fail("계정을 찾을 수 없습니다."); return; }
                if (!snap.IsActive) { ctx.Fail("비활성화된 계정입니다."); return; }
                // 봇은 회원 계정만 사용 가능. 관리 계정으로 발급된(또는 사용 중 승급된) 토큰이면 즉시 거부.
                if (snap.Role != UserRole.Member) { ctx.Fail("봇 사용 권한이 없는 계정입니다."); return; }
                if (snap.ExpirationDate is { } exp && exp < DateTime.UtcNow) { ctx.Fail("사용 기간이 만료되었습니다."); return; }
                // 세션 축출(다른 기기 로그인) 확인. 하위호환: CurrentSessionId 가 비면 검사 생략.
                if (!string.IsNullOrEmpty(snap.CurrentSessionId)
                    && !string.Equals(snap.CurrentSessionId, sid, StringComparison.Ordinal))
                {
                    ctx.Fail("다른 장치에서 로그인되어 세션이 종료되었습니다.");
                    return;
                }

                // 봇 버전 게이트: 토큰의 ver 클레임이 서버 요구 버전과 정확히 일치해야 한다.
                // 관리자가 요구 버전을 바꾸면 옛/불일치 봇은 30초(캐시) 안에 API가 막힌다.
                var verDb = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                string requiredVer = await VersionGate.RequiredAsync(cache, verDb);
                string? tokenVer = principal?.FindFirstValue(TokenService.VersionClaim);
                if (!VersionGate.IsAllowed(requiredVer, tokenVer))
                {
                    ctx.Fail("봇 버전이 서버와 일치하지 않습니다. 최신 봇으로 업데이트하세요.");
                    return;
                }
            }
        };
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        o.LoginPath = "/Admin/Login";
        o.LogoutPath = "/Admin/Logout";
        o.AccessDeniedPath = "/Admin/Login";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Cookie.Name = "PragmaticBot.Admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AdminOnly", p => p
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireRole("Admin"));

var app = builder.Build();

// ---------- pipeline ----------

// 리버스 프록시(Nginx/IIS/Cloudflare 등) 뒤에 배포되면 Kestrel이 보는 접속 IP는 항상 프록시(127.0.0.1)다.
// 진짜 사용자 IP는 X-Forwarded-For 헤더에 오므로, 이를 RemoteIpAddress에 반영한다.
// (프록시는 로컬에서 전달하므로 KnownProxies/KnownNetworks를 비워 전달 헤더를 신뢰한다.)
var forwardedOptions = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    // X-Forwarded-For 체인이 여러 단계여도 원 클라이언트까지 거슬러 읽도록 넉넉히 둔다.
    ForwardLimit = 5
};
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

// No HTTPS redirection: the bot talks plain HTTP and rejects non-JSON replies,
// so a 307 to https would surface as a confusing parse error on the client.

// Every API failure must come back as {"message": "..."} — that is what ApiClient parses.
app.UseExceptionHandler(a => a.Run(async ctx =>
{
    var error = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(error, "Unhandled error on {Path}", ctx.Request.Path);
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsJsonAsync(new ApiErrorResponse("서버 내부 오류가 발생했습니다."));
}));

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();   // D) 인증 POST 무차별 대입 제한(ForwardedHeaders 뒤라 실제 IP 기준)
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapHub<SessionHub>("/hubs/session");
app.MapGet("/", () => Results.Redirect("/Admin"));
app.MapGet("/health", () => Results.Json(new { status = "ok", utc = DateTime.UtcNow }));

// ---------- database ----------

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, builder.Configuration, app.Logger);
}

app.Run();
