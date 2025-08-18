using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Api.Common;
using MyCabs.Application.Services;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RidersController : ControllerBase
{
    private readonly IRiderService _svc;
    public RidersController(IRiderService svc) { _svc = svc; }

    // --- Search ---
    [AllowAnonymous]
    [HttpGet("companies")] // /api/riders/companies?search=&serviceType=&plan=&sort=&page=&pageSize=
    public async Task<IActionResult> SearchCompanies([FromQuery] RiderCompaniesQuery q)
    {
        var (items, total) = await _svc.SearchCompaniesAsync(q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(items, q.Page, q.PageSize, total)));
    }

    [AllowAnonymous]
    [HttpGet("drivers")] // /api/riders/drivers?search=&companyId=&sort=&page=&pageSize=
    public async Task<IActionResult> SearchDrivers([FromQuery] RiderDriversQuery q)
    {
        var (items, total) = await _svc.SearchDriversAsync(q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(items, q.Page, q.PageSize, total)));
    }

    // --- Ratings ---
    [Authorize(Roles = "Rider")]
    [HttpPost("ratings")] // body: CreateRatingDto
    public async Task<IActionResult> CreateRating([FromBody] CreateRatingDto dto)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        await _svc.CreateRatingAsync(uid, dto);
        return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Rating submitted" }));
    }

    [AllowAnonymous]
    [HttpGet("ratings")] // ?targetType=Company&targetId=...
    public async Task<IActionResult> GetRatings([FromQuery] RatingsQuery q)
    {
        var (items, total) = await _svc.GetRatingsAsync(q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<RatingDto>(items, q.Page, q.PageSize, total)));
    }

    [AllowAnonymous]
    [HttpGet("ratings/summary")] // ?targetType=&targetId=
    public async Task<IActionResult> GetRatingSummary([FromQuery] string targetType, [FromQuery] string targetId)
    {
        var s = await _svc.GetRatingSummaryAsync(targetType, targetId);
        return Ok(ApiEnvelope.Ok(HttpContext, s));
    }
}