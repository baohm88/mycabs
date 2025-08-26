using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IApplicationRepository
{
    Task<bool> ExistsPendingAsync(string companyId, string driverId);
    Task CreateAsync(string companyId, string driverId);
    Task<(IEnumerable<Application> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? status);
    Task<(IEnumerable<Application> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? status);

    Task EnsureIndexesAsync();
    Task<Application?> GetByIdAsync(string appId);
    Task UpdateStatusAsync(string appId, string status);
    Task<(IEnumerable<Application> Items, long Total)> FindByCompanyAsync(string companyId, int page, int pageSize);

    Task<(IEnumerable<Application> Items, long Total)> FindByDriverUserAsync(string driverUserId, int page, int pageSize);
}