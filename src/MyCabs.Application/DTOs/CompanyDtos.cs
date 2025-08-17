using MyCabs.Domain.Entities;

namespace MyCabs.Application.DTOs;

public record CompaniesQuery(
    int Page = 1,
    int PageSize = 10,
    string? Search = null,
    string? Plan = null,
    string? ServiceType = null,
    string? Sort = "-createdAt"
);

public record AddCompanyServiceDto(string Type, string Title, decimal BasePrice);

public record CompanyDto(
    string Id,
    string Name,
    string? Description,
    string? Address,
    string? Plan,
    string? BillingCycle,
    DateTime? ExpiresAt,
    IEnumerable<CompanyServiceItem> Services
)
{
    public static CompanyDto FromEntity(Company c) => new(
        c.Id.ToString(),
        c.Name,
        c.Description,
        c.Address,
        c.Membership?.Plan,
        c.Membership?.BillingCycle,
        c.Membership?.ExpiresAt,
        c.Services
    );
}