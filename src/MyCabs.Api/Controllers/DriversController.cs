using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Common;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DriversController : ControllerBase
{
    private readonly IDriverService _svc;
    private readonly IDriverRepository _drivers;
    public DriversController(IDriverService svc, IDriverRepository drivers) { _svc = svc; _drivers = drivers; }


    [HttpGet("openings")]
    public async Task<IActionResult> GetOpenings([FromQuery] CompaniesQuery q)
    {
        var (items, total) = await _svc.GetOpeningsAsync(q);
        var payload = new PagedResult<CompanyDto>(items, q.Page <= 0 ? 1 : q.Page, q.PageSize <= 0 ? 10 : q.PageSize, total);
        return Ok(ApiEnvelope.Ok(HttpContext, payload));
    }

    [Authorize(Roles = "Driver")]
    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] DriverApplyDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) // sometimes mapped
                     ?? User.FindFirstValue("sub")                     // JwtRegisteredClaimNames.Sub
                     ?? string.Empty;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "UNAUTHORIZED", "Authentication is required", 401));

        try
        {
            await _svc.ApplyAsync(userId, dto);
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
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "UNAUTHORIZED", "Authentication is required", 401));

        try
        {
            await _svc.RespondInvitationAsync(userId, inviteId, dto.Action);
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
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var d = await _drivers.GetByUserIdAsync(uid);
        if (d is null) return NotFound(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_FOUND", "Driver not found", 404));
        var (items, total) = await finance.GetDriverTransactionsAsync(d.Id.ToString(), q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<TransactionDto>(items, q.Page, q.PageSize, total)));
    }

    [Authorize(Roles = "Driver")]
    [HttpGet("me/wallet")]
    public async Task<IActionResult> MyWallet([FromServices] IFinanceService finance)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var d = await _drivers.GetByUserIdAsync(uid);
        if (d is null) return NotFound(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_FOUND", "Driver not found", 404));
        return Ok(ApiEnvelope.Ok(HttpContext, await finance.GetDriverWalletAsync(d.Id.ToString())));
    }

    [Authorize(Roles = "Driver")]
    [HttpGet("me/applications")]
    public async Task<IActionResult> MyApplications([FromServices] IHiringService hiring, [FromQuery] ApplicationsQuery q)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        try { var (items, total) = await hiring.GetMyApplicationsAsync(uid, q); return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<ApplicationDto>(items, q.Page, q.PageSize, total))); }
        catch (InvalidOperationException ex) when (ex.Message == "DRIVER_NOT_FOUND") { return NotFound(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_FOUND", "Driver not found", 404)); }
    }

    [Authorize(Roles = "Driver")]
    [HttpGet("me/invitations")]
    public async Task<IActionResult> MyInvitations([FromServices] IHiringService hiring, [FromQuery] InvitationsQuery q)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        try { var (items, total) = await hiring.GetMyInvitationsAsync(uid, q); return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<InvitationDto>(items, q.Page, q.PageSize, total))); }
        catch (InvalidOperationException ex) when (ex.Message == "DRIVER_NOT_FOUND") { return NotFound(ApiEnvelope.Fail(HttpContext, "DRIVER_NOT_FOUND", "Driver not found", 404)); }
    }
}