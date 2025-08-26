using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.Services;
using MyCabs.Domain.Interfaces;
using MyCabs.Api.Common;
using MyCabs.Application.DTOs;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _svc;
    private readonly INotificationRepository _repo;
    public NotificationsController(INotificationService svc, INotificationRepository repo)
    { _svc = svc; _repo = repo; }

    private string CurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    // ✅ Chỉ còn 1 action cho GET /api/Notifications
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool? unreadOnly = null)
    {
        var uid = CurrentUserId();
        var (items, total) = await _repo.FindAsync(uid, page, pageSize, unreadOnly);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(items, page, pageSize, total)));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var uid = CurrentUserId();
        var n = await _svc.GetUnreadCountAsync(uid);
        return Ok(ApiEnvelope.Ok(HttpContext, new { count = n }));
    }

    [HttpPost("mark-read/{id}")]
    public async Task<IActionResult> MarkRead([FromRoute] string id)
    {
        var uid = CurrentUserId();
        var ok = await _svc.MarkReadAsync(uid, id);
        if (!ok) return NotFound(ApiEnvelope.Fail(HttpContext, "NOT_FOUND", "Notification not found or already read", 404));
        return Ok(ApiEnvelope.Ok(HttpContext, new { marked = true }));
    }

    public record MarkBulkReq(string[] Ids);

    [HttpPost("mark-read-bulk")]
    public async Task<IActionResult> MarkReadBulk([FromBody] MarkBulkReq req)
    {
        var uid = CurrentUserId();
        var n = await _svc.MarkReadBulkAsync(uid, req.Ids ?? Array.Empty<string>());
        return Ok(ApiEnvelope.Ok(HttpContext, new { marked = n }));
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var uid = CurrentUserId();
        var n = await _svc.MarkAllReadAsync(uid);
        return Ok(ApiEnvelope.Ok(HttpContext, new { marked = n }));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id)
    {
        var uid = CurrentUserId();
        var ok = await _svc.DeleteAsync(uid, id);
        if (!ok) return NotFound(ApiEnvelope.Fail(HttpContext, "NOT_FOUND", "Notification not found", 404));
        return Ok(ApiEnvelope.Ok(HttpContext, new { deleted = true }));
    }
}
