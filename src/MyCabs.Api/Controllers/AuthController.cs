using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Linq;
using System;

using MyCabs.Api.Common;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserRepository _users;
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;

    public AuthController(
        IAuthService auth,
        IUserRepository users,
        IDriverRepository drivers,
        ICompanyRepository companies)
    {
        _auth = auth;
        _users = users;
        _drivers = drivers;
        _companies = companies;
    }

    private async Task<object?> BuildProfileAsync(string userId)
    {
        var u = await _users.GetByIdAsync(userId);
        if (u == null) return null;

        var roleRaw = u.Role ?? "Rider";
        var isDriver = string.Equals(roleRaw, "Driver", StringComparison.OrdinalIgnoreCase);
        var isCompany = string.Equals(roleRaw, "Company", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(roleRaw, "CompanyOwner", StringComparison.OrdinalIgnoreCase);

        object? driver = null;
        if (isDriver)
        {
            var d = await _drivers.GetByUserIdAsync(userId)
                ?? await _drivers.UpsertMainByUserAsync(userId, u.FullName, null, null);
            driver = new { id = d.Id.ToString(), userId = d.UserId.ToString(), companyId = d.CompanyId?.ToString(), status = d.Status, phone = d.Phone, bio = d.Bio };
        }

        object? company = null;
        if (isCompany)
        {
            // NEW: ensure a company exists for brand-new owners
            var c = await _companies.GetByOwnerUserIdAsync(userId)
                  ?? await _companies.UpsertMainByOwnerAsync(userId, u.FullName ?? u.Email, null, null);
             company = new { id = c.Id.ToString(), ownerUserId = c.OwnerUserId.ToString(), name = c.Name, description = c.Description, address = c.Address };
        }

        // infer role if missing
        var effectiveRole = !string.IsNullOrWhiteSpace(u.Role) ? u.Role! :
                            driver != null ? "Driver" :
                            company != null ? "Company" : "User";

        var user = new { id = u.Id.ToString(), email = u.Email, fullName = u.FullName, role = effectiveRole, emailVerified = u.EmailVerified };
        return new { user, driver, company };
    }




    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var (ok, err) = await _auth.RegisterAsync(dto);
        if (!ok)
            return Conflict(ApiEnvelope.Fail(HttpContext, "USER_ALREADY_EXISTS", err ?? "User already exists", 409));

        return Ok(ApiEnvelope.Ok(HttpContext, new { registered = true }));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var (ok, error, token) = await _auth.LoginAsync(dto);
        if (!ok || string.IsNullOrEmpty(token))
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "INVALID_CREDENTIALS", error ?? "Invalid credentials", 401));

        // Xây profile cho FE
        var emailLower = dto.Email.Trim().ToLowerInvariant();
        var u = await _users.GetByEmailAsync(emailLower);
        if (u == null)
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "INVALID_CREDENTIALS", "Invalid credentials", 401));

        var profile = await BuildProfileAsync(u.Id.ToString());
        return Ok(ApiEnvelope.Ok(HttpContext, new { accessToken = token, profile }));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var p = await BuildProfileAsync(uid);
        return Ok(ApiEnvelope.Ok(HttpContext, p));
    }
}
