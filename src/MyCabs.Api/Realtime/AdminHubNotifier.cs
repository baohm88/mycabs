using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Application.DTOs;
using MyCabs.Application.Realtime;

namespace MyCabs.Api.Realtime;

public class AdminHubNotifier : IAdminRealtime
{
    private readonly IHubContext<AdminHub> _hub;
    public AdminHubNotifier(IHubContext<AdminHub> hub) { _hub = hub; }

    public Task TxCreatedAsync(TransactionDto dto)
        => _hub.Clients.Group("admins").SendAsync("admin:tx:new", dto);
}