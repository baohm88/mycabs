using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IDriverRepository
{
    Task<Driver?> GetByUserIdAsync(string userId);
    Task<Driver> CreateIfMissingAsync(string userId);
    Task SetCompanyAsync(string driverId, string companyId);
    Task<(IEnumerable<Driver> Items, long Total)> FindAsync(int page, int pageSize, string? search, string? companyId, string? sort);

}