using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class NotificationRepository : INotificationRepository, IIndexInitializer
{
    private readonly IMongoCollection<Notification> _col;
    public NotificationRepository(IMongoContext ctx) => _col = ctx.GetCollection<Notification>("notifications");

    public Task CreateAsync(Notification n) => _col.InsertOneAsync(n);

    public async Task<(IEnumerable<Notification> Items, long Total)> FindForUserAsync(string userId, int page, int pageSize, bool? isRead)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return (Enumerable.Empty<Notification>(), 0);
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, uid);
        if (isRead.HasValue) f &= Builders<Notification>.Filter.Eq(x => x.IsRead, isRead.Value);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<bool> MarkReadAsync(string userId, string notificationId)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return false;
        if (!ObjectId.TryParse(notificationId, out var nid)) return false;
        var upd = Builders<Notification>.Update.Set(x => x.IsRead, true).Set(x => x.ReadAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.Id == nid && x.UserId == uid && !x.IsRead, upd);
        return res.ModifiedCount > 0;
    }

    public async Task<long> MarkAllReadAsync(string userId)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return 0;
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, uid) & Builders<Notification>.Filter.Eq(x => x.IsRead, false);
        var upd = Builders<Notification>.Update.Set(x => x.IsRead, true).Set(x => x.ReadAt, DateTime.UtcNow);
        var res = await _col.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Notification>(Builders<Notification>.IndexKeys
            .Ascending(x => x.UserId).Descending(x => x.CreatedAt));
        var ix2 = new CreateIndexModel<Notification>(Builders<Notification>.IndexKeys
            .Ascending(x => x.UserId).Ascending(x => x.IsRead).Descending(x => x.CreatedAt));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2 });
    }
}