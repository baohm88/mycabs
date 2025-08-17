using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IInvitationRepository
{
    Task<Invitation?> GetByIdAsync(string inviteId);
    Task UpdateStatusAsync(string inviteId, string status);
    Task EnsureIndexesAsync();
}