using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Common;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Domain.Interfaces;
using MyCabs.Domain.Entities;
using MongoDB.Bson;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DriversController : ControllerBase
{
    private readonly IDriverService _svc;
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;
    private readonly IHiringService _hiring;
    private readonly IWalletRepository _wallets;
    private readonly ITransactionRepository _txs;
    private readonly IInvitationRepository _invites;
    public DriversController(
        IDriverService svc,
        IDriverRepository drivers,
        ICompanyRepository companies,
        IHiringService hiring,
        IWalletRepository wallets,
        ITransactionRepository txs,
        IInvitationRepository invites)
    { _svc = svc; _drivers = drivers; _companies = companies; _hiring = hiring; _wallets = wallets; _txs = txs; _invites = invites; }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    [HttpGet("openings")]
    public async Task<IActionResult> Openings(
        [FromQuery] string? search = null,
        [FromQuery] string? serviceType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var me = await _drivers.GetByUserIdAsync(CurrentUserId());
        var (items, total) = await _companies.FindAsync(page, pageSize, search, plan: null, serviceType: serviceType, sort: null);

        var now = DateTime.UtcNow;
        var list = items.Select(c => new DriverOpeningDto(
            c.Id.ToString(),
            c.Name ?? string.Empty,
            c.Address,
            c.Membership?.Plan ?? "Free",
            c.Membership?.ExpiresAt,
            (c.Services ?? new List<MyCabs.Domain.Entities.CompanyServiceItem>())
                .Select(s => new DriverOpeningServiceDto(s.Type, s.Title, s.BasePrice)),
            // CanApply rules: chưa là nhân viên của company đó, company còn hạn (nếu có), có ít nhất 1 service
            // (me?.CompanyId == null || me.CompanyId != c.Id)
            ((me == null) || me.CompanyId == ObjectId.Empty || me.CompanyId != c.Id)
            && (c.Membership?.ExpiresAt == null || c.Membership!.ExpiresAt > now)
            && (c.Services != null && c.Services.Count > 0)
        ));

        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<DriverOpeningDto>(list, page, pageSize, total)));
    }

    [Authorize(Roles = "Driver")]
    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] DriverApplyDto dto, [FromServices] IHiringService hiring)
    {
        if (string.IsNullOrEmpty(CurrentUserId()))
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "UNAUTHORIZED", "Authentication is required", 401));

        try
        {
            await hiring.ApplyAsync(CurrentUserId(), dto);
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Application submitted" }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "COMPANY_NOT_FOUND")
        {
            return NotFound(ApiEnvelope.Fail(HttpContext, "COMPANY_NOT_FOUND", "Company not found", 404));
        }
        catch (InvalidOperationException ex) when (ex.Message == "APPLICATION_ALREADY_PENDING")
        {
            return Conflict(ApiEnvelope.Fail(HttpContext, "APPLICATION_ALREADY_PENDING", "Application already pending", 409));
        }
    }

    [Authorize(Roles = "Driver")]
    [HttpPost("invitations/{inviteId}/respond")]
    public async Task<IActionResult> Respond(string inviteId, [FromBody] InvitationRespondDto dto)
    {
        if (string.IsNullOrEmpty(CurrentUserId()))
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "UNAUTHORIZED", "Authentication is required", 401));

        try
        {
            await _svc.RespondInvitationAsync(CurrentUserId(), inviteId, dto.Action);
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Invitation updated" }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITATION_NOT_FOUND")
        {
            return NotFound(ApiEnvelope.Fail(HttpContext, "INVITATION_NOT_FOUND", "Invitation not found", 404));
        }
    }

    // Stub: sẽ nối với Transactions/Wallet về sau
    [Authorize(Roles = "Driver")]
    [HttpGet("me/transactions")]
    public async Task<IActionResult> MyTransactions([FromQuery] TransactionsQuery q, [FromServices] IFinanceService finance)
    {
        var d = await _drivers.GetByUserIdAsync(CurrentUserId());
        if (d is null) return NotFound(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_FOUND", "Driver not found", 404));
        var (items, total) = await finance.GetDriverTransactionsAsync(d.Id.ToString(), q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<TransactionDto>(items, q.Page, q.PageSize, total)));
    }

    [Authorize(Roles = "Driver")]
    [HttpGet("me/wallet")]
    public async Task<IActionResult> MyWallet()
    {
        var me = await _drivers.GetByUserIdAsync(CurrentUserId());
        if (me == null) return NotFound(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_FOUND", "Driver not found", 404));


        var w = await _wallets.GetOrCreateAsync("Driver", me.Id.ToString()); // auto-create if missing
        return Ok(ApiEnvelope.Ok(HttpContext, new
        {
            id = w.Id.ToString(),
            ownerType = w.OwnerType,
            ownerId = w.OwnerId.ToString(),
            balance = w.Balance
        }));
    }

    [Authorize(Roles = "Driver")]
    [HttpGet("me/applications")]
    public async Task<IActionResult> MyApplications([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var me = await _drivers.GetByUserIdAsync(CurrentUserId());
        if (me == null) return NotFound(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_FOUND", "Driver not found", 404));


        var w = await _wallets.GetOrCreateAsync("Driver", me.Id.ToString());
        var (items, total) = await _txs.FindByWalletAsync(w.Id.ToString(), page, pageSize);
        var list = items.Select(t => new
        {
            id = t.Id.ToString(),
            type = t.Type,
            status = t.Status,
            amount = t.Amount,
            note = t.Note,
            createdAt = t.CreatedAt
        });
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(list, page, pageSize, total)));
    }

    [Authorize(Roles = "Driver")]
    [HttpGet("me/invitations")]
    public async Task<IActionResult> MyInvitations([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var uid = CurrentUserId();
        var driver = await _drivers.GetByUserIdAsync(uid);
        if (driver == null)
            return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<MyInvitationDto>(Enumerable.Empty<MyInvitationDto>(), page, pageSize, 0)));

        var (items, total) = await _invites.FindByDriverIdAsync(driver.Id.ToString(), page, pageSize);

        // CompanyId là ObjectId (không nullable) => lọc bằng ObjectId.Empty
        var companyIds = items
            .Where(x => x.CompanyId != ObjectId.Empty)
            .Select(x => x.CompanyId.ToString())
            .Distinct()
            .ToArray();

        var companies = await _companies.GetManyByIdsAsync(companyIds);
        var nameById = companies.ToDictionary(c => c.Id.ToString(), c => c.Name);

        var dto = items.Select(x => new MyInvitationDto(
            Id: x.Id.ToString(),
            CompanyId: x.CompanyId != ObjectId.Empty ? x.CompanyId.ToString() : "",
            CompanyName: x.CompanyId != ObjectId.Empty && nameById.TryGetValue(x.CompanyId.ToString(), out var nm) ? nm : null,
            DriverId: x.DriverId != ObjectId.Empty ? x.DriverId.ToString() : "",
            Status: x.Status,
            CreatedAt: x.CreatedAt,
            Note: x.Note
        ));

        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<MyInvitationDto>(dto, page, pageSize, total)));
    }
}