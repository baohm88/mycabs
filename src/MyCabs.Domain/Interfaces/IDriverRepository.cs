using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IDriverRepository
{
    Task<(IEnumerable<Driver> Items, long Total)> FindAsync(int page, int pageSize, string? search, string? companyId, string? sort);
    Task<Driver?> GetByDriverIdAsync(string driverId);
    Task<Driver> CreateIfMissingAsync(string userId);
    Task SetCompanyAsync(string driverId, string companyId);
    Task<Driver> EnsureForUserAsync(string userId, string? phone = null, string? bio = null);
    Task<bool> UpdateProfileAsync(string userId, string? phone, string? bio);
    Task<Driver> UpsertMainByUserAsync(string userId, string? fullName, string? phone, string? bio);
    Task<IEnumerable<Driver>> GetByIdsAsync(IEnumerable<string> ids);
    Task<bool> AssignDriverToCompanyAndSetStatusAsync(string driverId, string companyId, string status);
}