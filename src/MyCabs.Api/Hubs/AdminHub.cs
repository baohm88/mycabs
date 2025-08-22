using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyCabs.Api.Hubs;

[Authorize(Roles = "Admin")] // admin-only hub
public class AdminHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value
                   ?? Context.User?.FindFirst("role")?.Value;
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value
                   ?? Context.User?.FindFirst("role")?.Value;
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "admins");

        await base.OnDisconnectedAsync(exception);
    }
}