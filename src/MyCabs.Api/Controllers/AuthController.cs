using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Linq;
using System;

using MyCabs.Api.Common;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Domain.Interfaces;
using MyCabs.Domain.Entities;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly IUserRepository _users;
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;
    private readonly IWalletRepository _wallets;

    public AuthController(
        IAuthService auth,
        IUserRepository users,
        IDriverRepository drivers,
        ICompanyRepository companies,
        IWalletRepository wallets)
    {
        _auth = auth;
        _users = users;
        _drivers = drivers;
        _companies = companies;
        _wallets = wallets;
    }

    private async Task<object?> BuildProfileAsync(string userId)
    {
        var u = await _users.GetByIdAsync(userId);
        if (u == null) return null;

        object? driver = null; object? company = null;
        var role = u.Role ?? "Rider";

        if (string.Equals(role, "Driver", StringComparison.OrdinalIgnoreCase))
        {
            var d = await _drivers.GetByUserIdAsync(userId);
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
        }
        else if (string.Equals(role, "Company", StringComparison.OrdinalIgnoreCase))
        {
            // TODO: thay bằng repo chuyên biệt GetByOwnerUserIdAsync nếu có
            var (items, _) = await _companies.FindAsync(1, 100, null, null, null, null); // 100 để tránh miss ở trang sau
            var c = items.FirstOrDefault(x => x.OwnerUserId.ToString() == userId);
            if (c != null)
            {
                // Lấy (hoặc tạo) ví theo owner
                var w = await _wallets.GetOrCreateAsync("Company", c.Id.ToString());
                company = new
                {
                    id = c.Id.ToString(),
                    ownerUserId = c.OwnerUserId.ToString(),
                    name = c.Name,
                    description = c.Description,
                    address = c.Address,
                    services = c.Services?.Select(s => new { serviceId = s.ServiceId, type = s.Type, title = s.Title, basePrice = s.BasePrice }),
                    membership = c.Membership,
                    walletId = w.Id.ToString(),
                    createdAt = c.CreatedAt,
                    updatedAt = c.UpdatedAt
                };
            }
        }

        var user = new
        {
            id = u.Id.ToString(),
            email = u.Email,
            fullName = u.FullName,
            role = role,
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
        var emailLower = dto.Email.Trim().ToLowerInvariant();

        // 1) Tìm user trước
        var u = await _users.GetByEmailAsync(emailLower);
        if (u == null)
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "INVALID_CREDENTIALS", "Invalid credentials", 401));

        // 2) Chặn nếu chưa xác minh email
        if (u.EmailVerified != true)
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "EMAIL_NOT_VERIFIED",
                "Email chưa được xác minh. Vui lòng nhập OTP để xác minh.", 401));
        // (Có thể dùng 403 nếu bạn muốn phân biệt rõ với sai mật khẩu)

        // 3) Kiểm tra mật khẩu & phát token như cũ
        var (ok, error, token) = await _auth.LoginAsync(dto);
        if (!ok || string.IsNullOrEmpty(token))
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "INVALID_CREDENTIALS", error ?? "Invalid credentials", 401));

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
