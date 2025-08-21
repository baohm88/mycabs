using System.Linq;
using System.Text.Json;
using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Application.Realtime;       // << dùng interface từ Application
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface INotificationService
{
    Task PublishAsync(string userId, CreateNotificationDto dto);
    Task<(IEnumerable<NotificationDto> Items, long Total)> GetAsync(string userId, NotificationsQuery q);
    Task<bool> MarkReadAsync(string userId, string notificationId);
    Task<long> MarkAllReadAsync(string userId);
    Task<long> GetUnreadCountAsync(string userId);
    Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids);
    Task<bool> DeleteAsync(string userId, string id);
}

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    private readonly IRealtimeNotifier _rt;
    public NotificationService(INotificationRepository repo, IRealtimeNotifier rt) { _repo = repo; _rt = rt; }

    public Task<long> GetUnreadCountAsync(string userId) => _repo.CountUnreadAsync(userId);

    public async Task<long> MarkAllReadAsync(string userId)
    {
        var n = await _repo.MarkAllReadAsync(userId);
        await _rt.PushUnreadCountAsync(userId); // realtime
        return n;
    }

    public async Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids)
    {
        var n = await _repo.MarkReadBulkAsync(userId, ids);
        if (n > 0) await _rt.PushUnreadCountAsync(userId);
        return n;
    }

    public async Task<bool> MarkReadAsync(string userId, string id)
    {
        var ok = await _repo.MarkReadAsync(userId, id);
        if (ok) await _rt.PushUnreadCountAsync(userId);
        return ok;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var ok = await _repo.SoftDeleteAsync(userId, id);
        if (ok) await _rt.PushUnreadCountAsync(userId);
        return ok;
    }

    // public async Task PublishAsync(string userId, CreateNotificationDto dto)
    // {
    //     if (!ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
    //     var n = new Notification
    //     {
    //         Id = ObjectId.GenerateNewId(),
    //         UserId = uid,
    //         Type = dto.Type,
    //         Title = dto.Title,
    //         Message = dto.Message,
    //         Data = dto.Data != null ? BsonDocument.Parse(JsonSerializer.Serialize(dto.Data)) : null,
    //         CreatedAt = DateTime.UtcNow
    //     };
    //     await _repo.CreateAsync(n);

    //     var payload = new NotificationDto(
    //         n.Id.ToString(), n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt, n.ReadAt, dto.Data
    //     );

    //     await _rt.NotifyUserAsync(userId, "notification", payload);
    // }

    public async Task PublishAsync(string userId, CreateNotificationDto dto)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
        var n = new Notification
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId(),
            UserId = uid,
            Type = dto.Type,
            Title = dto.Title,
            Message = dto.Message,
            Data = dto.Data != null
                ? MongoDB.Bson.BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dto.Data))
                : null,
            CreatedAt = DateTime.UtcNow
        };
        await _repo.CreateAsync(n);

        var payload = new NotificationDto(
            n.Id.ToString(), n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt, n.ReadAt, dto.Data
        );
        await _rt.NotifyUserAsync(userId, "notification", payload);
    }

    public async Task<(IEnumerable<NotificationDto> Items, long Total)> GetAsync(string userId, NotificationsQuery q)
    {
        var (items, total) = await _repo.FindAsync(userId, q.Page, q.PageSize, q.IsRead);
        var list = items.Select(n => new NotificationDto(
            n.Id.ToString(), n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt, n.ReadAt,
            n.Data != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(n.Data.ToJson()) : null
        ));
        return (list, total);
    }
}
