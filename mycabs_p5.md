# MyCabs – Rider Discovery & Ratings (MVP)

> Canvas đã **xóa nội dung cũ** và thay bằng module **Rider**: tìm Company/Driver + đánh giá & bình luận. Mục tiêu: có thể **search** (server-side filter/sort/paginate) và **rate/comment** cho `Company` hoặc `Driver`. Tất cả API trả về theo **ApiEnvelope** như trước.

---

## 1) Domain (Entities)

**Path:** `src/MyCabs.Domain/Entities/Rating.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Rating
{
    [BsonId] public ObjectId Id { get; set; }

    [BsonElement("targetType")]  // Company | Driver
    public string TargetType { get; set; } = "Company";

    [BsonElement("targetId")]
    public ObjectId TargetId { get; set; }

    [BsonElement("userId")]     // Rider user _id
    public ObjectId UserId { get; set; }

    [BsonElement("stars")]      // 1..5
    public int Stars { get; set; }

    [BsonElement("comment")]
    public string? Comment { get; set; }

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## 2) Domain (Interfaces)

**Path:** `src/MyCabs.Domain/Interfaces/IRatingRepository.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IRatingRepository
{
    Task CreateAsync(Rating r);
    Task<(IEnumerable<Rating> Items, long Total)> FindForTargetAsync(string targetType, string targetId, int page, int pageSize);
    Task<(long Count, double Average)> GetSummaryAsync(string targetType, string targetId);
    Task EnsureIndexesAsync();
}
```

> Sử dụng lại `ICompanyRepository.FindAsync(...)` & mở rộng `IDriverRepository` thêm `FindAsync(...)` (mục 4.2).

---

## 3) Infrastructure (Repositories)

**Path:** `src/MyCabs.Infrastructure/Repositories/RatingRepository.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class RatingRepository : IRatingRepository, IIndexInitializer
{
    private readonly IMongoCollection<Rating> _col;
    public RatingRepository(IMongoContext ctx) => _col = ctx.GetCollection<Rating>("ratings");

    public Task CreateAsync(Rating r) => _col.InsertOneAsync(r);

    public async Task<(IEnumerable<Rating> Items, long Total)> FindForTargetAsync(string targetType, string targetId, int page, int pageSize)
    {
        if (!ObjectId.TryParse(targetId, out var tid)) return (Enumerable.Empty<Rating>(), 0);
        var f = Builders<Rating>.Filter.Eq(x => x.TargetType, targetType) & Builders<Rating>.Filter.Eq(x => x.TargetId, tid);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<(long Count, double Average)> GetSummaryAsync(string targetType, string targetId)
    {
        if (!ObjectId.TryParse(targetId, out var tid)) return (0, 0);
        var f = Builders<Rating>.Filter.Eq(x => x.TargetType, targetType) & Builders<Rating>.Filter.Eq(x => x.TargetId, tid);
        var group = new BsonDocument { { "_id", BsonNull.Value }, { "count", new BsonDocument("$sum", 1) }, { "avg", new BsonDocument("$avg", "$stars") } };
        var pipeline = new[] { new BsonDocument("$match", f.Render(_col.DocumentSerializer, _col.Settings.SerializerRegistry)), new BsonDocument("$group", group) };
        var doc = await _col.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();
        if (doc == null) return (0, 0);
        return (doc["count"].AsInt64, doc["avg"].ToDouble());
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Rating>(Builders<Rating>.IndexKeys.Ascending(x => x.TargetType).Ascending(x => x.TargetId).Descending(x => x.CreatedAt));
        var ix2 = new CreateIndexModel<Rating>(Builders<Rating>.IndexKeys.Ascending(x => x.UserId));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2 });
    }
}
```

---

## 4) Application (DTOs, Queries, Validators, Services)

### 4.1 DTOs & Queries

**Path:** `src/MyCabs.Application/DTOs/RiderDtos.cs`

```csharp
namespace MyCabs.Application.DTOs;

public record RiderCompaniesQuery(int Page=1,int PageSize=10,string? Search=null,string? ServiceType=null,string? Plan=null,string? Sort=null);
public record RiderDriversQuery(int Page=1,int PageSize=10,string? Search=null,string? CompanyId=null,string? Sort=null);

