using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IApplicationRepository
{
    Task<bool> ExistsPendingAsync(string driverId, string companyId);
    Task CreateAsync(string driverId, string companyId);
    Task EnsureIndexesAsync();
}