using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

using MyCabs.Api.Common;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyService _svc;
    private readonly IFinanceService _finance;
    private readonly IHiringService _hiring;
    private readonly ICompanyRepository _companies;

    public CompaniesController(ICompanyService svc, IFinanceService finance, IHiringService hiring, ICompanyRepository companies)
    { _svc = svc; _finance = finance; _hiring = hiring; _companies = companies; }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    public record CompanyServiceItemDto(string? ServiceId, string Type, string Title, decimal basePrice);
    public record MembershipInfoDto(string Plan, string BillingCycle, DateTime? ExpiresAt);
    public record UpdateMyCompanyDto(string? Name, string? Description, string? Address, List<CompanyServiceItemDto>? Services, MembershipInfoDto? Membership);

    private static CompanyServiceItem Map(CompanyServiceItemDto d) => new CompanyServiceItem
    {
        ServiceId = string.IsNullOrWhiteSpace(d.ServiceId) ? MongoDB.Bson.ObjectId.GenerateNewId().ToString() : d.ServiceId,
        Type = d.Type,
        Title = d.Title,
        BasePrice = d.basePrice
    };

    private static object MapCompany(Company c) => new
    {
        id = c.Id.ToString(),
        ownerUserId = c.OwnerUserId.ToString(),
        name = c.Name,
        description = c.Description,
        address = c.Address,
        services = c.Services?.Select(s => new { serviceId = s.ServiceId, type = s.Type, title = s.Title, basePrice = s.BasePrice }),
        membership = c.Membership == null ? null : new { plan = c.Membership.Plan, billingCycle = c.Membership.BillingCycle, expiresAt = c.Membership.ExpiresAt },
        createdAt = c.CreatedAt,
        updatedAt = c.UpdatedAt
    };

    [HttpGet("me")]
    public async Task<IActionResult> GetMine()
    {
        var me = CurrentUserId();
        var c = await _companies.GetByOwnerUserIdAsync(me);
        if (c == null) return NotFound(ApiEnvelope.Fail(HttpContext, "COMPANY_NOT_FOUND", "Company not found", 404));
        return Ok(ApiEnvelope.Ok(HttpContext, MapCompany(c)));
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMine([FromBody] UpdateMyCompanyDto dto)
    {
        var me = CurrentUserId();

        List<CompanyServiceItem>? services = null;
        if (dto.Services != null) services = dto.Services.Select(Map).ToList();

        MembershipInfo? membership = null;
        if (dto.Membership != null)
            membership = new MembershipInfo { Plan = dto.Membership.Plan, BillingCycle = dto.Membership.BillingCycle, ExpiresAt = dto.Membership.ExpiresAt };

        var ok = await _companies.UpdateProfileByOwnerAsync(me, dto.Name, dto.Description, dto.Address, services, membership);
        if (!ok) return NotFound(ApiEnvelope.Fail(HttpContext, "COMPANY_NOT_FOUND", "Company not found", 404));

        var updated = await _companies.GetByOwnerUserIdAsync(me);
        return Ok(ApiEnvelope.Ok(HttpContext, MapCompany(updated!)));
    }

    [HttpGet]
    public async Task<IActionResult> GetCompanies([FromQuery] CompaniesQuery q)
    {
        var (items, total) = await _svc.GetCompaniesAsync(q);
        var page = q.Page <= 0 ? 1 : q.Page;
        var pageSize = q.PageSize <= 0 ? 10 : q.PageSize;
        var payload = new PagedResult<CompanyDto>(items, page, pageSize, total);
        return Ok(ApiEnvelope.Ok(HttpContext, payload));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var dto = await _svc.GetByIdAsync(id);
        if (dto is null) return NotFound(ApiEnvelope.Fail(HttpContext, "COMPANY_NOT_FOUND", "Company not found", 404));
        return Ok(ApiEnvelope.Ok(HttpContext, dto));
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/services")]
    public async Task<IActionResult> AddService(string id, [FromBody] AddCompanyServiceDto dto)
    {
        await _svc.AddServiceAsync(id, dto);
        return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Service added" }));
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpGet("{id}/wallet")]
    public async Task<IActionResult> GetWallet(string id)
 => Ok(ApiEnvelope.Ok(HttpContext, await _finance.GetCompanyWalletAsync(id)));

    [Authorize(Roles = "Company,Admin")]
    [HttpGet("{id}/transactions")]
    public async Task<IActionResult> GetTransactions(string id, [FromQuery] TransactionsQuery q)
    { var (items, total) = await _finance.GetCompanyTransactionsAsync(id, q); return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<TransactionDto>(items, q.Page, q.PageSize, total))); }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/wallet/topup")]
    public async Task<IActionResult> TopUp(string id, [FromBody] TopUpDto dto)
    { await _finance.TopUpCompanyAsync(id, dto); return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Topup completed" })); }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/pay-salary")]
    public async Task<IActionResult> PaySalary(string id, [FromBody] PaySalaryDto dto)
    { var (ok, err) = await _finance.PaySalaryAsync(id, dto); if (!ok && err == "INSUFFICIENT_FUNDS") return BadRequest(ApiEnvelope.Fail(HttpContext, "INSUFFICIENT_FUNDS", "Company wallet has insufficient funds", 400)); return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Salary paid" })); }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/membership/pay")]
    public async Task<IActionResult> PayMembership(string id, [FromBody] PayMembershipDto dto)
    { var (ok, err) = await _finance.PayMembershipAsync(id, dto); if (!ok && err == "INSUFFICIENT_FUNDS") return BadRequest(ApiEnvelope.Fail(HttpContext, "INSUFFICIENT_FUNDS", "Company wallet has insufficient funds", 400)); return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Membership updated" })); }


    [Authorize(Roles = "Company,Admin")]
    [HttpGet("{id}/applications")]
    public async Task<IActionResult> Applications(
    [FromRoute] string id,
    [FromServices] IApplicationsQueryService svc,   // <-- đưa lên trước
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20)
    {
        var data = await svc.GetByCompanyAsync(id, page, pageSize);
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/applications/{appId}/approve")]
    public async Task<IActionResult> ApproveApp(string id, string appId)
    {
        try
        {
            await _hiring.ApproveApplicationAsync(id, appId);
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Approved" }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "APPLICATION_NOT_FOUND")
        { return NotFound(ApiEnvelope.Fail(HttpContext, "APPLICATION_NOT_FOUND", "Application not found", 404)); }
        catch (InvalidOperationException ex) when (ex.Message == "FORBIDDEN")
        { return Forbid(); }
        catch (InvalidOperationException ex) when (ex.Message == "DRIVER_NOT_AVAILABLE")
        { return Conflict(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_AVAILABLE", "Driver already hired by another company", 409)); }
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/applications/{appId}/reject")]
    public async Task<IActionResult> RejectApp(string id, string appId)
    {
        try
        {
            await _hiring.RejectApplicationAsync(id, appId);
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Rejected" }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "APPLICATION_NOT_FOUND")
        { return NotFound(ApiEnvelope.Fail(HttpContext, "APPLICATION_NOT_FOUND", "Application not found", 404)); }
        catch (InvalidOperationException ex) when (ex.Message == "FORBIDDEN")
        { return Forbid(); }
        catch (InvalidOperationException ex) when (ex.Message == "CANNOT_REJECT_APPROVED")
        { return BadRequest(ApiEnvelope.Fail(HttpContext, "CANNOT_REJECT_APPROVED", "Already approved", 400)); }
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/invitations")]
    public async Task<IActionResult> Invite(string id, [FromBody] InviteDriverDto dto)
    {
        try { await _hiring.InviteDriverAsync(id, dto); return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Invitation sent" })); }
        catch (InvalidOperationException ex) when (ex.Message == "DRIVER_NOT_FOUND") { return NotFound(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_FOUND", "Driver not found", 404)); }
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpGet("{id}/invitations")]
    public async Task<IActionResult> GetInvitations(string id, [FromQuery] InvitationsQuery q)
    { var (items, total) = await _hiring.GetCompanyInvitationsAsync(id, q); return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<InvitationDto>(items, q.Page, q.PageSize, total))); }
}