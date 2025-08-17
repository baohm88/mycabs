using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class WalletRepository : IWalletRepository, IIndexInitializer
{
    private readonly IMongoCollection<Wallet> _col;
    public WalletRepository(IMongoContext ctx) => _col = ctx.GetCollection<Wallet>("wallets");

    public async Task<Wallet> GetOrCreateAsync(string ownerType, string ownerId)
    {
        if (!ObjectId.TryParse(ownerId, out var oid)) throw new ArgumentException("Invalid ownerId");
        var w = await _col.Find(x => x.OwnerType == ownerType && x.OwnerId == oid).FirstOrDefaultAsync();
        if (w != null) return w;
        w = new Wallet { Id = ObjectId.GenerateNewId(), OwnerType = ownerType, OwnerId = oid };
        await _col.InsertOneAsync(w); return w;
    }

    public async Task<Wallet?> GetByOwnerAsync(string ownerType, string ownerId)
    {
        if (!ObjectId.TryParse(ownerId, out var oid)) return null;
        return await _col.Find(x => x.OwnerType == ownerType && x.OwnerId == oid).FirstOrDefaultAsync();
    }

    public async Task<bool> TryDebitAsync(string walletId, decimal amount)
    {
        if (!ObjectId.TryParse(walletId, out var wid)) throw new ArgumentException("Invalid walletId");
        var filter = Builders<Wallet>.Filter.Eq(x => x.Id, wid) & Builders<Wallet>.Filter.Gte(x => x.Balance, amount);
        var update = Builders<Wallet>.Update.Inc(x => x.Balance, -amount).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(filter, update);
        return res.ModifiedCount == 1;
    }

    public async Task CreditAsync(string walletId, decimal amount)
    {
        if (!ObjectId.TryParse(walletId, out var wid)) throw new ArgumentException("Invalid walletId");
        var update = Builders<Wallet>.Update.Inc(x => x.Balance, amount).Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _col.UpdateOneAsync(x => x.Id == wid, update);
    }

    public async Task EnsureIndexesAsync()
    {
        var uniqueOwner = new CreateIndexModel<Wallet>(
            Builders<Wallet>.IndexKeys.Ascending(x => x.OwnerType).Ascending(x => x.OwnerId),
            new CreateIndexOptions { Unique = true }
        );
        var byUpdated = new CreateIndexModel<Wallet>(Builders<Wallet>.IndexKeys.Descending(x => x.UpdatedAt));
        await _col.Indexes.CreateManyAsync(new[] { uniqueOwner, byUpdated });
    }
}