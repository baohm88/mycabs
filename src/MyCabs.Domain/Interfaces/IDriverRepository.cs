using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IDriverRepository
{
    Task<(IEnumerable<Driver> Items, long Total)> FindAsync(int page, int pageSize, string? search, string? companyId, string? sort);
    Task<Driver?> GetByIdAsync(string id);
    Task<Driver?> GetByUserIdAsync(string userId);
    Task<Driver> CreateIfMissingAsync(string userId);
    Task SetCompanyAsync(string driverId, string companyId);
    Task<Driver> EnsureForUserAsync(string userId, string? phone = null, string? bio = null);
    Task<bool> UpdateProfileAsync(string userId, string? phone, string? bio);
    Task<Driver> UpsertMainByUserAsync(string userId, string? fullName, string? phone, string? bio);
}