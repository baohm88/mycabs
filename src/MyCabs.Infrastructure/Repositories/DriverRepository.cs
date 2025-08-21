using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class DriverRepository : IDriverRepository, IIndexInitializer
{
    private readonly IMongoCollection<Driver> _col;
    public DriverRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<Driver>("drivers");
    }

    public async Task<Driver?> GetByUserIdAsync(string userId)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return null;
        return await _col.Find(x => x.UserId == uid).FirstOrDefaultAsync();
    }

    public async Task<Driver> CreateIfMissingAsync(string userId)
    {
        if (!ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
        var d = await _col.Find(x => x.UserId == uid).FirstOrDefaultAsync();
        if (d != null) return d;
        d = new Driver { Id = ObjectId.GenerateNewId(), UserId = uid, Status = "available" };
        await _col.InsertOneAsync(d);
        return d;
    }

    public async Task SetCompanyAsync(string driverId, string companyId)
    {
        if (!ObjectId.TryParse(driverId, out var did)) throw new ArgumentException("Invalid driverId");
        if (!ObjectId.TryParse(companyId, out var cid)) throw new ArgumentException("Invalid companyId");
        var update = Builders<Driver>.Update
            .Set(x => x.CompanyId, cid)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _col.UpdateOneAsync(x => x.Id == did, update);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Driver>(Builders<Driver>.IndexKeys.Ascending(x => x.UserId), new CreateIndexOptions { Unique = true });
        var ix2 = new CreateIndexModel<Driver>(Builders<Driver>.IndexKeys.Ascending(x => x.CompanyId));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2 });
    }

    public async Task<(IEnumerable<Driver> Items, long Total)> FindAsync(int page, int pageSize, string? search, string? companyId, string? sort)
    {
        var f = Builders<Driver>.Filter.Empty;
        if (!string.IsNullOrWhiteSpace(search))
            f &= Builders<Driver>.Filter.Or(
                Builders<Driver>.Filter.Regex(x => x.FullName, new BsonRegularExpression(search, "i")),
                Builders<Driver>.Filter.Regex(x => x.Phone, new BsonRegularExpression(search, "i"))
            );
        if (!string.IsNullOrWhiteSpace(companyId) && ObjectId.TryParse(companyId, out var cid))
            f &= Builders<Driver>.Filter.Eq(x => x.CompanyId, cid);

        var q = _col.Find(f);
        q = (sort?.ToLower()) switch
        {
            "name_asc" => q.SortBy(x => x.FullName),
            "name_desc" => q.SortByDescending(x => x.FullName),
            _ => q.SortByDescending(x => x.CreatedAt)
        };
        var total = await _col.CountDocumentsAsync(f);
        var items = await q.Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    

}