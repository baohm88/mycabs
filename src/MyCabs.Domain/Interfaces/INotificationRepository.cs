using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface INotificationRepository
{
    Task CreateAsync(Notification n);
    Task<(IEnumerable<Notification> Items,long Total)> FindAsync(string userId, int page, int pageSize, bool? unreadOnly);

    // New
    Task<long> CountUnreadAsync(string userId);
    Task<bool> MarkReadAsync(string userId, string id);
    Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids);
    Task<long> MarkAllReadAsync(string userId);
    Task<bool> SoftDeleteAsync(string userId, string id);
    Task EnsureIndexesAsync();
}