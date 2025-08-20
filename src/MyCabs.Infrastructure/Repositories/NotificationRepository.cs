using System.Linq;
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
    public NotificationRepository(IMongoContext ctx)
        => _col = ctx.GetCollection<Notification>("notifications");

    public Task CreateAsync(Notification n) => _col.InsertOneAsync(n);

    public async Task<(IEnumerable<Notification> Items,long Total)> FindAsync(string userId, int page, int pageSize, bool? unreadOnly)
    {
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, ObjectId.Parse(userId))
              & Builders<Notification>.Filter.Eq(x => x.DeletedAt, null);

        if (unreadOnly == true)
            f &= Builders<Notification>.Filter.Eq(x => x.ReadAt, null);

        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task<long> CountUnreadAsync(string userId)
    {
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, ObjectId.Parse(userId))
              & Builders<Notification>.Filter.Eq(x => x.ReadAt, null)
              & Builders<Notification>.Filter.Eq(x => x.DeletedAt, null);
        return _col.CountDocumentsAsync(f);
    }

    public async Task<bool> MarkReadAsync(string userId, string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;

        var f = Builders<Notification>.Filter.Where(x =>
            x.Id == oid &&
            x.UserId == ObjectId.Parse(userId) &&
            x.ReadAt == null);

        var upd = Builders<Notification>.Update
            .Set(x => x.ReadAt, DateTime.UtcNow); // bỏ UpdatedAt nếu entity không có

        var res = await _col.UpdateOneAsync(f, upd);
        return res.ModifiedCount > 0;
    }

    public async Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids)
    {
        var validOids = ids
            .Select(s => ObjectId.TryParse(s, out var oid) ? (ObjectId?)oid : null)
            .Where(oid => oid.HasValue)
            .Select(oid => oid!.Value)
            .ToArray();
        if (validOids.Length == 0) return 0;

        var f = Builders<Notification>.Filter.In(x => x.Id, validOids)
              & Builders<Notification>.Filter.Eq(x => x.UserId, ObjectId.Parse(userId))
              & Builders<Notification>.Filter.Eq(x => x.ReadAt, null);

        var upd = Builders<Notification>.Update
            .Set(x => x.ReadAt, DateTime.UtcNow);

        var res = await _col.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }

    public async Task<long> MarkAllReadAsync(string userId)
    {
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, ObjectId.Parse(userId))
              & Builders<Notification>.Filter.Eq(x => x.ReadAt, null)
              & Builders<Notification>.Filter.Eq(x => x.DeletedAt, null);

        var upd = Builders<Notification>.Update
            .Set(x => x.ReadAt, DateTime.UtcNow);

        var res = await _col.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }

    public async Task<bool> SoftDeleteAsync(string userId, string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;

        var f = Builders<Notification>.Filter.Where(x =>
            x.Id == oid &&
            x.UserId == ObjectId.Parse(userId) &&
            x.DeletedAt == null);

        var upd = Builders<Notification>.Update
            .Set(x => x.DeletedAt, DateTime.UtcNow);

        var res = await _col.UpdateOneAsync(f, upd);
        return res.ModifiedCount > 0;
    }

    public async Task EnsureIndexesAsync()
    {
        var idx1 = new CreateIndexModel<Notification>(
            Builders<Notification>.IndexKeys.Combine(
                Builders<Notification>.IndexKeys.Ascending(x => x.UserId),
                Builders<Notification>.IndexKeys.Descending(x => x.CreatedAt)
            )
        );

        var idx2 = new CreateIndexModel<Notification>(
            Builders<Notification>.IndexKeys.Combine(
                Builders<Notification>.IndexKeys.Ascending(x => x.UserId),
                Builders<Notification>.IndexKeys.Ascending(x => x.ReadAt)
            )
        );

        var idx3 = new CreateIndexModel<Notification>(
            Builders<Notification>.IndexKeys.Ascending(x => x.DeletedAt)
        );

        await _col.Indexes.CreateManyAsync(new[] { idx1, idx2, idx3 });
    }
}
