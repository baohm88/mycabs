using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email);
    Task CreateAsync(User u);
    Task EnsureIndexesAsync();

    // existing helpers
    Task<User?> GetByEmailAsync(string emailLower);
    Task<bool> SetEmailVerifiedAsync(string emailLower);
    Task<bool> UpdatePasswordHashAsync(string emailLower, string newHash);

    // NEW
    Task<User?> GetByIdAsync(string id);
    Task<bool> UpdateFullNameAsync(string id, string fullName);
}