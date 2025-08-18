using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class RatingRepository : IRatingRepository, IIndexInitializer
{
    private readonly IMongoCollection<Rating> _col;
    public RatingRepository(IMongoContext ctx) => _col = ctx.GetCollection<Rating>("ratings");

    public Task CreateAsync(Rating r) => _col.InsertOneAsync(r);

    public async Task<(IEnumerable<Rating> Items, long Total)> FindForTargetAsync(string targetType, string targetId, int page, int pageSize)
    {
        if (!ObjectId.TryParse(targetId, out var tid)) return (Enumerable.Empty<Rating>(), 0);
        var f = Builders<Rating>.Filter.Eq(x => x.TargetType, targetType) & Builders<Rating>.Filter.Eq(x => x.TargetId, tid);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    // dùng typed Aggregate → tránh .Render(...) gây lỗi
    public async Task<(long Count, double Average)> GetSummaryAsync(string targetType, string targetId)
    {
        if (!ObjectId.TryParse(targetId, out var tid)) return (0, 0);

        var result = await _col.Aggregate()
            .Match(r => r.TargetType == targetType && r.TargetId == tid)
            .Group(r => 1, g => new { Count = g.Count(), Avg = g.Average(x => x.Stars) })
            .FirstOrDefaultAsync();

        if (result == null) return (0, 0);
        return (result.Count, result.Avg);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Rating>(Builders<Rating>.IndexKeys
            .Ascending(x => x.TargetType).Ascending(x => x.TargetId).Descending(x => x.CreatedAt));
        var ix2 = new CreateIndexModel<Rating>(Builders<Rating>.IndexKeys.Ascending(x => x.UserId));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2 });
    }
}
