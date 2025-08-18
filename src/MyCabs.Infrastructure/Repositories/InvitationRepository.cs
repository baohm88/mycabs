using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class InvitationRepository : IInvitationRepository, IIndexInitializer
{
    private readonly IMongoCollection<Invitation> _col;
    public InvitationRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<Invitation>("invitations");
    }

    public async Task<Invitation?> GetByIdAsync(string inviteId)
    {
        if (!ObjectId.TryParse(inviteId, out var iid)) return null;
        return await _col.Find(x => x.Id == iid).FirstOrDefaultAsync();
    }

    public async Task UpdateStatusAsync(string inviteId, string status)
    {
        if (!ObjectId.TryParse(inviteId, out var iid)) throw new ArgumentException("Invalid inviteId");
        var update = Builders<Invitation>.Update
            .Set(x => x.Status, status)
            .Set(x => x.CreatedAt, DateTime.UtcNow);
        await _col.UpdateOneAsync(x => x.Id == iid, update);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Invitation>(Builders<Invitation>.IndexKeys.Ascending(x => x.DriverId));
        var ix2 = new CreateIndexModel<Invitation>(Builders<Invitation>.IndexKeys.Ascending(x => x.CompanyId));
        var ix3 = new CreateIndexModel<Invitation>(Builders<Invitation>.IndexKeys.Ascending(x => x.Status));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2, ix3 });
    }
    public async Task CreateAsync(string companyId, string driverId, string? note)
    {
        if (!ObjectId.TryParse(companyId, out var cid)) throw new ArgumentException("Invalid companyId");
        if (!ObjectId.TryParse(driverId, out var did)) throw new ArgumentException("Invalid driverId");
        var inv = new Invitation { Id = ObjectId.GenerateNewId(), CompanyId = cid, DriverId = did, Status = "Pending", CreatedAt = DateTime.UtcNow, Note = note };
        await _col.InsertOneAsync(inv);
    }

    public async Task<(IEnumerable<Invitation> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? status)
    {
        if (!ObjectId.TryParse(companyId, out var cid)) return (Enumerable.Empty<Invitation>(), 0);
        var f = Builders<Invitation>.Filter.Eq(x => x.CompanyId, cid);
        if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Invitation>.Filter.Eq(x => x.Status, status);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<(IEnumerable<Invitation> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? status)
    {
        if (!ObjectId.TryParse(driverId, out var did)) return (Enumerable.Empty<Invitation>(), 0);
        var f = Builders<Invitation>.Filter.Eq(x => x.DriverId, did);
        if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Invitation>.Filter.Eq(x => x.Status, status);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }
}