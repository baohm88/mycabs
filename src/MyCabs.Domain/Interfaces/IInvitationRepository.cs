using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IInvitationRepository
{
    Task<Invitation?> GetByIdAsync(string inviteId);
    Task UpdateStatusAsync(string inviteId, string status);
    Task EnsureIndexesAsync();
    Task CreateAsync(string companyId, string driverId, string? note);
    Task<(IEnumerable<Invitation> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? status);
    Task<(IEnumerable<Invitation> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? status);
}