public record CreateRatingDto(string TargetType, string TargetId, int Stars, string? Comment);
public record RatingsQuery(string TargetType, string TargetId, int Page=1, int PageSize=10);

public record RatingDto(string Id, string TargetType, string TargetId, string UserId, int Stars, string? Comment, DateTime CreatedAt);
public record RatingSummaryDto(long Count, double Average);
```

**Path:** `src/MyCabs.Application/Validation/RiderValidators.cs`

```csharp
using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class CreateRatingDtoValidator : AbstractValidator<CreateRatingDto>
{
    public CreateRatingDtoValidator()
    {
        RuleFor(x => x.TargetType).NotEmpty().Must(t => t is "Company" or "Driver");
        RuleFor(x => x.TargetId).NotEmpty();
        RuleFor(x => x.Stars).InclusiveBetween(1,5);
        RuleFor(x => x.Comment).MaximumLength(1000);
    }
}
```

### 4.2 Service

**Patch interface Driver repo** `src/MyCabs.Domain/Interfaces/IDriverRepository.cs`

```csharp
Task<(IEnumerable<Driver> Items, long Total)> FindAsync(int page, int pageSize, string? search, string? companyId, string? sort);
```

**Patch implement** `src/MyCabs.Infrastructure/Repositories/DriverRepository.cs`

```csharp
public async Task<(IEnumerable<Driver> Items, long Total)> FindAsync(int page, int pageSize, string? search, string? companyId, string? sort)
{
    var f = Builders<Driver>.Filter.Empty;
    if (!string.IsNullOrWhiteSpace(search))
        f &= Builders<Driver>.Filter.Or(
            Builders<Driver>.Filter.Regex(x => x.FullName, new BsonRegularExpression(search, "i")),
            Builders<Driver>.Filter.Regex(x => x.Phone, new BsonRegularExpression(search, "i"))
        );
    if (!string.IsNullOrWhiteSpace(companyId) && ObjectId.TryParse(companyId, out var cid))
        f &= Builders<Driver>.Filter.Eq(x => x.CompanyId, cid);

    var q = _col.Find(f);
    q = (sort?.ToLower()) switch
    {
        "name_asc"  => q.SortBy(x => x.FullName),
        "name_desc" => q.SortByDescending(x => x.FullName),
        _            => q.SortByDescending(x => x.CreatedAt)
    };
    var total = await _col.CountDocumentsAsync(f);
    var items = await q.Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
    return (items, total);
}
```

**Path:** `src/MyCabs.Application/Services/RiderService.cs`

```csharp
using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IRiderService
{
    Task<(IEnumerable<Company> Items,long Total)> SearchCompaniesAsync(RiderCompaniesQuery q);
    Task<(IEnumerable<Driver> Items,long Total)>  SearchDriversAsync(RiderDriversQuery q);

    Task CreateRatingAsync(string userId, CreateRatingDto dto);
    Task<(IEnumerable<RatingDto> Items,long Total)> GetRatingsAsync(RatingsQuery q);
    Task<RatingSummaryDto> GetRatingSummaryAsync(string targetType, string targetId);
}

public class RiderService : IRiderService
{
    private readonly ICompanyRepository _companies;
    private readonly IDriverRepository _drivers;
    private readonly IRatingRepository _ratings;

    public RiderService(ICompanyRepository companies, IDriverRepository drivers, IRatingRepository ratings)
    { _companies = companies; _drivers = drivers; _ratings = ratings; }

    public Task<(IEnumerable<Company> Items,long Total)> SearchCompaniesAsync(RiderCompaniesQuery q)
        => _companies.FindAsync(q.Page, q.PageSize, q.Search ?? string.Empty, q.Plan ?? string.Empty, q.ServiceType ?? string.Empty, q.Sort ?? string.Empty);

    public Task<(IEnumerable<Driver> Items,long Total)> SearchDriversAsync(RiderDriversQuery q)
        => _drivers.FindAsync(q.Page, q.PageSize, q.Search, q.CompanyId, q.Sort);

