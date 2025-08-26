using MyCabs.Application.DTOs;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IApplicationsQueryService
{
    Task<PagedResult<CompanyApplicationItemDto>> GetByCompanyAsync(string companyId, int page, int pageSize);
    Task<PagedResult<DriverApplicationItemDto>> GetByDriverAsync(string driverUserId, int page, int pageSize);
}

public class ApplicationsQueryService : IApplicationsQueryService
{
    private readonly IApplicationRepository _apps;   // giả định bạn đã có repo domain Applications
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;

    public ApplicationsQueryService(IApplicationRepository apps, IDriverRepository drivers, ICompanyRepository companies)
    { _apps = apps; _drivers = drivers; _companies = companies; }

    public async Task<PagedResult<CompanyApplicationItemDto>> GetByCompanyAsync(string companyId, int page, int pageSize)
    {
        var (items, total) = await _apps.FindByCompanyAsync(companyId, page, pageSize);
        var driverIds = items.Select(x => x.DriverId.ToString()).Distinct().ToArray();
        var drivers = await _drivers.GetByIdsAsync(driverIds);
        var dict = drivers.ToDictionary(d => d.Id.ToString(), d => d.FullName);

        var list = items.Select(a => new CompanyApplicationItemDto(
            a.Id.ToString(),
            a.DriverId.ToString(),
            dict.TryGetValue(a.DriverId.ToString(), out var name) ? name : null,
            a.Status,
            a.CreatedAt
        ));

        return new PagedResult<CompanyApplicationItemDto>(list, page, pageSize, total);
    }

    public async Task<PagedResult<DriverApplicationItemDto>> GetByDriverAsync(string driverUserId, int page, int pageSize)
    {
        // giả định repo tìm theo driverUserId (nếu của bạn là driverId thì đổi tham số)
        var (items, total) = await _apps.FindByDriverUserAsync(driverUserId, page, pageSize);

        var companyIds = items.Select(x => x.CompanyId.ToString()).Distinct().ToArray();
        var companies = await _companies.GetByIdsAsync(companyIds);
        var dict = companies.ToDictionary(c => c.Id.ToString(), c => c.Name);

        var list = items.Select(a => new DriverApplicationItemDto(
            a.Id.ToString(),
            a.CompanyId.ToString(),
            dict.TryGetValue(a.CompanyId.ToString(), out var name) ? name : null,
            a.Status,
            a.CreatedAt
        ));

        return new PagedResult<DriverApplicationItemDto>(list, page, pageSize, total);
    }
}
