using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Application.Realtime;

namespace MyCabs.Api.Realtime;

public class ChatPusher : IChatPusher
{
    private readonly IHubContext<NotificationsHub> _hub;
    public ChatPusher(IHubContext<NotificationsHub> hub) { _hub = hub; }

    private static string G(string threadId) => $"thread:{threadId}";

    public Task SendToThreadAsync(string threadId, string eventName, object payload)
        => _hub.Clients.Group(G(threadId)).SendAsync(eventName, payload);

    public Task SendToUserAsync(string userId, string eventName, object payload)
        => _hub.Clients.User(userId).SendAsync(eventName, payload);
}