    public async Task CreateRatingAsync(string userId, CreateRatingDto dto)
    {
        if (!ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
        if (!ObjectId.TryParse(dto.TargetId, out var tid)) throw new ArgumentException("Invalid targetId");
        await _ratings.CreateAsync(new Rating {
            Id = ObjectId.GenerateNewId(), TargetType = dto.TargetType, TargetId = tid,
            UserId = uid, Stars = dto.Stars, Comment = dto.Comment, CreatedAt = DateTime.UtcNow
        });
    }

    public async Task<(IEnumerable<RatingDto> Items,long Total)> GetRatingsAsync(RatingsQuery q)
    {
        var (items,total) = await _ratings.FindForTargetAsync(q.TargetType, q.TargetId, q.Page, q.PageSize);
        return (items.Select(r => new RatingDto(r.Id.ToString(), r.TargetType, r.TargetId.ToString(), r.UserId.ToString(), r.Stars, r.Comment, r.CreatedAt)), total);
    }

    public async Task<RatingSummaryDto> GetRatingSummaryAsync(string targetType, string targetId)
    {
        var (count, avg) = await _ratings.GetSummaryAsync(targetType, targetId);
        return new RatingSummaryDto(count, Math.Round(avg, 2));
    }
}
```

---

## 5) API (Controller)

**Path:** `src/MyCabs.Api/Controllers/RidersController.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RidersController : ControllerBase
{
    private readonly IRiderService _svc;
    public RidersController(IRiderService svc) { _svc = svc; }

    // --- Search ---
    [AllowAnonymous]
    [HttpGet("companies")] // /api/riders/companies?search=&serviceType=&plan=&sort=&page=&pageSize=
    public async Task<IActionResult> SearchCompanies([FromQuery] RiderCompaniesQuery q)
    {
        var (items,total) = await _svc.SearchCompaniesAsync(q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(items, q.Page, q.PageSize, total)));
    }

    [AllowAnonymous]
    [HttpGet("drivers")] // /api/riders/drivers?search=&companyId=&sort=&page=&pageSize=
    public async Task<IActionResult> SearchDrivers([FromQuery] RiderDriversQuery q)
    {
        var (items,total) = await _svc.SearchDriversAsync(q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(items, q.Page, q.PageSize, total)));
    }

    // --- Ratings ---
    [Authorize(Roles = "Rider")]
    [HttpPost("ratings")] // body: CreateRatingDto
    public async Task<IActionResult> CreateRating([FromBody] CreateRatingDto dto)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        await _svc.CreateRatingAsync(uid, dto);
        return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Rating submitted" }));
    }

    [AllowAnonymous]
    [HttpGet("ratings")] // ?targetType=Company&targetId=...
    public async Task<IActionResult> GetRatings([FromQuery] RatingsQuery q)
    {
        var (items,total) = await _svc.GetRatingsAsync(q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<RatingDto>(items, q.Page, q.PageSize, total)));
    }

    [AllowAnonymous]
    [HttpGet("ratings/summary")] // ?targetType=&targetId=
    public async Task<IActionResult> GetRatingSummary([FromQuery] string targetType, [FromQuery] string targetId)
    {
        var s = await _svc.GetRatingSummaryAsync(targetType, targetId);
        return Ok(ApiEnvelope.Ok(HttpContext, s));
    }
}
```

> Ghi chú: `PagedResult<object>` phía search có thể thay bằng DTO riêng nếu bạn muốn ẩn bớt trường. Ở MVP giữ nguyên entity để nhanh.

---

## 6) DI (Program.cs)

Thêm đăng ký cho Rider & Rating:

**Path:** `src/MyCabs.Api/Program.cs`

```csharp
using MyCabs.Domain.Interfaces;           // IRatingRepository
using MyCabs.Infrastructure.Repositories; // RatingRepository
using MyCabs.Application.Services;        // IRiderService, RiderService
using MyCabs.Infrastructure.Startup;      // IIndexInitializer

builder.Services.AddScoped<IRatingRepository, RatingRepository>();
builder.Services.AddScoped<IRiderService, RiderService>();

