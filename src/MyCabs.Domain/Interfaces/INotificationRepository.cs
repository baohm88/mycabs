using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface INotificationRepository
{
    Task CreateAsync(Notification n);
    Task<(IEnumerable<Notification> Items, long Total)> FindForUserAsync(string userId, int page, int pageSize, bool? isRead);
    Task<bool> MarkReadAsync(string userId, string notificationId);
    Task<long> MarkAllReadAsync(string userId);
    Task EnsureIndexesAsync();
}