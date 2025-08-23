using MongoDB.Driver;
using MongoDB.Bson;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class UserRepository : IUserRepository, IIndexInitializer
{
    private readonly IMongoCollection<User> _col;

    public UserRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<User>("users");
    }

    public async Task<User?> FindByEmailAsync(string email)
        => await _col.Find(x => x.Email == email).FirstOrDefaultAsync();

    public Task CreateAsync(User u)
        => _col.InsertOneAsync(u);

    public async Task EnsureIndexesAsync()
    {
        var ixEmailUnique = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true }
        );
        var ixEmailLower = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.EmailLower)
        );
        await _col.Indexes.CreateManyAsync(new[] { ixEmailUnique, ixEmailLower });
    }

    // === OTP helpers ===
    public async Task<User?> GetByEmailAsync(string emailLower)
        => await _col.Find(x => x.EmailLower == emailLower).FirstOrDefaultAsync();

    public async Task<bool> SetEmailVerifiedAsync(string emailLower)
    {
        var upd = Builders<User>.Update
            .Set(x => x.EmailVerified, true)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.EmailLower == emailLower, upd);
        return res.ModifiedCount > 0;
    }

    public async Task<bool> UpdatePasswordHashAsync(string emailLower, string newHash)
    {
        var upd = Builders<User>.Update
            .Set(x => x.PasswordHash, newHash)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.EmailLower == emailLower, upd);
        return res.ModifiedCount > 0;
    }

    public async Task<User?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return null;
        return await _col.Find(x => x.Id == oid).FirstOrDefaultAsync();
    }

    public async Task<bool> UpdateFullNameAsync(string id, string fullName)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;
        var upd = Builders<User>.Update.Set(x => x.FullName, fullName).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.Id == oid, upd);
        return res.ModifiedCount > 0;
    }
}
