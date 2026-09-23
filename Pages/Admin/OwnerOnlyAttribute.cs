namespace PragmaticBot.Server.Pages.Admin;

/// <summary>
/// Marks a page handler as 본사-only. Put it on the POST handlers of operator-wide screens
/// (게임 · 공지) so a 총판 cannot reach them by posting the URL directly.
/// <para>
/// This is a marker, not a filter: Razor Pages ignores filters declared on handler methods, so the
/// check lives in <see cref="AdminPageModel.OnPageHandlerExecutionAsync"/>, right after the signed-in
/// account is loaded.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OwnerOnlyAttribute : Attribute
{
}
