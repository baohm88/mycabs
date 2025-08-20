using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Api.Common;


namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class OtpController : ControllerBase
{
    private readonly IEmailOtpService _svc;
    public OtpController(IEmailOtpService svc) { _svc = svc; }

    [HttpPost("request")] // body: { email, purpose }
    public async Task<IActionResult> RequestOtp([FromBody] RequestEmailOtpDto dto)
    {
        try
        {
            await _svc.RequestAsync(dto);
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "OTP sent" }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "EMAIL_NOT_FOUND")
        {
            // tránh lộ email tồn tại: vẫn trả OK
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "OTP sent" }));
        }
    }

    [HttpPost("verify")] // body: { email, purpose, code }
    public async Task<IActionResult> Verify([FromBody] VerifyEmailOtpDto dto)
    {
        var ok = await _svc.VerifyAsync(dto);
        if (!ok) return BadRequest(ApiEnvelope.Fail(HttpContext, "OTP_INVALID", "OTP invalid or expired", 400));
        return Ok(ApiEnvelope.Ok(HttpContext, new { verified = true }));
    }

    [HttpPost("reset-password")] // body: { email, code, newPassword }
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordWithOtpDto dto)
    {
        var ok = await _svc.ResetPasswordWithOtpAsync(dto);
        if (!ok) return BadRequest(ApiEnvelope.Fail(HttpContext, "OTP_INVALID", "OTP invalid/expired or user not found", 400));
        return Ok(ApiEnvelope.Ok(HttpContext, new { reset = true }));
    }
}