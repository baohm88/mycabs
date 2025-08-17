using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class ApplicationRepository : IApplicationRepository, IIndexInitializer
{
    private readonly IMongoCollection<Application> _col;
    public ApplicationRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<Application>("applications");
    }

    public async Task<bool> ExistsPendingAsync(string driverId, string companyId)
    {
        if (!ObjectId.TryParse(driverId, out var did)) return false;
        if (!ObjectId.TryParse(companyId, out var cid)) return false;
        var filter = Builders<Application>.Filter.And(
            Builders<Application>.Filter.Eq(x => x.DriverId, did),
            Builders<Application>.Filter.Eq(x => x.CompanyId, cid),
            Builders<Application>.Filter.Eq(x => x.Status, "Pending")
        );
        var count = await _col.CountDocumentsAsync(filter);
        return count > 0;
    }

    public async Task CreateAsync(string driverId, string companyId)
    {
        if (!ObjectId.TryParse(driverId, out var did)) throw new ArgumentException("Invalid driverId");
        if (!ObjectId.TryParse(companyId, out var cid)) throw new ArgumentException("Invalid companyId");
        var app = new Application { Id = ObjectId.GenerateNewId(), DriverId = did, CompanyId = cid, Status = "Pending", CreatedAt = DateTime.UtcNow };
        await _col.InsertOneAsync(app);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Application>(Builders<Application>.IndexKeys.Ascending(x => x.DriverId));
        var ix2 = new CreateIndexModel<Application>(Builders<Application>.IndexKeys.Ascending(x => x.CompanyId));
        var ix3 = new CreateIndexModel<Application>(Builders<Application>.IndexKeys.Ascending(x => x.Status));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2, ix3 });
    }
}