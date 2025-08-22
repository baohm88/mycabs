# MyCabs – Realtime 1–1 Chat (SignalR groups)

> Chat 1–1 realtime, tái sử dụng **NotificationsHub** (chung 1 kết nối SignalR). Hỗ trợ: tạo/get thread, gửi message, join/leave group theo threadId, đánh dấu đã đọc, đếm unread tổng & theo thread, typing indicator.

---

## 1) Domain – Entities & Interfaces

**Path:** `src/MyCabs.Domain/Entities/ChatThread.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class ChatThread
{
    [BsonId] public ObjectId Id { get; set; }

    // Key unique cho 2 user theo thứ tự tăng dần (ex: "<a>_<b>")
    [BsonElement("key")] public string Key { get; set; } = string.Empty;

    [BsonElement("users")] public ObjectId[] Users { get; set; } = Array.Empty<ObjectId>(); // length 2

    [BsonElement("lastMessage")] public string? LastMessage { get; set; }
    [BsonElement("lastMessageAt")] public DateTime? LastMessageAt { get; set; }

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**Path:** `src/MyCabs.Domain/Entities/ChatMessage.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class ChatMessage
{
    [BsonId] public ObjectId Id { get; set; }

    [BsonElement("threadId")] public ObjectId ThreadId { get; set; }
    [BsonElement("senderUserId")] public ObjectId SenderUserId { get; set; }
    [BsonElement("recipientUserId")] public ObjectId RecipientUserId { get; set; }

    [BsonElement("content")] public string Content { get; set; } = string.Empty;

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("readAt")] public DateTime? ReadAt { get; set; }
}
```

**Path:** `src/MyCabs.Domain/Interfaces/IChatRepository.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IChatRepository
{
    Task<ChatThread> GetOrCreateThreadAsync(string userA, string userB);
    Task<ChatThread?> GetThreadByIdAsync(string threadId);
    Task<(IEnumerable<ChatThread> Items, long Total)> ListThreadsForUserAsync(string userId, int page, int pageSize);

    Task<ChatMessage> AddMessageAsync(ChatMessage msg);
    Task<(IEnumerable<ChatMessage> Items, long Total)> ListMessagesAsync(string threadId, int page, int pageSize);

    Task<long> MarkThreadReadAsync(string userId, string threadId);
    Task<long> CountUnreadForUserAsync(string userId);
    Task<long> CountUnreadInThreadAsync(string userId, string threadId);

    Task EnsureIndexesAsync();
}
```

---

## 2) Infrastructure – Mongo Implementation

**Path:** `src/MyCabs.Infrastructure/Repositories/ChatRepository.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class ChatRepository : IChatRepository, IIndexInitializer
{
    private readonly IMongoCollection<ChatThread> _threads;
    private readonly IMongoCollection<ChatMessage> _messages;

    public ChatRepository(IMongoContext ctx)
    {
        _threads = ctx.GetCollection<ChatThread>("chat_threads");
        _messages = ctx.GetCollection<ChatMessage>("chat_messages");
    }

    private static (ObjectId a, ObjectId b, string key) NormalizePair(string userA, string userB)
    {
        var oa = ObjectId.Parse(userA);
        var ob = ObjectId.Parse(userB);
        var (min, max) = oa.CompareTo(ob) <= 0 ? (oa, ob) : (ob, oa);
        return (min, max, $"{min}_{max}");
    }

    public async Task<ChatThread> GetOrCreateThreadAsync(string userA, string userB)
    {
        var (a, b, key) = NormalizePair(userA, userB);
        var exist = await _threads.Find(x => x.Key == key).FirstOrDefaultAsync();
        if (exist != null) return exist;

        var t = new ChatThread
        {
            Id = ObjectId.GenerateNewId(),
            Key = key,
            Users = new[] { a, b },
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _threads.InsertOneAsync(t);
        return t;
    }

    public Task<ChatThread?> GetThreadByIdAsync(string threadId)
    {
        if (!ObjectId.TryParse(threadId, out var oid)) return Task.FromResult<ChatThread?>(null);
        return _threads.Find(x => x.Id == oid).FirstOrDefaultAsync();
    }

    public async Task<(IEnumerable<ChatThread> Items, long Total)> ListThreadsForUserAsync(string userId, int page, int pageSize)
    {
        var uid = ObjectId.Parse(userId);
        var f = Builders<ChatThread>.Filter.AnyEq(x => x.Users, uid);
        var total = await _threads.CountDocumentsAsync(f);
        var items = await _threads.Find(f)
            .SortByDescending(x => x.LastMessageAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
        return (items, total);
    }

    public async Task<ChatMessage> AddMessageAsync(ChatMessage msg)
    {
        await _messages.InsertOneAsync(msg);
        var upd = Builders<ChatThread>.Update
            .Set(x => x.LastMessage, msg.Content)
            .Set(x => x.LastMessageAt, msg.CreatedAt)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _threads.UpdateOneAsync(x => x.Id == msg.ThreadId, upd);
        return msg;
    }

    public async Task<(IEnumerable<ChatMessage> Items, long Total)> ListMessagesAsync(string threadId, int page, int pageSize)
    {
        var tid = ObjectId.Parse(threadId);
        var f = Builders<ChatMessage>.Filter.Eq(x => x.ThreadId, tid);
        var total = await _messages.CountDocumentsAsync(f);
        var items = await _messages.Find(f)
            .SortBy(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();
        return (items, total);
    }

    public async Task<long> MarkThreadReadAsync(string userId, string threadId)
    {
        var uid = ObjectId.Parse(userId);
        var tid = ObjectId.Parse(threadId);
        var f = Builders<ChatMessage>.Filter.Eq(x => x.ThreadId, tid)
              & Builders<ChatMessage>.Filter.Eq(x => x.RecipientUserId, uid)
              & Builders<ChatMessage>.Filter.Eq(x => x.ReadAt, null);
        var upd = Builders<ChatMessage>.Update.Set(x => x.ReadAt, DateTime.UtcNow);
        var res = await _messages.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }

    public Task<long> CountUnreadForUserAsync(string userId)
    {
        var uid = ObjectId.Parse(userId);
        var f = Builders<ChatMessage>.Filter.Eq(x => x.RecipientUserId, uid)
              & Builders<ChatMessage>.Filter.Eq(x => x.ReadAt, null);
        return _messages.CountDocumentsAsync(f);
    }

    public Task<long> CountUnreadInThreadAsync(string userId, string threadId)
    {
        var uid = ObjectId.Parse(userId);
        var tid = ObjectId.Parse(threadId);
        var f = Builders<ChatMessage>.Filter.Eq(x => x.ThreadId, tid)
              & Builders<ChatMessage>.Filter.Eq(x => x.RecipientUserId, uid)
              & Builders<ChatMessage>.Filter.Eq(x => x.ReadAt, null);
        return _messages.CountDocumentsAsync(f);
    }

    public async Task EnsureIndexesAsync()
    {
        var ixThreads = new List<CreateIndexModel<ChatThread>>
        {
            new(CreateIndexKeys<ChatThread>.Ascending(x => x.Key), new CreateIndexOptions { Unique = true }),
            new(CreateIndexKeys<ChatThread>.Ascending(x => x.Users).Descending(x => x.LastMessageAt))
        };
        await _threads.Indexes.CreateManyAsync(ixThreads);

        var ixMsgs = new List<CreateIndexModel<ChatMessage>>
        {
            new(CreateIndexKeys<ChatMessage>.Ascending(x => x.ThreadId).Ascending(x => x.CreatedAt)),
            new(CreateIndexKeys<ChatMessage>.Ascending(x => x.RecipientUserId).Ascending(x => x.ReadAt))
        };
        await _messages.Indexes.CreateManyAsync(ixMsgs);
    }
}
```

---

## 3) Application – DTOs & Service

**Path:** `src/MyCabs.Application/DTOs/ChatDtos.cs`

```csharp
namespace MyCabs.Application.DTOs;

public record StartChatDto(string PeerUserId);
public record SendChatMessageDto(string Content);
public record ThreadsQuery(int Page = 1, int PageSize = 20);
public record MessagesQuery(int Page = 1, int PageSize = 50);

public record ThreadDto(
    string Id,
    string[] UserIds,
    string PeerUserId,
    string? LastMessage,
    DateTime? LastMessageAt,
    long UnreadCount
);

public record MessageDto(
    string Id,
    string ThreadId,
    string SenderUserId,
    string RecipientUserId,
    string Content,
    DateTime CreatedAt,
    DateTime? ReadAt
);
```

**Path:** `src/MyCabs.Application/Realtime/IChatPusher.cs`

```csharp
namespace MyCabs.Application.Realtime;

public interface IChatPusher
{
    Task SendToThreadAsync(string threadId, string eventName, object payload);
    Task SendToUserAsync(string userId, string eventName, object payload);
}
```

**Path:** `src/MyCabs.Application/Services/ChatService.cs`

```csharp
using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Application.Realtime;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IChatService
{
    Task<ThreadDto> StartOrGetThreadAsync(string currentUserId, string peerUserId);
    Task<(IEnumerable<ThreadDto> Items,long Total)> GetThreadsAsync(string currentUserId, ThreadsQuery q);
    Task<(IEnumerable<MessageDto> Items,long Total)> GetMessagesAsync(string currentUserId, string threadId, MessagesQuery q);
    Task<MessageDto> SendMessageAsync(string currentUserId, string threadId, string content);
    Task<long> MarkThreadReadAsync(string currentUserId, string threadId);
    Task<long> GetTotalUnreadAsync(string currentUserId);
}

public class ChatService : IChatService
{
    private readonly IChatRepository _repo;
    private readonly IChatPusher _rt;

    public ChatService(IChatRepository repo, IChatPusher rt)
    { _repo = repo; _rt = rt; }

    public async Task<ThreadDto> StartOrGetThreadAsync(string currentUserId, string peerUserId)
    {
        var t = await _repo.GetOrCreateThreadAsync(currentUserId, peerUserId);
        var peer = t.Users.Select(u => u.ToString()).First(id => id != currentUserId);
        var unread = await _repo.CountUnreadInThreadAsync(currentUserId, t.Id.ToString());
        return new ThreadDto(t.Id.ToString(), t.Users.Select(x => x.ToString()).ToArray(), peer, t.LastMessage, t.LastMessageAt, unread);
    }

    public async Task<(IEnumerable<ThreadDto> Items, long Total)> GetThreadsAsync(string currentUserId, ThreadsQuery q)
    {
        var (items,total) = await _repo.ListThreadsForUserAsync(currentUserId, q.Page, q.PageSize);
        var list = new List<ThreadDto>();
        foreach (var t in items)
        {
            var peer = t.Users.Select(u => u.ToString()).First(id => id != currentUserId);
            var unread = await _repo.CountUnreadInThreadAsync(currentUserId, t.Id.ToString());
            list.Add(new ThreadDto(t.Id.ToString(), t.Users.Select(x => x.ToString()).ToArray(), peer, t.LastMessage, t.LastMessageAt, unread));
        }
        return (list, total);
    }

    public async Task<(IEnumerable<MessageDto> Items, long Total)> GetMessagesAsync(string currentUserId, string threadId, MessagesQuery q)
    {
        var (items,total) = await _repo.ListMessagesAsync(threadId, q.Page, q.PageSize);
        var list = items.Select(m => new MessageDto(
            m.Id.ToString(), m.ThreadId.ToString(), m.SenderUserId.ToString(), m.RecipientUserId.ToString(), m.Content, m.CreatedAt, m.ReadAt
        ));
        return (list, total);
    }

    public async Task<MessageDto> SendMessageAsync(string currentUserId, string threadId, string content)
    {
        if (!ObjectId.TryParse(threadId, out var tid)) throw new ArgumentException("Invalid threadId");
        var t = await _repo.GetThreadByIdAsync(threadId) ?? throw new InvalidOperationException("THREAD_NOT_FOUND");
        var peer = t.Users.Select(u => u.ToString()).First(id => id != currentUserId);

        var msg = new ChatMessage
        {
            Id = ObjectId.GenerateNewId(),
            ThreadId = tid,
            SenderUserId = ObjectId.Parse(currentUserId),
            RecipientUserId = ObjectId.Parse(peer),
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
        msg = await _repo.AddMessageAsync(msg);

        var dto = new MessageDto(msg.Id.ToString(), threadId, currentUserId, peer, msg.Content, msg.CreatedAt, msg.ReadAt);
        await _rt.SendToThreadAsync(threadId, "chat.message", dto);

        // cập nhật badge unread cho người nhận
        var totalUnread = await _repo.CountUnreadForUserAsync(peer);
        await _rt.SendToUserAsync(peer, "chat.unread_total", new { count = totalUnread });
        var threadUnread = await _repo.CountUnreadInThreadAsync(peer, threadId);
        await _rt.SendToUserAsync(peer, "chat.thread_unread", new { threadId, count = threadUnread });

        return dto;
    }

    public async Task<long> MarkThreadReadAsync(string currentUserId, string threadId)
    {
        var n = await _repo.MarkThreadReadAsync(currentUserId, threadId);
        var totalUnread = await _repo.CountUnreadForUserAsync(currentUserId);
        await _rt.SendToUserAsync(currentUserId, "chat.unread_total", new { count = totalUnread });
        var threadUnread = await _repo.CountUnreadInThreadAsync(currentUserId, threadId);
        await _rt.SendToUserAsync(currentUserId, "chat.thread_unread", new { threadId, count = threadUnread });
        return n;
    }

    public Task<long> GetTotalUnreadAsync(string currentUserId)
        => _repo.CountUnreadForUserAsync(currentUserId);
}
```

---

## 4) Realtime – Reuse NotificationsHub as Chat Hub

**Path:** `src/MyCabs.Api/Realtime/ChatPusher.cs`

```csharp
using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Application.Realtime;

namespace MyCabs.Api.Realtime;

public class ChatPusher : IChatPusher
{
    private readonly IHubContext<NotificationsHub> _hub;
    public ChatPusher(IHubContext<NotificationsHub> hub) { _hub = hub; }

    private static string G(string threadId) => $"thread:{threadId}";

    public Task SendToThreadAsync(string threadId, string eventName, object payload)
        => _hub.Clients.Group(G(threadId)).SendAsync(eventName, payload);

    public Task SendToUserAsync(string userId, string eventName, object payload)
        => _hub.Clients.User(userId).SendAsync(eventName, payload);
}
```

**Path:** `src/MyCabs.Api/Hubs/NotificationsHub.cs` (bổ sung method)

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyCabs.Api.Hubs;

[Authorize]
public class NotificationsHub : Hub
{
    private static string G(string threadId) => $"thread:{threadId}";

    // Client gọi sau khi có threadId để vào group chat
    public Task JoinThread(string threadId) => Groups.AddToGroupAsync(Context.ConnectionId, G(threadId));
    public Task LeaveThread(string threadId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, G(threadId));

    // Typing indicator: broadcast cho người còn lại trong thread
    public Task Typing(string threadId, bool isTyping)
        => Clients.OthersInGroup(G(threadId)).SendAsync("chat.typing", new { threadId, userId = Context.UserIdentifier, isTyping });
}
```

---

## 5) API – Controller

**Path:** `src/MyCabs.Api/Controllers/ChatController.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;

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
        var (items,total) = await _svc.GetThreadsAsync(me, new ThreadsQuery(page, pageSize));
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<ThreadDto>(items, page, pageSize, total)));
    }

    [HttpGet("threads/{threadId}/messages")]
    public async Task<IActionResult> Messages([FromRoute] string threadId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        var me = CurrentUserId();
        var (items,total) = await _svc.GetMessagesAsync(me, threadId, new MessagesQuery(page, pageSize));
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
```

---

## 6) Program.cs – DI

**Path:** `src/MyCabs.Api/Program.cs` (đặt cạnh các đăng ký khác)

```csharp
using MyCabs.Application.Realtime; // IChatPusher

// Repos & Services
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IChatService, ChatService>();

// Realtime pusher dùng chung NotificationsHub
builder.Services.AddScoped<IChatPusher, ChatPusher>();

// (Hub route đã có cho NotificationsHub):
// app.MapHub<NotificationsHub>("/hubs/notifications");
```

---

## 7) Frontend/Client – Reuse existing connection

- **Kết nối**: dùng cùng `HubConnection` đang kết nối `/hubs/notifications`.
- **Tham gia thread**: sau khi có `threadId`, gọi phương thức hub `JoinThread(threadId)`.

```ts
// ví dụ (pseudo)
const conn = /* HubConnectionBuilder ... to /hubs/notifications */

// lắng nghe sự kiện chat
conn.on('chat.message', (m) => {
  // { id, threadId, senderUserId, recipientUserId, content, createdAt, readAt }
});
conn.on('chat.typing', (p) => { /* { threadId, userId, isTyping } */ });
conn.on('chat.unread_total', ({ count }) => setChatBadge(count));
conn.on('chat.thread_unread', ({ threadId, count }) => updateThreadUnread(threadId, count));

// sau khi gọi POST /api/chat/threads để lấy thread
await conn.invoke('JoinThread', threadId);

// khi rời màn hình chat
await conn.invoke('LeaveThread', threadId);

// typing:
await conn.invoke('Typing', threadId, true);
```

---

## 8) Test nhanh (Postman + Swagger)

1. **Login** 2 tài khoản A & B → lấy `accessToken` cho mỗi bên.
2. **A** gọi `POST /api/chat/threads` body `{ "peerUserId": "<B_userId>" }` → trả về `threadId`.
3. Trên client A và B, sau khi kết nối hub, gọi `JoinThread(threadId)`.
4. **A** gửi tin: `POST /api/chat/threads/{threadId}/messages` body `{ "content": "hello" }` → **B** nhận `chat.message` realtime + badge `chat.unread_total`.
5. **B** đọc: `POST /api/chat/threads/{threadId}/read` → A/B đều có thể thấy event `chat.thread_unread` (0) cho B.
6. `GET /api/chat/unread-count` để kiểm tra tổng.

> Gợi ý: có thể hiển thị danh sách thread từ `GET /api/chat/threads`, client đồng bộ unread theo mỗi thread bằng event `chat.thread_unread`.

