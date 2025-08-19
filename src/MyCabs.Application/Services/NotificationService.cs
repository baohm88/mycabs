using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IRealtimeNotifier
{
    Task NotifyUserAsync(string userId,string eventName,object payload);
}

public interface INotificationService
{
    Task PublishAsync(string userId, CreateNotificationDto dto);
    Task<(IEnumerable<NotificationDto> Items,long Total)> GetAsync(string userId, NotificationsQuery q);
    Task<bool> MarkReadAsync(string userId,string notificationId);
    Task<long> MarkAllReadAsync(string userId);
}

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    private readonly IRealtimeNotifier _rt;
    public NotificationService(INotificationRepository repo, IRealtimeNotifier rt){ _repo = repo; _rt = rt; }

    public async Task PublishAsync(string userId, CreateNotificationDto dto)
    {
        if (!ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
        var n = new Notification{
            Id = ObjectId.GenerateNewId(), UserId = uid, Type = dto.Type, Title = dto.Title,
            Message = dto.Message, Data = dto.Data!=null? BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dto.Data)) : null,
            CreatedAt = DateTime.UtcNow
        };
        await _repo.CreateAsync(n);
        var payload = new NotificationDto(n.Id.ToString(), n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt, n.ReadAt,
            dto.Data);
        await _rt.NotifyUserAsync(userId, "notification", payload);
    }

    public async Task<(IEnumerable<NotificationDto> Items,long Total)> GetAsync(string userId, NotificationsQuery q)
    {
        var (items,total) = await _repo.FindForUserAsync(userId, q.Page, q.PageSize, q.IsRead);
        var list = items.Select(n => new NotificationDto(
            n.Id.ToString(), n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt, n.ReadAt,
            n.Data != null ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,object>>(n.Data.ToJson()) : null
        ));
        return (list, total);
    }

    public Task<bool> MarkReadAsync(string userId,string notificationId) => _repo.MarkReadAsync(userId, notificationId);
    public Task<long> MarkAllReadAsync(string userId) => _repo.MarkAllReadAsync(userId);
}