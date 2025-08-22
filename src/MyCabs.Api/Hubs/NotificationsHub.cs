using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyCabs.Api.Hubs;

[Authorize]
public class NotificationsHub : Hub
{
    private static string G(string threadId) => $"thread:{threadId}";
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
        await base.OnDisconnectedAsync(exception);
    }

    // Client gọi sau khi có threadId để vào group chat
    public Task JoinThread(string threadId) => Groups.AddToGroupAsync(Context.ConnectionId, G(threadId));
    public Task LeaveThread(string threadId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, G(threadId));

    // Typing indicator: broadcast cho người còn lại trong thread
    public Task Typing(string threadId, bool isTyping)
        => Clients.OthersInGroup(G(threadId)).SendAsync("chat.typing", new { threadId, userId = Context.UserIdentifier, isTyping });
}