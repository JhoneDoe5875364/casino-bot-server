using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

/// <summary>봇 버전 게이트 관리(본사 전용). 정확 일치 정책의 요구 버전을 설정한다.</summary>
public class BotVersionModel(AppDbContext db, HierarchyService tree, IMemoryCache cache) : AdminPageModel(db, tree)
{
    [BindProperty] public string RequiredBotVersion { get; set; } = "";

    /// <summary>서버 코드가 아는 기준 봇 버전(참고 표시용).</summary>
    public string SeedVersion => BotVersionInfo.Current;

    public async Task<IActionResult> OnGetAsync()
    {
        var deny = RequireOwner();
        if (deny != null) return deny;

        var cfg = await Db.ServerConfigs.AsNoTracking().OrderBy(c => c.Id).FirstOrDefaultAsync();
        RequiredBotVersion = cfg?.RequiredBotVersion ?? "";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var deny = RequireOwner();
        if (deny != null) return deny;

        var value = (RequiredBotVersion ?? "").Trim();
        var cfg = await Db.ServerConfigs.OrderBy(c => c.Id).FirstOrDefaultAsync();
        if (cfg is null)
        {
            cfg = new ServerConfig();
            Db.ServerConfigs.Add(cfg);
        }
        cfg.RequiredBotVersion = value;
        cfg.UpdatedUtc = DateTime.UtcNow;
        await Db.SaveChangesAsync();
        VersionGate.Evict(cache);   // 캐시를 비워 30초 기다리지 않고 즉시 반영

        TempData["Ok"] = value.Length == 0
            ? "버전 게이트를 해제했습니다(모든 봇 허용)."
            : $"필요 봇 버전을 {value} 로 저장했습니다.";
        return RedirectToPage();
    }
}
