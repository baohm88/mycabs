// using Microsoft.AspNetCore.Authorization;
// using Microsoft.AspNetCore.Mvc;
// using System.Security.Claims;
// using System.Linq;

// using Microsoft.AspNetCore.Mvc;
// using MyCabs.Application.DTOs;
// using MyCabs.Application.Services;
// using MyCabs.Api.Common;

// namespace MyCabs.Api.Controllers;

// [ApiController]
// [Route("api/[controller]")]
// public class AuthController : ControllerBase
// {
//     private readonly IAuthService _svc;
//     private readonly IAuthService _auth;
//     private readonly IUserRepository _users;
//     private readonly IDriverRepository _drivers;
//     private readonly ICompanyRepository _companies;

//     public AuthController(IAuthService auth, IUserRepository users, IDriverRepository drivers, ICompanyRepository companies, IAuthService svc)
//     { _auth = auth; _users = users; _drivers = drivers; _companies = companies; _svc = svc; }

//     // helper to shape profile
//     private async Task<object?> BuildProfileAsync(string userId)
//     {
//         var u = await _users.GetByIdAsync(userId);
//         if (u == null) return null;
//         var role = u.Role ?? "User";
//         object? driver = null; object? company = null;
//         if (string.Equals(role, "Driver", StringComparison.OrdinalIgnoreCase))
//         {
//             var d = await _drivers.GetByUserIdAsync(userId);
//             if (d != null) driver = new { id = d.Id.ToString(), userId = d.UserId.ToString(), companyId = d.CompanyId?.ToString(), status = d.Status, phone = d.Phone, bio = d.Bio };
//         }
//         else if (string.Equals(role, "Company", StringComparison.OrdinalIgnoreCase))
//         {
//             // company where current user is owner
//             var (items, _) = await _companies.FindAsync(1, 1, null, null, null, null);
//             var c = items.FirstOrDefault(x => x.OwnerUserId.ToString() == userId);
//             if (c != null) company = new { id = c.Id.ToString(), ownerUserId = c.OwnerUserId.ToString(), name = c.Name, description = c.Description, address = c.Address };
//         }
//         var user = new { id = u.Id.ToString(), email = u.Email, fullName = u.FullName, role = role, emailVerified = u.EmailVerified };
//         return new { user, driver, company };
//     }



//     [HttpPost("register")]
//     public async Task<IActionResult> Register([FromBody] RegisterDto dto)
//     {
//         var (ok, err) = await _svc.RegisterAsync(dto);
//         if (!ok) return Conflict(ApiEnvelope.Fail(HttpContext, "USER_ALREADY_EXISTS", err!, 409));
//         return Ok(ApiEnvelope.Ok(HttpContext));
//     }

//     [HttpPost("login")]
//     public async Task<IActionResult> Login([FromBody] LoginDto dto)
//     {
//         var (ok, err, token) = await _svc.LoginAsync(dto);
//         if (!ok)
//         {
//             var code = err == "Account deactivated" ? "ACCOUNT_DEACTIVATED" : "INVALID_CREDENTIALS";
//             var status = err == "Account deactivated" ? 403 : 401;
//             return StatusCode(status, ApiEnvelope.Fail(HttpContext, code, err!, status));
//         }
//         return Ok(ApiEnvelope.Ok(HttpContext, new { accessToken = token }));
//     }

//     [HttpPost("login")]
//     [AllowAnonymous]
//     public async Task<IActionResult> Login([FromBody] LoginDto dto)
//     {
//         var (ok, error, token) = await _auth.LoginAsync(dto);
//         if (!ok || string.IsNullOrEmpty(token))
//             return Unauthorized(ApiEnvelope.Fail(HttpContext, "INVALID_CREDENTIALS", error ?? "Invalid credentials", 401));

//         // find user by emailLower and build profile
//         var emailLower = dto.Email.Trim().ToLowerInvariant();
//         var u = await _users.GetByEmailAsync(emailLower);
//         if (u == null)
//             return Unauthorized(ApiEnvelope.Fail(HttpContext, "INVALID_CREDENTIALS", "Invalid credentials", 401));

//         var profile = await BuildProfileAsync(u.Id.ToString());
//         return Ok(ApiEnvelope.Ok(HttpContext, new { accessToken = token, profile }));
//     }

//     [Authorize]
//     [HttpGet("me")]
//     public async Task<IActionResult> Me()
//     {
//         var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
//         var p = await BuildProfileAsync(uid);
//         return Ok(ApiEnvelope.Ok(HttpContext, p));
//     }
// }

// src/MyCabs.Api/Controllers/AuthController.cs  // CHANGED: hợp nhất Login, thêm /me + using cần thiết
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Linq;

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

    // Helper: build payload profile cho FE
    // private async Task<object?> BuildProfileAsync(string userId)
    // {
    //     var u = await _users.GetByIdAsync(userId);
    //     if (u == null) return null;

    //     var role = u.Role ?? "User";
    //     object? driver = null;
    //     object? company = null;

    //     if (string.Equals(role, "Driver", StringComparison.OrdinalIgnoreCase))
    //     {
    //         var d = await _drivers.GetByUserIdAsync(userId);
    //         if (d != null)
    //             driver = new
    //             {
    //                 id = d.Id.ToString(),
    //                 userId = d.UserId.ToString(),
    //                 companyId = d.CompanyId?.ToString(),
    //                 status = d.Status,
    //                 phone = d.Phone,
    //                 bio = d.Bio
    //             };
    //     }
    //     else if (string.Equals(role, "Company", StringComparison.OrdinalIgnoreCase))
    //     {
    //         var c = await _companies.GetByOwnerUserIdAsync(userId);
    //         if (c != null)
    //             company = new
    //             {
    //                 id = c.Id.ToString(),
    //                 ownerUserId = c.OwnerUserId.ToString(),
    //                 name = c.Name,
    //                 description = c.Description,
    //                 address = c.Address
    //             };
    //     }

    //     var user = new
    //     {
    //         id = u.Id.ToString(),
    //         email = u.Email,
    //         fullName = u.FullName,
    //         role,
    //         emailVerified = u.EmailVerified
    //     };

    //     return new { user, driver, company };
    // }

    // helper to shape profile
    private async Task<object?> BuildProfileAsync(string userId)
    {
        var u = await _users.GetByIdAsync(userId);
        if (u == null) return null;

        // ALWAYS try to load related docs, regardless of role string
        var d = await _drivers.GetByUserIdAsync(userId);
        var c = await _companies.GetByOwnerUserIdAsync(userId);

        object? driver = null;
        if (d != null)
            driver = new
            {
                id = d.Id.ToString(),
                userId = d.UserId.ToString(),
                companyId = d.CompanyId?.ToString(),
                status = d.Status,
                phone = d.Phone,
                bio = d.Bio
            };

        object? company = null;
        if (c != null)
            company = new
            {
                id = c.Id.ToString(),
                ownerUserId = c.OwnerUserId.ToString(),
                name = c.Name,
                description = c.Description,
                address = c.Address
            };

        // If Role is missing, infer from available docs; otherwise keep original
        var effectiveRole =
            !string.IsNullOrWhiteSpace(u.Role) ? u.Role! :
            driver != null ? "Driver" :
            company != null ? "Company" : "User";

        var user = new
        {
            id = u.Id.ToString(),
            email = u.Email,
            fullName = u.FullName,
            role = effectiveRole,
            emailVerified = u.EmailVerified
        };

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
