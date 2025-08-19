using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Api.Common;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _svc;
    public NotificationsController(INotificationService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] NotificationsQuery q)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var (items, total) = await _svc.GetAsync(uid, q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<NotificationDto>(items, q.Page, q.PageSize, total)));
    }

    [HttpPost("mark-read")] // body: { notificationId: "..." }
    public async Task<IActionResult> MarkRead([FromBody] Dictionary<string, string> body)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        if (!body.TryGetValue("notificationId", out var nid)) return BadRequest(ApiEnvelope.Fail(HttpContext, "VALIDATION_ERROR", "notificationId is required", 400));
        var ok = await _svc.MarkReadAsync(uid, nid);
        return Ok(ApiEnvelope.Ok(HttpContext, new { updated = ok }));
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var cnt = await _svc.MarkAllReadAsync(uid);
        return Ok(ApiEnvelope.Ok(HttpContext, new { updated = cnt }));
    }

    // Dev helper: tự tạo 1 notification cho user hiện tại
    [HttpPost("test")] // body: CreateNotificationDto
    public async Task<IActionResult> Test([FromBody] CreateNotificationDto dto)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        await _svc.PublishAsync(uid, dto);
        return Ok(ApiEnvelope.Ok(HttpContext, new { message = "pushed" }));
    }
}