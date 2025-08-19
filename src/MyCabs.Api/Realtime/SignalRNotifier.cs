using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Realtime;

public class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationsHub> _hub;
    public SignalRNotifier(IHubContext<NotificationsHub> hub) { _hub = hub; }
    public Task NotifyUserAsync(string userId, string eventName, object payload)
        => _hub.Clients.Group(userId).SendAsync(eventName, payload);
}