using MongoDB.Driver;
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

    // Trả về User? nên viết async/await để khớp nullability
    public async Task<User?> FindByEmailAsync(string email)
    {
        User? user = await _col
            .Find(x => x.Email == email)
            .FirstOrDefaultAsync();
        return user;
    }

    public Task CreateAsync(User u)
        => _col.InsertOneAsync(u);

    public async Task EnsureIndexesAsync()
    {
        var ix = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true }
        );
        await _col.Indexes.CreateOneAsync(ix);
    }
}