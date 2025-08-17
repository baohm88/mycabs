using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email);
    Task CreateAsync(User user);
}