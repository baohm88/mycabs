using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Interfaces;
using MyCabs.Domain.Entities;


namespace MyCabs.Application.Services;

public interface IHiringService
{
    Task ApplyAsync(string userId, DriverApplyDto dto);
    Task<(IEnumerable<ApplicationDto> Items, long Total)> GetMyApplicationsAsync(string userId, ApplicationsQuery q);
    Task<(IEnumerable<ApplicationDto> Items, long Total)> GetCompanyApplicationsAsync(string companyId, ApplicationsQuery q);
    Task ApproveApplicationAsync(string companyId, string appId);
    Task RejectApplicationAsync(string companyId, string appId);
    Task InviteDriverAsync(string companyId, InviteDriverDto dto);
    Task<(IEnumerable<InvitationDto> Items, long Total)> GetCompanyInvitationsAsync(string companyId, InvitationsQuery q);
    // Task<(IEnumerable<InvitationDto> Items, long Total)> GetMyInvitationsAsync(string userId, InvitationsQuery q);
    Task<(IEnumerable<InvitationDto> items, long total)> GetMyInvitationsAsync(string currentUserId, InvitationsQuery q);

}

public class HiringService : IHiringService
{
    private readonly IApplicationRepository _apps;
    private readonly IInvitationRepository _invites;
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;
    private readonly IUserRepository _users;

    public HiringService(
        IApplicationRepository apps,
        IInvitationRepository invites,
        IDriverRepository drivers,
        ICompanyRepository companies,
        IUserRepository users)
    { _apps = apps; _invites = invites; _drivers = drivers; _companies = companies; _users = users; }

    static ApplicationDto MapApp(MyCabs.Domain.Entities.Application a)
        => new(a.Id.ToString(), a.DriverId.ToString(), a.CompanyId.ToString(), a.Status, a.CreatedAt);
    static InvitationDto MapInv(MyCabs.Domain.Entities.Invitation i)
        => new(i.Id.ToString(), i.CompanyId.ToString(), i.DriverId.ToString(), i.Status, i.CreatedAt, i.Note);

    public async Task ApplyAsync(string userId, DriverApplyDto dto)
    {
        // Lấy driver theo userId (và tự tạo nếu chưa có để không bị null)
        var d = await _drivers.GetByDriverIdAsync(userId)
              ?? await _drivers.UpsertMainByUserAsync(userId, fullName: null, phone: null, bio: null);

        // Tìm company theo Id client gửi
        var c = await _companies.GetByIdAsync(dto.CompanyId);
        if (c == null) throw new InvalidOperationException("COMPANY_NOT_FOUND");

        // Không cho apply 2 lần ở trạng thái Pending
        if (await _apps.ExistsPendingAsync(c.Id.ToString(), d.Id.ToString()))
            throw new InvalidOperationException("APPLICATION_ALREADY_PENDING");

        // CHANGED: chèn đúng cặp ID
        await _apps.CreateAsync(c.Id.ToString(), d.Id.ToString());
    }

    public async Task<(IEnumerable<ApplicationDto> Items, long Total)> GetCompanyApplicationsAsync(string companyId, ApplicationsQuery q)
    { var (items, total) = await _apps.FindForCompanyAsync(companyId, q.Page, q.PageSize, q.Status); return (items.Select(MapApp), total); }


    public async Task InviteDriverAsync(string companyId, InviteDriverDto dto)
    {
        // từ userId → driver profile
        var driver = await _drivers.GetByDriverIdAsync(dto.DriverUserId) ?? throw new InvalidOperationException("DRIVER_NOT_FOUND");
        await _invites.CreateAsync(companyId, driver.Id.ToString(), dto.Note);
    }

    public async Task<(IEnumerable<InvitationDto> Items, long Total)> GetCompanyInvitationsAsync(string companyId, InvitationsQuery q)
    { var (items, total) = await _invites.FindForCompanyAsync(companyId, q.Page, q.PageSize, q.Status); return (items.Select(MapInv), total); }

    public async Task<(IEnumerable<ApplicationDto> Items, long Total)> GetMyApplicationsAsync(string userId, ApplicationsQuery q)
    {
        var d = await _drivers.GetByDriverIdAsync(userId);
        if (d == null) throw new InvalidOperationException("DRIVER_NOT_FOUND");

        // Chuẩn: truy vấn theo driver.Id
        var (items, total) = await _apps.FindForDriverAsync(d.Id.ToString(), q.Page, q.PageSize, q.Status);

        // map về DTO
        var dtos = items.Select(a => new ApplicationDto(
            a.Id.ToString(),
            a.CompanyId.ToString(),
            a.DriverId.ToString(),
            a.Status,
            a.CreatedAt
        ));

        return (dtos, total);
    }

    public async Task<(IEnumerable<InvitationDto> items, long total)> GetMyInvitationsAsync(string currentUserId, InvitationsQuery q)
    {
        var user = await _users.GetByIdAsync(currentUserId) ?? throw new InvalidOperationException("USER_NOT_FOUND");
        var driver = await _drivers.GetByUserIdAsync(currentUserId);
        var driverId = driver?.Id.ToString();
        var emailLower = user.EmailLower;

        var (items, total) = await _invites.FindForCandidateAsync(
            page: q.Page, pageSize: q.PageSize,
            candidateDriverId: driverId,
            candidateEmail: emailLower,
            status: q.Status
        );

        return (items.Select(MapInv), total);
    }



    public async Task ApproveApplicationAsync(string companyId, string appId)
    {
        var app = await _apps.GetByAppIdAsync(appId) ?? throw new InvalidOperationException("APPLICATION_NOT_FOUND");
        if (app.CompanyId.ToString() != companyId) throw new InvalidOperationException("FORBIDDEN");

        // Idempotent: nếu đã duyệt & driver đã ở đúng công ty thì thoát sớm
        if (string.Equals(app.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            return;

        // Kiểm tra driver
        var driver = await _drivers.GetByDriverIdAsync(app.DriverId.ToString());
        if (driver == null) throw new InvalidOperationException("DRIVER_NOT_FOUND");

        // Cập nhật application trước
        await _apps.UpdateAppStatusAsync(appId, "Approved");

        // Hire driver → gán company + set status = hired
        var ok = await _drivers.AssignDriverToCompanyAndSetStatusAsync(app.DriverId.ToString(), companyId, "hired");
        if (!ok)
        {
            // roll-forward hợp lý: app vẫn approved nhưng báo xung đột để FE biết
            throw new InvalidOperationException("DRIVER_NOT_AVAILABLE"); // đã thuộc công ty khác
        }

        // Auto-reject các app Pending khác của cùng driver (tuỳ chọn)
        _ = _apps.RejectPendingByDriverExceptAsync(app.DriverId.ToString(), appId);
    }

    public async Task RejectApplicationAsync(string companyId, string appId)
    {
        var app = await _apps.GetByAppIdAsync(appId) ?? throw new InvalidOperationException("APPLICATION_NOT_FOUND");
        if (app.CompanyId.ToString() != companyId) throw new InvalidOperationException("FORBIDDEN");

        if (string.Equals(app.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CANNOT_REJECT_APPROVED");

        await _apps.UpdateAppStatusAsync(appId, "Rejected");
        // Không đụng vào driver
    }
}