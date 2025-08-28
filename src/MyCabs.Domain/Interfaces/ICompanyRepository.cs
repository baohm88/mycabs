using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface ICompanyRepository
{
    Task<(IEnumerable<Company> Items, long Total)> FindAsync(
        int page, int pageSize, string? search, string? plan, string? serviceType, string? sort);
    Task<Company?> GetByIdAsync(string id);
    Task AddServiceAsync(string companyId, CompanyServiceItem item);
    Task EnsureIndexesAsync();
    Task UpdateMembershipAsync(string companyId, MembershipInfo info);
    Task<bool> UpdateMainAsync(string ownerUserId, string? name, string? description, string? address);

    Task<Company?> GetByOwnerUserIdAsync(string ownerUserId);
    Task<Company> CreateAsync(Company c);
    Task<Company> UpsertMainByOwnerAsync(string ownerUserId, string? name, string? description, string? address);
    Task<IEnumerable<Company>> GetByIdsAsync(IEnumerable<string> ids);
    Task<IReadOnlyList<Company>> GetManyByIdsAsync(IEnumerable<string> ids);
    Task<bool> UpdateProfileByOwnerAsync(
        string ownerUserId,
        string? name,
        string? description,
        string? address,
        List<CompanyServiceItem>? services,
        MembershipInfo? membership
    );

}