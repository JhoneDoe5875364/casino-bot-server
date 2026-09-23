using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config, ILogger logger)
    {
        if (!await db.Users.AnyAsync(u => u.Role == UserRole.Owner))
        {
            var username = config["Seed:AdminUsername"] ?? "admin";
            var password = config["Seed:AdminPassword"] ?? "admin1234";
            var owner = new User
            {
                Username = username,
                DisplayName = "본사",
                PasswordHash = PasswordHasher.Hash(password),
                IsActive = true,
                Role = UserRole.Owner,
                ParentId = null,
                TreePath = "/"
            };
            db.Users.Add(owner);
            await db.SaveChangesAsync();
            logger.LogWarning(
                "본사 계정을 생성했습니다: {Username} / {Password} — 로그인 후 반드시 변경하세요.",
                username, password);
        }

        if (!await db.DomainSettings.AnyAsync(d => d.Provider == "PragmaticPlay"))
        {
            db.DomainSettings.Add(new DomainSetting { Provider = "PragmaticPlay" });
            await db.SaveChangesAsync();
        }

        // 봇 버전 게이트: 기본값을 현재 봇 버전으로 두면, 버전을 안 보내는 옛 봇은 즉시 차단된다.
        if (!await db.ServerConfigs.AnyAsync())
        {
            db.ServerConfigs.Add(new ServerConfig { RequiredBotVersion = PragmaticBot.Server.Services.BotVersionInfo.Current });
            await db.SaveChangesAsync();
        }
    }
}
