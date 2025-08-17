using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Common;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DriversController : ControllerBase
{
    private readonly IDriverService _svc;
    public DriversController(IDriverService svc) { _svc = svc; }

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
    public IActionResult MyTransactions()
    {
        var payload = new PagedResult<object>(Array.Empty<object>(), 1, 10, 0);
        return Ok(ApiEnvelope.Ok(HttpContext, payload));
    }
}