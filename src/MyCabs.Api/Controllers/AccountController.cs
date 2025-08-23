using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Common;
using MyCabs.Domain.Interfaces;
using System.Security.Claims;

namespace MyCabs.Api.Controllers;

public record UpdateAccountDto(
    string? FullName,
    // driver
    string? DriverPhone,
    string? DriverBio,
    // company
    string? CompanyName,
    string? CompanyDescription,
    string? CompanyAddress
);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly IUserRepository _users;
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;

    public AccountController(IUserRepository users, IDriverRepository drivers, ICompanyRepository companies)
    { _users = users; _drivers = drivers; _companies = companies; }

    private string CurrentUserId()
        => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateAccountDto dto)
    {
        var uid = CurrentUserId();
        var u = await _users.GetByIdAsync(uid);
        if (u == null) return NotFound(ApiEnvelope.Fail(HttpContext, "USER_NOT_FOUND", "User not found", 404));

        // update user name
        if (!string.IsNullOrWhiteSpace(dto.FullName))
            await _users.UpdateFullNameAsync(uid, dto.FullName!.Trim());

        var role = u.Role ?? "User";
        if (string.Equals(role, "Driver", StringComparison.OrdinalIgnoreCase))
        {
            await _drivers.UpdateProfileAsync(uid, dto.DriverPhone, dto.DriverBio);
        }
        else if (string.Equals(role, "Company", StringComparison.OrdinalIgnoreCase))
        {
            await _companies.UpdateMainAsync(uid, dto.CompanyName, dto.CompanyDescription, dto.CompanyAddress);
        }

        return Ok(ApiEnvelope.Ok(HttpContext, new { updated = true }));
    }
}