using MyCabs.Application.DTOs;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IDriverService
{
    Task<(IEnumerable<CompanyDto> Items, long Total)> GetOpeningsAsync(CompaniesQuery q);
    Task ApplyAsync(string userId, DriverApplyDto dto);
    Task RespondInvitationAsync(string userId, string inviteId, string action);
}

public class DriverService : IDriverService
{
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;
    private readonly IApplicationRepository _apps;
    private readonly IInvitationRepository _invites;

    public DriverService(IDriverRepository drivers, ICompanyRepository companies, IApplicationRepository apps, IInvitationRepository invites)
    {
        _drivers = drivers; _companies = companies; _apps = apps; _invites = invites;
    }

    public async Task<(IEnumerable<CompanyDto> Items, long Total)> GetOpeningsAsync(CompaniesQuery q)
    {
        var (items, total) = await _companies.FindAsync(q.Page, q.PageSize, q.Search, q.Plan, q.ServiceType, q.Sort);
        return (items.Select(CompanyDto.FromEntity), total);
    }

    public async Task ApplyAsync(string userId, DriverApplyDto dto)
    {
        // Đảm bảo driver profile tồn tại
        var driver = await _drivers.CreateIfMissingAsync(userId);

        // Company tồn tại?
        var comp = await _companies.GetByIdAsync(dto.CompanyId);
        if (comp is null) throw new InvalidOperationException("COMPANY_NOT_FOUND");

        // Đã có pending application chưa?
        if (await _apps.ExistsPendingAsync(driver.Id.ToString(), dto.CompanyId))
            throw new InvalidOperationException("APPLICATION_ALREADY_PENDING");

        await _apps.CreateAsync(driver.Id.ToString(), dto.CompanyId);
    }

    public async Task RespondInvitationAsync(string userId, string inviteId, string action)
    {
        var driver = await _drivers.CreateIfMissingAsync(userId);
        var inv = await _invites.GetByIdAsync(inviteId);
        if (inv is null || inv.DriverId != driver.Id)
            throw new InvalidOperationException("INVITATION_NOT_FOUND");

        var newStatus = action == "Accept" ? "Accepted" : "Declined";
        await _invites.UpdateStatusAsync(inviteId, newStatus);

        if (newStatus == "Accepted")
        {
            await _drivers.SetCompanyAsync(driver.Id.ToString(), inv.CompanyId.ToString());
        }
    }
}