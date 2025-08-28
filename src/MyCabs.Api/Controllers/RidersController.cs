using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Api.Common;
using MyCabs.Application.Services;
using MyCabs.Infrastructure.Repositories;
using MyCabs.Domain.Entities;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RidersController : ControllerBase
{
    private readonly IRiderService _svc;
    private readonly UserRepository _users;
    private readonly CompanyRepository _companies;

    public RidersController(IRiderService svc, UserRepository users, CompanyRepository companies)
    {
        _svc = svc;
        _users = users;
        _companies = companies;
    }

    private static string? ToIdString(object? oid)
        => oid?.ToString();

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    // --- Search Companies ---
    [AllowAnonymous]
    [HttpGet("companies")] // /api/riders/companies?search=&serviceType=&plan=&sort=&page=&pageSize=
    public async Task<IActionResult> SearchCompanies([FromQuery] RiderCompaniesQuery q)
    {
        var (items, total) = await _svc.SearchCompaniesAsync(q);

        var shaped = items.Select(c => new
        {
            id = c.Id.ToString(),
            ownerUserId = c.OwnerUserId.ToString(),
            name = c.Name,
            description = c.Description,
            address = c.Address,

            // CHANGED: dùng kiểu mạnh thay vì List<object>
            services = ((IEnumerable<CompanyServiceItem>?)c.Services ?? Array.Empty<CompanyServiceItem>())
                .Select(s => new
                {
                    serviceId = s.ServiceId,
                    type = s.Type,
                    title = s.Title,
                    basePrice = s.BasePrice
                })
                .ToList(),

            // giữ nguyên phần membership
            membership = c.Membership == null ? null : new
            {
                plan = (string?)c.Membership.GetType().GetProperty("Plan")?.GetValue(c.Membership),
                billingCycle = (string?)c.Membership.GetType().GetProperty("BillingCycle")?.GetValue(c.Membership),
                expiresAt = (DateTime?)c.Membership.GetType().GetProperty("ExpiresAt")?.GetValue(c.Membership)
            },

            walletId = c.WalletId?.ToString(),
            createdAt = c.CreatedAt,
            updatedAt = c.UpdatedAt
        });

        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(shaped, q.Page, q.PageSize, total)));
    }


    // --- Search Drivers ---
    [AllowAnonymous]
    [HttpGet("drivers")] // /api/riders/drivers?search=&companyId=&sort=&page=&pageSize=
    public async Task<IActionResult> SearchDrivers([FromQuery] RiderDriversQuery q)
    {
        var (items, total) = await _svc.SearchDriversAsync(q);

        // ADDED: load tên user + tên company
        var userIds = items.Select(d => d.UserId.ToString()).Distinct().ToList();
        var companyIds = items.Where(d => d.CompanyId != null).Select(d => d.CompanyId!.ToString()).Distinct().ToList();

        // tải lần lượt cho đơn giản, dữ liệu nhỏ OK; có thể tối ưu batch sau
        var userNameById = new Dictionary<string, string?>();
        foreach (var uid in userIds)
        {
            var u = await _users.GetByIdAsync(uid);
            userNameById[uid] = u?.FullName;
        }

        var userEmailById = new Dictionary<string, string?>();
        foreach (var uid in userIds)
        {
            var u = await _users.GetByIdAsync(uid);
            userEmailById[uid] = u?.Email;
        }

        var companyNameById = new Dictionary<string, string?>();
        foreach (var cid in companyIds)
        {
            if (string.IsNullOrWhiteSpace(cid))
                return BadRequest(ApiEnvelope.Fail(HttpContext, "INVALID_ID", "id is required", 400));
            var c = await _companies.GetByIdAsync(cid);
            companyNameById[cid] = c?.Name;
        }

        // CHANGED: ép Id/ObjectId -> string + thêm fullName, companyName
        var shaped = items.Select(d =>
        {
            var id = d.Id.ToString();
            var userId = d.UserId.ToString();
            var companyId = d.CompanyId?.ToString();
            userNameById.TryGetValue(userId, out var fullName);
            userEmailById.TryGetValue(userId, out var email);
            string? companyName = null;
            if (!string.IsNullOrEmpty(companyId))
                companyNameById.TryGetValue(companyId!, out companyName);

            return new
            {
                id,
                userId,
                fullName,
                companyId,
                companyName,
                email,
                status = d.Status,
                phone = d.Phone,
                bio = d.Bio,
                createdAt = d.CreatedAt,
                updatedAt = d.UpdatedAt
            };
        });

        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(shaped, q.Page, q.PageSize, total)));
    }

    // --- Ratings ---
    [Authorize(Roles = "Rider")]
    [HttpPost("ratings")]
    public async Task<IActionResult> CreateRating([FromBody] CreateRatingDto dto)
    {
        var uid = CurrentUserId();
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
