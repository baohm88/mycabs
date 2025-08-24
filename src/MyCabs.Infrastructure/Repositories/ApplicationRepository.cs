using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

// alias để tránh đụng tên với namespace MyCabs.Application
using ApplicationEntity = MyCabs.Domain.Entities.Application;

namespace MyCabs.Infrastructure.Repositories;

public class ApplicationRepository : IApplicationRepository, IIndexInitializer
{
    private readonly IMongoCollection<ApplicationEntity> _col;

    public ApplicationRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<ApplicationEntity>("applications");
    }

    public async Task<ApplicationEntity?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return null;
        ApplicationEntity? app = await _col.Find(x => x.Id == oid).FirstOrDefaultAsync();
        return app;
    }

    public async Task<(IEnumerable<ApplicationEntity> Items, long Total)> FindForCompanyAsync(
        string companyId, int page, int pageSize, string? status)
    {
        var cid = ObjectId.Parse(companyId);
        var f = Builders<ApplicationEntity>.Filter.Eq(x => x.CompanyId, cid);
        if (!string.IsNullOrEmpty(status))
            f &= Builders<ApplicationEntity>.Filter.Eq(x => x.Status, status);

        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f)
            .SortByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<(IEnumerable<ApplicationEntity> Items, long Total)> FindForDriverAsync(
        string driverId, int page, int pageSize, string? status)
    {
        var did = ObjectId.Parse(driverId);
        var f = Builders<ApplicationEntity>.Filter.Eq(x => x.DriverId, did);
        if (!string.IsNullOrEmpty(status))
            f &= Builders<ApplicationEntity>.Filter.Eq(x => x.Status, status);

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

        var f = Builders<ApplicationEntity>.Filter.Eq(x => x.CompanyId, cid)
              & Builders<ApplicationEntity>.Filter.Eq(x => x.DriverId, did)
              & Builders<ApplicationEntity>.Filter.Eq(x => x.Status, "Pending");

        var count = await _col.CountDocumentsAsync(f);
        return count > 0;
    }

    public Task CreateAsync(string companyId, string driverId)
    {
        var doc = new ApplicationEntity
        {
            Id = ObjectId.GenerateNewId(),
            CompanyId = ObjectId.Parse(companyId),
            DriverId = ObjectId.Parse(driverId),
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };
        return _col.InsertOneAsync(doc);
    }

    public async Task UpdateStatusAsync(string id, string status)
    {
        if (!ObjectId.TryParse(id, out var oid)) return;

        // Dùng tên field string cho updatedAt để không phụ thuộc property
        var upd = Builders<ApplicationEntity>.Update
            .Set(x => x.Status, status)
            .Set("updatedAt", DateTime.UtcNow);

        await _col.UpdateOneAsync(x => x.Id == oid, upd);
    }

    public async Task EnsureIndexesAsync()
    {
        var indexes = new List<CreateIndexModel<ApplicationEntity>>
        {
            // companyId + createdAt desc
            new CreateIndexModel<ApplicationEntity>(
                Builders<ApplicationEntity>.IndexKeys
                    .Ascending(x => x.CompanyId)
                    .Descending(x => x.CreatedAt)
            ),
            // driverId + createdAt desc
            new CreateIndexModel<ApplicationEntity>(
                Builders<ApplicationEntity>.IndexKeys
                    .Ascending(x => x.DriverId)
                    .Descending(x => x.CreatedAt)
            ),
            // status + createdAt desc
            new CreateIndexModel<ApplicationEntity>(
                Builders<ApplicationEntity>.IndexKeys
                    .Ascending(x => x.Status)
                    .Descending(x => x.CreatedAt)
            )
        };

        await _col.Indexes.CreateManyAsync(indexes);
    }
}
