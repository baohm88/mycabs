using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class TransactionRepository : ITransactionRepository, IIndexInitializer
{
    private readonly IMongoCollection<Transaction> _col;
    public TransactionRepository(IMongoContext ctx) => _col = ctx.GetCollection<Transaction>("transactions");

    public Task CreateAsync(Transaction tx) => _col.InsertOneAsync(tx);

    public async Task<(IEnumerable<Transaction> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? type, string? status)
    {
        if (!ObjectId.TryParse(companyId, out var cid)) return (Enumerable.Empty<Transaction>(), 0);
        var f = Builders<Transaction>.Filter.Eq(x => x.CompanyId, cid);
        if (!string.IsNullOrWhiteSpace(type)) f &= Builders<Transaction>.Filter.Eq(x => x.Type, type);
        if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Transaction>.Filter.Eq(x => x.Status, status);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<(IEnumerable<Transaction> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? type, string? status)
    {
        if (!ObjectId.TryParse(driverId, out var did)) return (Enumerable.Empty<Transaction>(), 0);
        var f = Builders<Transaction>.Filter.Eq(x => x.DriverId, did);
        if (!string.IsNullOrWhiteSpace(type)) f &= Builders<Transaction>.Filter.Eq(x => x.Type, type);
        if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Transaction>.Filter.Eq(x => x.Status, status);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Descending(x => x.CreatedAt));
        var ix2 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Ascending(x => x.CompanyId));
        var ix3 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Ascending(x => x.DriverId));
        var ix4 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Ascending(x => x.Type));
        var ix5 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Ascending(x => x.Status));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2, ix3, ix4, ix5 });
    }

    public async Task<(IEnumerable<Transaction> Items, long Total)> FindByWalletAsync(string walletId, int page, int pageSize)
    {
        var wid = ObjectId.Parse(walletId);
        var f = Builders<Transaction>.Filter.Or(
        Builders<Transaction>.Filter.Eq(x => x.FromWalletId, wid),
        Builders<Transaction>.Filter.Eq(x => x.ToWalletId, wid)
        );
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f)
        .SortByDescending(x => x.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Limit(pageSize)
        .ToListAsync();
        return (items, total);
    }
}