builder.Services.AddScoped<IIndexInitializer, RatingRepository>();
```

> Đảm bảo `DriverRepository` có method `FindAsync(...)` như mục 4.2. Nếu trước đó bạn chưa `AddScoped<IDriverRepository, DriverRepository>()` trong DI thì thêm vào.

---

## 7) Test nhanh (Swagger/Postman)

- **Search Companies**

```
GET /api/riders/companies?search=alpha&serviceType=taxi&plan=Basic&sort=name_asc&page=1&pageSize=10
```

- **Search Drivers**

```
GET /api/riders/drivers?search=nam&companyId=<CompanyId>&sort=name_desc&page=1&pageSize=10
```

- **Create Rating** (Role=Rider)

```
POST /api/riders/ratings
{ "targetType":"Company", "targetId":"<companyId>", "stars":5, "comment":"Dịch vụ tốt!" }
```

- **List Ratings**

```
GET /api/riders/ratings?targetType=Company&targetId=<companyId>&page=1&pageSize=5
```

- **Summary**

```
GET /api/riders/ratings/summary?targetType=Company&targetId=<companyId>
```

---

## 8) Seed mẫu (mongosh)

```js
// Rating mẫu cho company
const companyId = ObjectId("<CompanyId>");
const riderUserId = ObjectId("<RiderUserId>");
db.ratings.insertOne({ targetType:"Company", targetId:companyId, userId:riderUserId, stars:5, comment:"Quá ổn", createdAt:new Date() });

// Rating mẫu cho driver
const driverId = ObjectId("<DriverId>");
db.ratings.insertOne({ targetType:"Driver", targetId:driverId, userId:riderUserId, stars:4, comment:"Lịch sự", createdAt:new Date() });
```

---

### Ghi chú thêm

- Nếu muốn ngăn 1 rider đánh giá trùng target nhiều lần, bạn có thể thay `CreateAsync` bằng **upsert** theo key `(userId,targetType,targetId)` và tạo unique index tương ứng.
- Có thể mở rộng trả về **ratingSummary** khi gọi chi tiết Company/Driver.
- Phần Frontend: hiển thị danh sách + form rate (1–5 sao), gọi `POST /api/riders/ratings` rồi reload list.



---

# Notifications (read/unread + SignalR)

> Module thông báo realtime (SignalR) + REST: tạo/list/đánh dấu đã đọc. Giữ đúng Clean Architecture: Application **không** phụ thuộc ASP.NET; realtime được bọc qua interface `IRealtimeNotifier` (implement ở Api).

## 1) Domain (Entity)
**Path:** `src/MyCabs.Domain/Entities/Notification.cs`
```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Notification
{
    [BsonId] public ObjectId Id { get; set; }
    [BsonElement("userId")] public ObjectId UserId { get; set; }
    [BsonElement("type")] public string Type { get; set; } = "info"; // info|warn|payment|chat|...
    [BsonElement("title")] public string? Title { get; set; }
    [BsonElement("message")] public string Message { get; set; } = string.Empty;
    [BsonElement("data")] public BsonDocument? Data { get; set; }
    [BsonElement("isRead")] public bool IsRead { get; set; } = false;
    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("readAt")] public DateTime? ReadAt { get; set; }
}
```

## 2) Domain (Interface)
**Path:** `src/MyCabs.Domain/Interfaces/INotificationRepository.cs`
```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface INotificationRepository
{
    Task CreateAsync(Notification n);
    Task<(IEnumerable<Notification> Items,long Total)> FindForUserAsync(string userId,int page,int pageSize,bool? isRead);
    Task<bool> MarkReadAsync(string userId,string notificationId);
    Task<long> MarkAllReadAsync(string userId);
    Task EnsureIndexesAsync();
}
```

## 3) Infrastructure (Repository)
**Path:** `src/MyCabs.Infrastructure/Repositories/NotificationRepository.cs`
```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class NotificationRepository : INotificationRepository, IIndexInitializer
{
    private readonly IMongoCollection<Notification> _col;
    public NotificationRepository(IMongoContext ctx) => _col = ctx.GetCollection<Notification>("notifications");

    public Task CreateAsync(Notification n) => _col.InsertOneAsync(n);

    public async Task<(IEnumerable<Notification> Items, long Total)> FindForUserAsync(string userId,int page,int pageSize,bool? isRead)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return (Enumerable.Empty<Notification>(),0);
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, uid);
        if (isRead.HasValue) f &= Builders<Notification>.Filter.Eq(x => x.IsRead, isRead.Value);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
        return (items,total);
    }

    public async Task<bool> MarkReadAsync(string userId,string notificationId)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return false;
        if (!ObjectId.TryParse(notificationId, out var nid)) return false;
        var upd = Builders<Notification>.Update.Set(x => x.IsRead, true).Set(x => x.ReadAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.Id == nid && x.UserId == uid && !x.IsRead, upd);
        return res.ModifiedCount > 0;
    }

    public async Task<long> MarkAllReadAsync(string userId)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return 0;
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, uid) & Builders<Notification>.Filter.Eq(x => x.IsRead, false);
        var upd = Builders<Notification>.Update.Set(x => x.IsRead, true).Set(x => x.ReadAt, DateTime.UtcNow);
        var res = await _col.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Notification>(Builders<Notification>.IndexKeys
            .Ascending(x => x.UserId).Descending(x => x.CreatedAt));
        var ix2 = new CreateIndexModel<Notification>(Builders<Notification>.IndexKeys
            .Ascending(x => x.UserId).Ascending(x => x.IsRead).Descending(x => x.CreatedAt));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2 });
    }
}
```

## 4) Application (DTOs + Service + Realtime interface)
**Path:** `src/MyCabs.Application/DTOs/NotificationDtos.cs`
```csharp
namespace MyCabs.Application.DTOs;

