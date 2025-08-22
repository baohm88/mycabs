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
public class ChatController : ControllerBase
{
    private readonly IChatService _svc;
    public ChatController(IChatService svc) { _svc = svc; }

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    [HttpPost("threads")]
    public async Task<IActionResult> Start([FromBody] StartChatDto dto)
    {
        var me = CurrentUserId();
        var t = await _svc.StartOrGetThreadAsync(me, dto.PeerUserId);
        return Ok(ApiEnvelope.Ok(HttpContext, t));
    }

    [HttpGet("threads")]
    public async Task<IActionResult> Threads([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var me = CurrentUserId();
        var (items, total) = await _svc.GetThreadsAsync(me, new ThreadsQuery(page, pageSize));
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<ThreadDto>(items, page, pageSize, total)));
    }

    [HttpGet("threads/{threadId}/messages")]
    public async Task<IActionResult> Messages([FromRoute] string threadId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var me = CurrentUserId();
        var (items, total) = await _svc.GetMessagesAsync(me, threadId, new MessagesQuery(page, pageSize));
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<MessageDto>(items, page, pageSize, total)));
    }

    public record SendReq(string Content);

    [HttpPost("threads/{threadId}/messages")]
    public async Task<IActionResult> Send([FromRoute] string threadId, [FromBody] SendReq req)
    {
        var me = CurrentUserId();
        var m = await _svc.SendMessageAsync(me, threadId, req.Content);
        return Ok(ApiEnvelope.Ok(HttpContext, m));
    }

    [HttpPost("threads/{threadId}/read")]
    public async Task<IActionResult> MarkRead([FromRoute] string threadId)
    {
        var me = CurrentUserId();
        var n = await _svc.MarkThreadReadAsync(me, threadId);
        return Ok(ApiEnvelope.Ok(HttpContext, new { marked = n }));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount()
    {
        var me = CurrentUserId();
        var n = await _svc.GetTotalUnreadAsync(me);
        return Ok(ApiEnvelope.Ok(HttpContext, new { count = n }));
    }
}