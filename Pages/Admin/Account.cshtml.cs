using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PragmaticBot.Server.Data;
using PragmaticBot.Server.Hubs;
using PragmaticBot.Server.Services;

namespace PragmaticBot.Server.Pages.Admin;

/// <summary>
/// Lets an admin maintain their own account. The user-management screens deliberately refuse to
/// act on yourself (you may only manage accounts below you), which otherwise leaves the single
/// 본사 account with no way to change its own password.
/// </summary>
public class AccountModel(AppDbContext db, HierarchyService tree, SessionNotifier notifier)
    : AdminPageModel(db, tree)
{
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostRenameAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            TempData["Err"] = "표시 이름을 입력하세요.";
            return RedirectToPage();
        }

        var me = await Db.Users.FirstAsync(u => u.Id == Me.Id);
        me.DisplayName = displayName.Trim();
        await Db.SaveChangesAsync();

        TempData["Ok"] = "표시 이름을 변경했습니다.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostChangePasswordAsync(
        string currentPassword, string newPassword, string confirmPassword)
    {
        var me = await Db.Users.FirstAsync(u => u.Id == Me.Id);

        if (!PasswordHasher.Verify(currentPassword ?? "", me.PasswordHash))
        {
            TempData["Err"] = "현재 비밀번호가 올바르지 않습니다.";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 4)
        {
            TempData["Err"] = "새 비밀번호는 4자 이상이어야 합니다.";
            return RedirectToPage();
        }

        if (newPassword != confirmPassword)
        {
            TempData["Err"] = "새 비밀번호와 확인이 일치하지 않습니다.";
            return RedirectToPage();
        }

        me.PasswordHash = PasswordHasher.Hash(newPassword);
        me.CurrentSessionId = null;
        await Db.SaveChangesAsync();
        await notifier.ForceLogoutAsync(me.Id, "비밀번호가 변경되어 세션이 종료되었습니다.");

        // the cookie was issued against the old credentials — make them sign in again
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData["Ok"] = "비밀번호를 변경했습니다. 새 비밀번호로 다시 로그인하세요.";
        return RedirectToPage("/Admin/Login");
    }

    public async Task<IActionResult> OnPostAddOwnerAsync(string username, string displayName, string password)
    {
        if (RequireOwner() is { } deny)
            return deny;

        username = (username ?? "").Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Err"] = "아이디와 비밀번호는 필수입니다.";
            return RedirectToPage();
        }

        if (await Db.Users.AnyAsync(u => u.Username == username))
        {
            TempData["Err"] = $"이미 존재하는 아이디입니다: {username}";
            return RedirectToPage();
        }

        // a second 본사 sits at the root alongside the first, not underneath it
        Db.Users.Add(new User
        {
            Username = username,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            Role = UserRole.Owner,
            ParentId = null,
            TreePath = "/",
            IsActive = true
        });
        await Db.SaveChangesAsync();

        TempData["Ok"] = $"본사 계정을 만들었습니다: {username}";
        return RedirectToPage();
    }
}
