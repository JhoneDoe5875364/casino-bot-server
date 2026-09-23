using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

public class NoticesModel(AppDbContext db, HierarchyService tree) : AdminPageModel(db, tree)
{
    public List<Notice> Notices { get; set; } = [];

    /// <summary>The one the bot actually shows: newest active.</summary>
    public long? LiveNoticeId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (RequireOwner() is { } deny)
            return deny;
        await LoadAsync();
        return Page();
    }

    [OwnerOnly]
    public async Task<IActionResult> OnPostCreateAsync(string message, bool activate = false)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            TempData["Err"] = "공지 내용을 입력하세요.";
            return RedirectToPage();
        }

        Db.Notices.Add(new Notice { Message = message.Trim(), IsActive = activate });
        await Db.SaveChangesAsync();

        TempData["Ok"] = "공지를 등록했습니다.";
        return RedirectToPage();
    }

    [OwnerOnly]
    public async Task<IActionResult> OnPostToggleAsync(long id)
    {
        var notice = await Db.Notices.FindAsync(id);
        if (notice is null)
            return NotFound();

        notice.IsActive = !notice.IsActive;
        await Db.SaveChangesAsync();

        TempData["Ok"] = notice.IsActive ? "공지를 게시했습니다." : "공지를 내렸습니다.";
        return RedirectToPage();
    }

    [OwnerOnly]
    public async Task<IActionResult> OnPostDeleteAsync(long id)
    {
        var notice = await Db.Notices.FindAsync(id);
        if (notice is null)
            return NotFound();

        Db.Notices.Remove(notice);
        await Db.SaveChangesAsync();

        TempData["Ok"] = "공지를 삭제했습니다.";
        return RedirectToPage();
    }

    async Task LoadAsync()
    {
        Notices = await Db.Notices.AsNoTracking().OrderByDescending(n => n.Id).ToListAsync();
        LiveNoticeId = Notices.Where(n => n.IsActive)
            .OrderByDescending(n => n.CreatedUtc)
            .Select(n => (long?)n.Id)
            .FirstOrDefault();
    }
}
