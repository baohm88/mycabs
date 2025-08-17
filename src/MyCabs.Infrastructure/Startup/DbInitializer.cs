// using MyCabs.Domain.Interfaces;

// namespace MyCabs.Infrastructure.Startup;

// public class DbInitializer
// {
//     private readonly IUserRepository _users;   // ✅ dùng interface
//     public DbInitializer(IUserRepository users) { _users = users; }

//     public async Task EnsureIndexesAsync() => await (_users as MyCabs.Infrastructure.Repositories.UserRepository)!.EnsureIndexesAsync();

// }

namespace MyCabs.Infrastructure.Startup;

public class DbInitializer
{
    private readonly IEnumerable<IIndexInitializer> _inits;
    public DbInitializer(IEnumerable<IIndexInitializer> inits) { _inits = inits; }

    public async Task EnsureIndexesAsync()
    {
        foreach (var i in _inits)
            await i.EnsureIndexesAsync();
    }
}