using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

// public interface IUserRepository
// {
//     Task<User?> GetByEmailAsync(string emailLower);
//     Task<bool> SetEmailVerifiedAsync(string emailLower);
//     Task<bool> UpdatePasswordHashAsync(string emailLower, string newHash);
// }

public interface IUserRepository
{
    // các method cũ bạn có sẵn, ví dụ:
    Task<User?> FindByEmailAsync(string email);
    Task CreateAsync(User u);

    // thêm 3 method phục vụ OTP:
    Task<User?> GetByEmailAsync(string emailLower);
    Task<bool> SetEmailVerifiedAsync(string emailLower);
    Task<bool> UpdatePasswordHashAsync(string emailLower, string newHash);
}