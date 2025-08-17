using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Api.Common;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _svc;
    public AuthController(IAuthService svc) { _svc = svc; }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var (ok, err, token) = await _svc.RegisterAsync(dto);
        if (!ok) return Conflict(ApiEnvelope.Fail(HttpContext, "USER_ALREADY_EXISTS", err!, 409));
        return Ok(ApiEnvelope.Ok(HttpContext, new { accessToken = token }));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var (ok, err, token) = await _svc.LoginAsync(dto);
        if (!ok)
        {
            var code = err == "Account deactivated" ? "ACCOUNT_DEACTIVATED" : "INVALID_CREDENTIALS";
            var status = err == "Account deactivated" ? 403 : 401;
            return StatusCode(status, ApiEnvelope.Fail(HttpContext, code, err!, status));
        }
        return Ok(ApiEnvelope.Ok(HttpContext, new { accessToken = token }));
    }
}