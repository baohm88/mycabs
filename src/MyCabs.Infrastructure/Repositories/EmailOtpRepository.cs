using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class EmailOtpRepository : IEmailOtpRepository, IIndexInitializer
{
    private readonly IMongoCollection<EmailOtp> _col;
    public EmailOtpRepository(IMongoContext ctx) => _col = ctx.GetCollection<EmailOtp>("email_otps");

    public Task InsertAsync(EmailOtp doc) => _col.InsertOneAsync(doc);

    public async Task<EmailOtp?> GetLatestActiveAsync(string emailLower, string purpose)
    {
        var now = DateTime.UtcNow;
        var f = Builders<EmailOtp>.Filter.Eq(x => x.EmailLower, emailLower)
              & Builders<EmailOtp>.Filter.Eq(x => x.Purpose, purpose)
              & Builders<EmailOtp>.Filter.Gt(x => x.ExpiresAt, now)
              & Builders<EmailOtp>.Filter.Eq(x => x.ConsumedAt, null);
        return await _col.Find(f).SortByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
    }

    public async Task<bool> ConsumeAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;
        var upd = Builders<EmailOtp>.Update.Set(x => x.ConsumedAt, DateTime.UtcNow).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.Id == oid && x.ConsumedAt == null, upd);
        return res.ModifiedCount > 0;
    }

    public async Task IncrementAttemptAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return;
        var upd = Builders<EmailOtp>.Update.Inc(x => x.AttemptCount, 1).Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _col.UpdateOneAsync(x => x.Id == oid, upd);
    }

    public async Task EnsureIndexesAsync()
    {
        // TTL xoá doc sau khi hết hạn ~10 phút buffer (TTL tính theo seconds, không chính xác tuyệt đối)
        var ttl = new CreateIndexModel<EmailOtp>(Builders<EmailOtp>.IndexKeys.Ascending(x => x.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.FromMinutes(10) });
        var combo = new CreateIndexModel<EmailOtp>(Builders<EmailOtp>.IndexKeys
            .Ascending(x => x.EmailLower).Ascending(x => x.Purpose).Descending(x => x.CreatedAt));
        await _col.Indexes.CreateManyAsync(new[] { ttl, combo });
    }
}