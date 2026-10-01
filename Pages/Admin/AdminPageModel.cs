using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

/// <summary>
/// Base for every admin screen. Loads the signed-in 본사/총판/대리점 account once per request so
/// pages can scope their queries to that account's branch instead of the whole table.
/// </summary>
public abstract class AdminPageModel(AppDbContext db, HierarchyService tree) : PageModel
{
    protected AppDbContext Db { get; } = db;
    protected HierarchyService Tree { get; } = tree;

    /// <summary>The signed-in admin. Never null inside a handler — the filter below guarantees it.</summary>
    public User Me { get; private set; } = null!;

    public bool IsOwner => Me.Role == UserRole.Owner;

    public string RoleLabel(UserRole role) => HierarchyService.Label(role);

    public override async Task OnPageHandlerExecutionAsync(
        PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var me = int.TryParse(id, out var userId)
            ? await Db.Users.FirstOrDefaultAsync(u => u.Id == userId)
            : null;

        // Signed in but the account was deleted, demoted, disabled or expired since the cookie was issued.
        // 만료 검사까지 여기서 하므로, 기간이 지난 관리 계정은 모든 화면(계정 생성 포함)에서 즉시 로그아웃된다.
        if (me is null || me.Role == UserRole.Member || !me.IsActive
            || (me.ExpirationDate is { } exp && exp < DateTime.UtcNow))
        {
            context.Result = new RedirectToPageResult("/Admin/Logout");
            return;
        }

        Me = me;

        // The layout used to read a "tier" claim, which goes stale whenever the cookie predates a
        // role change (or the claim itself). Publish the value we just read from the database.
        ViewData["IsOwner"] = me.Role == UserRole.Owner;

        // [OwnerOnly] handlers. Checked here rather than by a filter on the handler, which Razor
        // Pages never runs — that left these handlers open to any 총판 or 대리점.
        if (me.Role != UserRole.Owner
            && context.HandlerMethod?.MethodInfo.IsDefined(typeof(OwnerOnlyAttribute), inherit: true) == true)
        {
            TempData["Err"] = "본사 계정만 사용할 수 있습니다.";
            context.Result = new RedirectToPageResult("/Admin/Index");
            return;
        }

        await next();
    }

    /// <summary>Pages that only the 본사 may open (operator-wide settings).</summary>
    protected IActionResult? RequireOwner()
    {
        if (IsOwner)
            return null;
        TempData["Err"] = "본사 계정만 접근할 수 있습니다.";
        return RedirectToPage("/Admin/Index");
    }
}