public record NotificationsQuery(int Page=1,int PageSize=10,bool? IsRead=null);
public record CreateNotificationDto(string Type,string? Title,string Message, Dictionary<string,object>? Data=null);
public record NotificationDto(string Id,string Type,string? Title,string Message,bool IsRead,DateTime CreatedAt,DateTime? ReadAt, Dictionary<string,object>? Data);
```

**Path:** `src/MyCabs.Application/Services/NotificationService.cs`
```csharp
using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IRealtimeNotifier
{
    Task NotifyUserAsync(string userId,string eventName,object payload);
}

public interface INotificationService
{
    Task PublishAsync(string userId, CreateNotificationDto dto);
    Task<(IEnumerable<NotificationDto> Items,long Total)> GetAsync(string userId, NotificationsQuery q);
    Task<bool> MarkReadAsync(string userId,string notificationId);
    Task<long> MarkAllReadAsync(string userId);
}

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    private readonly IRealtimeNotifier _rt;
    public NotificationService(INotificationRepository repo, IRealtimeNotifier rt){ _repo = repo; _rt = rt; }

    public async Task PublishAsync(string userId, CreateNotificationDto dto)
    {
        if (!ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
        var n = new Notification{
            Id = ObjectId.GenerateNewId(), UserId = uid, Type = dto.Type, Title = dto.Title,
            Message = dto.Message, Data = dto.Data!=null? BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dto.Data)) : null,
            CreatedAt = DateTime.UtcNow
        };
        await _repo.CreateAsync(n);
        var payload = new NotificationDto(n.Id.ToString(), n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt, n.ReadAt,
            dto.Data);
        await _rt.NotifyUserAsync(userId, "notification", payload);
    }

    public async Task<(IEnumerable<NotificationDto> Items,long Total)> GetAsync(string userId, NotificationsQuery q)
    {
        var (items,total) = await _repo.FindForUserAsync(userId, q.Page, q.PageSize, q.IsRead);
        var list = items.Select(n => new NotificationDto(
            n.Id.ToString(), n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt, n.ReadAt,
            n.Data != null ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,object>>(n.Data.ToJson()) : null
        ));
        return (list, total);
    }

    public Task<bool> MarkReadAsync(string userId,string notificationId) => _repo.MarkReadAsync(userId, notificationId);
    public Task<long> MarkAllReadAsync(string userId) => _repo.MarkAllReadAsync(userId);
}
```

## 5) API (SignalR Hub + Notifier + Controller)
**Path:** `src/MyCabs.Api/Hubs/NotificationsHub.cs`
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyCabs.Api.Hubs;

[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(userId))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, userId);
        await base.OnDisconnectedAsync(exception);
    }
}
```

