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
}