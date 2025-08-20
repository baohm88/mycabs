using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Application.Realtime;
using MyCabs.Domain.Interfaces;


namespace MyCabs.Api.Realtime;

public class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly INotificationRepository _repo;

    public SignalRNotifier(IHubContext<NotificationsHub> hub, INotificationRepository repo)
    {
        _hub = hub;
        _repo = repo;
    }

    public Task PushNotificationAsync(string userId, object payload)
        => NotifyUserAsync(userId, "notification", payload);

    public async Task PushUnreadCountAsync(string userId)
    {
        var count = await _repo.CountUnreadAsync(userId);
        await NotifyUserAsync(userId, "unread_count", new { count });
    }

    public Task NotifyUserAsync(string userId, string eventName, object payload)
        => _hub.Clients.User(userId).SendAsync(eventName, payload);
}