**Path:** `src/MyCabs.Api/Realtime/SignalRNotifier.cs`
```csharp
using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Realtime;

public class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationsHub> _hub;
    public SignalRNotifier(IHubContext<NotificationsHub> hub){ _hub = hub; }
    public Task NotifyUserAsync(string userId,string eventName,object payload)
        => _hub.Clients.Group(userId).SendAsync(eventName, payload);
}
```

**Path:** `src/MyCabs.Api/Controllers/NotificationsController.cs`
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
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _svc;
    public NotificationsController(INotificationService svc){ _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] NotificationsQuery q)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        var (items,total) = await _svc.GetAsync(uid, q);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<NotificationDto>(items, q.Page, q.PageSize, total)));
    }

    [HttpPost("mark-read")] // body: { notificationId: "..." }
    public async Task<IActionResult> MarkRead([FromBody] Dictionary<string,string> body)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        if (!body.TryGetValue("notificationId", out var nid)) return BadRequest(ApiEnvelope.Fail(HttpContext,"VALIDATION_ERROR","notificationId is required",400));
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
```

## 6) Program.cs (DI + SignalR + CORS + JWT for hubs)
**Path:** `src/MyCabs.Api/Program.cs`
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MyCabs.Api.Hubs;
using MyCabs.Api.Realtime;
using MyCabs.Application.Services;            // INotificationService, IRealtimeNotifier
using MyCabs.Domain.Interfaces;               // INotificationRepository
using MyCabs.Infrastructure.Repositories;     // NotificationRepository
using MyCabs.Infrastructure.Startup;          // IIndexInitializer

// 1) CORS cho Vite
builder.Services.AddCors(options =>
{
    options.AddPolicy("ViteDev", p => p
        .WithOrigins("http://localhost:5173", "https://localhost:5173")
        .AllowAnyHeader().AllowAnyMethod()
        .AllowCredentials());
});

// 2) SignalR
builder.Services.AddSignalR();

// 3) Cho phép JWT từ query string khi kết nối Hub
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // (giữ cấu hình ValidateIssuer/Key như hiện có của bạn)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"].ToString();
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/notifications"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

// 4) DI
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddSingleton<IRealtimeNotifier, SignalRNotifier>();

builder.Services.AddScoped<IIndexInitializer, NotificationRepository>();

var app = builder.Build();

app.UseCors("ViteDev"); // đặt trước UseAuthentication nếu cần ws cross-origin
app.UseAuthentication();
app.UseAuthorization();

app.MapHub<NotificationsHub>("/hubs/notifications");
```
> Ghi chú: giữ nguyên phần cấu hình JWT (Issuer/Audience/SigningKey) bạn đang dùng; đoạn trên chỉ **thêm** handler để đọc token qua `access_token` khi client kết nối SignalR.

---

## 7) Test nhanh
1) **Kết nối Hub (JS – Vite/Console):**
```js
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
const token = '<JWT từ /api/auth/login>'; // chuỗi raw, không có "Bearer "
const conn = new HubConnectionBuilder()
  .withUrl('https://localhost:5001/hubs/notifications', { accessTokenFactory: () => token })
  .configureLogging(LogLevel.Information)
  .build();

conn.on('notification', (payload) => console.log('NOTIF:', payload));
await conn.start();
```
2) **Gửi thử 1 thông báo:**
```
POST /api/notifications/test
{ "type":"payment", "title":"Low wallet", "message":"Your wallet < 100k", "data": {"balance": 95000} }
```
→ Console sẽ log `NOTIF: {...}`; Đồng thời `GET /api/notifications` sẽ thấy item mới.

3) **Đánh dấu đã đọc:**
```
POST /api/notifications/mark-read
{ "notificationId": "<id>" }
```
Hoặc tất cả:
```
POST /api/notifications/mark-all-read
```

---

### Gợi ý mở rộng
- Loại thông báo: `payment.low_balance`, `hiring.invite`, `chat.new_message`…
- Tạo **unique index** nếu muốn chặn spam cùng 1 message/data trong thời gian ngắn.
- Có thể đẩy summary (unreadCount) sau mỗi `PublishAsync`/`Mark*` để client cập nhật badge.

