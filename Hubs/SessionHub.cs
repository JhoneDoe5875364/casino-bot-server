using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PragmaticBot.Server.Hubs;

/// <summary>
/// The bot connects here after login and waits for "ForceLogout". Every connection joins a
/// per-user group so the server can kick one account's other devices.
/// </summary>
[Authorize]
public class SessionHub : Hub
{
    public static string GroupFor(int userId) => $"user-{userId}";

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
        if (int.TryParse(userId, out var id))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(id));
        await base.OnConnectedAsync();
    }
}

public class SessionNotifier(IHubContext<SessionHub> hub)
{
    public Task ForceLogoutAsync(int userId, string message) =>
        hub.Clients.Group(SessionHub.GroupFor(userId)).SendAsync("ForceLogout", message);
}
