using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;
using MyCabs.Domain.Entities;


// alias để tránh đụng tên với namespace MyCabs.Application
using AppEntity = MyCabs.Domain.Entities.Application;

namespace MyCabs.Infrastructure.Repositories;

public class ApplicationRepository : IApplicationRepository, IIndexInitializer
{
    private readonly IMongoCollection<AppEntity> _col;
    private readonly IMongoCollection<Driver> _drivers;

    public ApplicationRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<AppEntity>("applications");
        _drivers = ctx.GetCollection<Driver>("drivers");
    }

    public async Task<AppEntity?> GetByAppIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return null;
        AppEntity? app = await _col.Find(x => x.Id == oid).FirstOrDefaultAsync();
        return app;
    }

    public async Task<(IEnumerable<AppEntity> Items, long Total)> FindForCompanyAsync(
        string companyId, int page, int pageSize, string? status)
    {
        var cid = ObjectId.Parse(companyId);
        var f = Builders<AppEntity>.Filter.Eq(x => x.CompanyId, cid);
        if (!string.IsNullOrEmpty(status))
            f &= Builders<AppEntity>.Filter.Eq(x => x.Status, status);

        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IEnumerable<AppEntity> Items, long Total)> FindForDriverAsync(
        string driverId, int page, int pageSize, string? status)
    {
        var did = ObjectId.Parse(driverId);
        var f = Builders<AppEntity>.Filter.Eq(x => x.DriverId, did);
        if (!string.IsNullOrEmpty(status))
            f &= Builders<AppEntity>.Filter.Eq(x => x.Status, status);

        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<bool> ExistsPendingAsync(string companyId, string driverId)
    {
        var cid = ObjectId.Parse(companyId);
        var did = ObjectId.Parse(driverId);

        var f = Builders<AppEntity>.Filter.Eq(x => x.CompanyId, cid)
              & Builders<AppEntity>.Filter.Eq(x => x.DriverId, did)
              & Builders<AppEntity>.Filter.Eq(x => x.Status, "Pending");

        var count = await _col.CountDocumentsAsync(f);
        return count > 0;
    }

    public Task CreateAsync(string companyId, string driverId)
    {
        var doc = new AppEntity
        {
            Id = ObjectId.GenerateNewId(),
            CompanyId = ObjectId.Parse(companyId),
            DriverId = ObjectId.Parse(driverId),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        return _col.InsertOneAsync(doc);
    }

    public async Task UpdateAppStatusAsync(string appId, string status)
    {
        if (!ObjectId.TryParse(appId, out var oid)) return;
        var upd = Builders<AppEntity>.Update
            .Set(x => x.Status, status)
            .Set("updatedAt", DateTime.UtcNow);

        await _col.UpdateOneAsync(x => x.Id == oid, upd);
    }

    public async Task EnsureIndexesAsync()
    {
        var indexes = new List<CreateIndexModel<AppEntity>>
        {
            // companyId + createdAt desc
            new CreateIndexModel<AppEntity>(
                Builders<AppEntity>.IndexKeys
                    .Ascending(x => x.CompanyId)
                    .Descending(x => x.CreatedAt)
            ),
            // driverId + createdAt desc
            new CreateIndexModel<AppEntity>(
                Builders<AppEntity>.IndexKeys
                    .Ascending(x => x.DriverId)
                    .Descending(x => x.CreatedAt)
            ),
            // status + createdAt desc
            new CreateIndexModel<AppEntity>(
                Builders<AppEntity>.IndexKeys
                    .Ascending(x => x.Status)
                    .Descending(x => x.CreatedAt)
            )
        };

        await _col.Indexes.CreateManyAsync(indexes);
    }

    public async Task<(IEnumerable<AppEntity> Items, long Total)> FindByCompanyAsync(string companyId, int page, int pageSize)
    {
        if (!ObjectId.TryParse(companyId, out var cid))
            return (Enumerable.Empty<AppEntity>(), 0);

        var f = Builders<AppEntity>.Filter.Eq(x => x.CompanyId, cid);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IEnumerable<AppEntity> Items, long Total)> FindByDriverUserAsync(string driverUserId, int page, int pageSize)
    {
        if (!ObjectId.TryParse(driverUserId, out var uid))
            return (Enumerable.Empty<AppEntity>(), 0);

        // Trường hợp 1: collection Application có field DriverUserId => lọc trực tiếp
        var fByUser = Builders<AppEntity>.Filter.Eq("DriverUserId", uid);

        var total = await _col.CountDocumentsAsync(fByUser);
        if (total > 0)
        {
            var items = await _col.Find(fByUser)
                .SortByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return (items, total);
        }

        // Trường hợp 2 (fallback): nếu không có DriverUserId, lookup Driver theo UserId rồi lọc theo DriverId
        // (chỉ cần nếu schema của bạn không lưu DriverUserId trong Application)
        var d = await _drivers.Find(x => x.UserId == uid).FirstOrDefaultAsync();
        if (d == null) return (Enumerable.Empty<AppEntity>(), 0);

        var fByDriverId = Builders<AppEntity>.Filter.Eq(x => x.DriverId, d.Id);
        var total2 = await _col.CountDocumentsAsync(fByDriverId);
        var items2 = await _col.Find(fByDriverId)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items2, total2);
    }

    // Reject tất cả application Pending của driver (trừ app hiện hành)
    public async Task<long> RejectPendingByDriverExceptAsync(string driverId, string exceptAppId)
    {
        if (!ObjectId.TryParse(driverId, out var did)) return 0;
        if (!ObjectId.TryParse(exceptAppId, out var aid)) return 0;

        var f = Builders<AppEntity>.Filter.Eq(x => x.DriverId, did)
              & Builders<AppEntity>.Filter.Ne(x => x.Id, aid)
              & Builders<AppEntity>.Filter.Eq(x => x.Status, "Pending");

        var upd = Builders<AppEntity>.Update
            .Set(x => x.Status, "Rejected")
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }
}
