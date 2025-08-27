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

    public async Task<Driver?> GetByDriverIdAsync(string driverId)
    {
        if (!ObjectId.TryParse(driverId, out var uid)) return null;
        return await _col.Find(x => x.Id == uid).FirstOrDefaultAsync();
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
        var keys = Builders<Driver>.IndexKeys;

        var ix = new List<CreateIndexModel<Driver>>
    {
        new(keys.Ascending(x => x.UserId),   new CreateIndexOptions { Unique = true }),
        new(keys.Ascending(x => x.CompanyId)),
        new(keys.Ascending(x => x.Status))
    };

        await _col.Indexes.CreateManyAsync(ix);
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

    public async Task<bool> UpdateProfileAsync(string userId, string? phone, string? bio)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return false;

        var updates = new List<UpdateDefinition<Driver>>
    {
        Builders<Driver>.Update.Set(d => d.UpdatedAt, DateTime.UtcNow)
    };
        if (phone != null) updates.Add(Builders<Driver>.Update.Set(d => d.Phone, phone));
        if (bio != null) updates.Add(Builders<Driver>.Update.Set(d => d.Bio, bio));

        var update = Builders<Driver>.Update
            .Combine(updates)
            .SetOnInsert(d => d.Id, ObjectId.GenerateNewId())
            .SetOnInsert(d => d.UserId, uid)
            .SetOnInsert(d => d.Status, "available")
            .SetOnInsert(d => d.CreatedAt, DateTime.UtcNow);

        var res = await _col.UpdateOneAsync(
            filter: d => d.UserId == uid,
            update: update,
            options: new UpdateOptions { IsUpsert = true }
        );

        return res.ModifiedCount > 0 || res.UpsertedId != null;
    }

    public async Task<Driver> EnsureForUserAsync(string userId, string? phone = null, string? bio = null)
    {
        if (!ObjectId.TryParse(userId, out var uid))
            throw new ArgumentException("Invalid userId", nameof(userId));

        var exist = await _col.Find(x => x.UserId == uid).FirstOrDefaultAsync();
        if (exist != null) return exist;

        var now = DateTime.UtcNow;
        var doc = new Driver
        {
            Id = ObjectId.GenerateNewId(),
            UserId = uid,
            CompanyId = null,
            Status = "available",
            Phone = phone,
            Bio = bio,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _col.InsertOneAsync(doc);
        return doc;
    }


    public async Task<Driver> UpsertMainByUserAsync(string userId, string? fullName, string? phone, string? bio)
    {
        if (!ObjectId.TryParse(userId, out var uid))
            throw new ArgumentException("Invalid userId", nameof(userId));

        var updates = new List<UpdateDefinition<Driver>>
    {
        Builders<Driver>.Update.Set(d => d.UpdatedAt, DateTime.UtcNow)
    };
        if (fullName != null) updates.Add(Builders<Driver>.Update.Set(d => d.FullName, fullName));
        if (phone != null) updates.Add(Builders<Driver>.Update.Set(d => d.Phone, phone));
        if (bio != null) updates.Add(Builders<Driver>.Update.Set(d => d.Bio, bio));

        var update = Builders<Driver>.Update
            .Combine(updates)
            .SetOnInsert(d => d.Id, ObjectId.GenerateNewId())
            .SetOnInsert(d => d.UserId, uid)
            .SetOnInsert(d => d.Status, "available")
            .SetOnInsert(d => d.CreatedAt, DateTime.UtcNow);

        var res = await _col.UpdateOneAsync(
            filter: d => d.UserId == uid,
            update: update,
            options: new UpdateOptions { IsUpsert = true }
        );

        // Lấy document vừa upsert/updated (đảm bảo non-null theo interface)
        if (res.UpsertedId?.IsObjectId == true)
        {
            var insertedId = res.UpsertedId.AsObjectId;
            var inserted = await _col.Find(x => x.Id == insertedId).FirstOrDefaultAsync();
            if (inserted != null) return inserted;
        }

        var existing = await _col.Find(x => x.UserId == uid).FirstOrDefaultAsync();
        if (existing != null) return existing;

        throw new InvalidOperationException("Upsert failed to return a document.");
    }



    public async Task<IEnumerable<Driver>> GetByIdsAsync(IEnumerable<string> ids)
    {
        var oids = ids
            .Where(s => ObjectId.TryParse(s, out _))
            .Select(ObjectId.Parse)
            .ToArray();

        if (oids.Length == 0) return Array.Empty<Driver>();
        var f = Builders<Driver>.Filter.In(x => x.Id, oids);
        return await _col.Find(f).ToListAsync();
    }

    // Gán company + set status; idempotent nếu đã đúng công ty & trạng thái
    public async Task<bool> AssignDriverToCompanyAndSetStatusAsync(string driverId, string companyId, string status)
    {
        if (!ObjectId.TryParse(driverId, out var did)) return false;
        if (!ObjectId.TryParse(companyId, out var cid)) return false;

        var upd = Builders<Driver>.Update
            .Set(x => x.CompanyId, cid)
            .Set(x => x.Status, status)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        // chặn hire chéo: chỉ update nếu (chưa có company) hoặc (đã thuộc cùng company)
        var f = Builders<Driver>.Filter.Eq(x => x.Id, did)
              & (Builders<Driver>.Filter.Eq(x => x.CompanyId, null) | Builders<Driver>.Filter.Eq(x => x.CompanyId, cid));

        var res = await _col.UpdateOneAsync(f, upd);
        return res.ModifiedCount > 0;
    }

}