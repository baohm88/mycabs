# MyCabs – Notifications v2 (Unread Counter, Bulk, Soft Delete)

> Bổ sung cho module Notifications hiện có: **đếm số chưa đọc (unread\_count)** theo realtime (SignalR), **đọc nhiều cái** một lúc, **đánh dấu đã đọc tất cả**, và **xoá mềm**.

---

## 1) Domain (Entity) – Patch

**Path:** `src/MyCabs.Domain/Entities/Notification.cs`

```csharp
// Thêm (nếu chưa có)
[BsonElement("readAt")] public DateTime? ReadAt { get; set; }
[BsonElement("deletedAt")] public DateTime? DeletedAt { get; set; }

// Helper (không cần BsonElement)
[BsonIgnore] public bool IsRead => ReadAt != null;
```

> Lưu ý: nếu bạn đang dùng trường `IsRead` boolean, có thể giữ nguyên, nhưng **ưu tiên **`` để biết thời điểm đọc.

---

## 2) Domain (Interface) – Repository

**Path:** `src/MyCabs.Domain/Interfaces/INotificationRepository.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface INotificationRepository
{
    Task CreateAsync(Notification n);
    Task<(IEnumerable<Notification> Items,long Total)> FindAsync(string userId, int page, int pageSize, bool? unreadOnly);

    // New
    Task<long> CountUnreadAsync(string userId);
    Task<bool> MarkReadAsync(string userId, string id);
    Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids);
    Task<long> MarkAllReadAsync(string userId);
    Task<bool> SoftDeleteAsync(string userId, string id);
    Task EnsureIndexesAsync();
}
```

---

## 3) Infrastructure – Repository impl

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
    public NotificationRepository(IMongoContext ctx)
        => _col = ctx.GetCollection<Notification>("notifications");

    public Task CreateAsync(Notification n) => _col.InsertOneAsync(n);

    public async Task<(IEnumerable<Notification> Items,long Total)> FindAsync(string userId, int page, int pageSize, bool? unreadOnly)
    {
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, ObjectId.Parse(userId))
              & Builders<Notification>.Filter.Eq(x => x.DeletedAt, null);
        if (unreadOnly == true)
            f &= Builders<Notification>.Filter.Eq(x => x.ReadAt, null);

        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt)
            .Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public Task<long> CountUnreadAsync(string userId)
    {
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, ObjectId.Parse(userId))
              & Builders<Notification>.Filter.Eq(x => x.ReadAt, null)
              & Builders<Notification>.Filter.Eq(x => x.DeletedAt, null);
        return _col.CountDocumentsAsync(f);
    }

    public async Task<bool> MarkReadAsync(string userId, string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;
        var upd = Builders<Notification>.Update.Set(x => x.ReadAt, DateTime.UtcNow).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.Id == oid && x.UserId == ObjectId.Parse(userId) && x.ReadAt == null, upd);
        return res.ModifiedCount > 0;
    }

    public async Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids)
    {
        var valid = ids.Where(ObjectId.TryParse).Select(ObjectId.Parse).ToArray();
        if (valid.Length == 0) return 0;
        var f = Builders<Notification>.Filter.In(x => x.Id, valid)
              & Builders<Notification>.Filter.Eq(x => x.UserId, ObjectId.Parse(userId))
              & Builders<Notification>.Filter.Eq(x => x.ReadAt, null);
        var upd = Builders<Notification>.Update.Set(x => x.ReadAt, DateTime.UtcNow).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }

    public async Task<long> MarkAllReadAsync(string userId)
    {
        var f = Builders<Notification>.Filter.Eq(x => x.UserId, ObjectId.Parse(userId))
              & Builders<Notification>.Filter.Eq(x => x.ReadAt, null)
              & Builders<Notification>.Filter.Eq(x => x.DeletedAt, null);
        var upd = Builders<Notification>.Update.Set(x => x.ReadAt, DateTime.UtcNow).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateManyAsync(f, upd);
        return res.ModifiedCount;
    }

    public async Task<bool> SoftDeleteAsync(string userId, string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;
        var upd = Builders<Notification>.Update.Set(x => x.DeletedAt, DateTime.UtcNow).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.Id == oid && x.UserId == ObjectId.Parse(userId) && x.DeletedAt == null, upd);
        return res.ModifiedCount > 0;
    }

    public async Task EnsureIndexesAsync()
    {
        var ix = new List<CreateIndexModel<Notification>>
        {
            new(CreateIndexKeys<Notification>.Ascending(x => x.UserId).Descending(x => x.CreatedAt)),
            new(CreateIndexKeys<Notification>.Ascending(x => x.UserId).Ascending(x => x.ReadAt)),
            new(CreateIndexKeys<Notification>.Ascending(x => x.DeletedAt))
        };
        await _col.Indexes.CreateManyAsync(ix);
    }
}
```

---

## 4) Application – Service

**Path:** `src/MyCabs.Application/Services/NotificationService.cs`

```csharp
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface INotificationService
{
    Task<long> GetUnreadCountAsync(string userId);
    Task<long> MarkAllReadAsync(string userId);
    Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids);
    Task<bool> MarkReadAsync(string userId, string id);
    Task<bool> DeleteAsync(string userId, string id);
}

public class NotificationService : INotificationService
{
    private readonly INotificationRepository _repo;
    private readonly IRealtimeNotifier _rt;
    public NotificationService(INotificationRepository repo, IRealtimeNotifier rt)
    { _repo = repo; _rt = rt; }

    public Task<long> GetUnreadCountAsync(string userId) => _repo.CountUnreadAsync(userId);

    public async Task<long> MarkAllReadAsync(string userId)
    {
        var n = await _repo.MarkAllReadAsync(userId);
        await _rt.PushUnreadCountAsync(userId); // realtime
        return n;
    }

    public async Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids)
    {
        var n = await _repo.MarkReadBulkAsync(userId, ids);
        if (n > 0) await _rt.PushUnreadCountAsync(userId);
        return n;
    }

    public async Task<bool> MarkReadAsync(string userId, string id)
    {
        var ok = await _repo.MarkReadAsync(userId, id);
        if (ok) await _rt.PushUnreadCountAsync(userId);
        return ok;
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        var ok = await _repo.SoftDeleteAsync(userId, id);
        if (ok) await _rt.PushUnreadCountAsync(userId);
        return ok;
    }
}
```

> Sử dụng `IRealtimeNotifier` đã có. Bổ sung thêm 1 method mới bên dưới.

---

## 5) Realtime – Notifier + Hub event

**Path:** `src/MyCabs.Application/Realtime/IRealtimeNotifier.cs`

````csharp
namespace MyCabs.Application.Realtime;

public interface IRealtimeNotifier
{
    // gửi event tuỳ tên
    Task NotifyUserAsync(string userId, string eventName, object payload);

    // tiện ích: đẩy sự kiện "notification"
    Task PushNotificationAsync(string userId, object payload);

    // tiện ích: đẩy badge số chưa đọc
    Task PushUnreadCountAsync(string userId);
}
```csharp
namespace MyCabs.Api.Realtime;

public interface IRealtimeNotifier
{
    Task PushNotificationAsync(string userId, object payload);

    // New: đẩy badge số chưa đọc
    Task PushUnreadCountAsync(string userId);
}
````

**Path:** `src/MyCabs.Api/Realtime/SignalRNotifier.cs`

````csharp
using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Application.Realtime;   // implement interface từ Application
using MyCabs.Domain.Interfaces;

namespace MyCabs.Api.Realtime;

public class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly INotificationRepository _repo;
    public SignalRNotifier(IHubContext<NotificationsHub> hub, INotificationRepository repo)
    { _hub = hub; _repo = repo; }

    public Task PushNotificationAsync(string userId, object payload)
        => NotifyUserAsync(userId, "notification", payload);

    public async Task PushUnreadCountAsync(string userId)
    {
        var count = await _repo.CountUnreadAsync(userId);
        await NotifyUserAsync(userId, "unread_count", new { count });
    }

    public Task NotifyUserAsync(string userId, string eventName, object payload)
        => _hub.Clients.User(userId).SendAsync(eventName, payload);
}
```csharp
using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Api.Realtime;

public class SignalRNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NotificationsHub> _hub;
    private readonly INotificationRepository _repo;
    public SignalRNotifier(IHubContext<NotificationsHub> hub, INotificationRepository repo)
    { _hub = hub; _repo = repo; }

    public Task PushNotificationAsync(string userId, object payload)
        => _hub.Clients.User(userId).SendAsync("notification", payload);

    public async Task PushUnreadCountAsync(string userId)
    {
        var count = await _repo.CountUnreadAsync(userId);
        await _hub.Clients.User(userId).SendAsync("unread_count", new { count });
    }
}
````

**Path:** `src/MyCabs.Api/Hubs/NotificationsHub.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyCabs.Api.Hubs;

[Authorize]
public class NotificationsHub : Hub
{
    // (giữ nguyên). Có thể thêm method "Ping" nếu cần debug.
}
```

---

## 6) API – Controller

**Path:** `src/MyCabs.Api/Controllers/NotificationsController.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.Services;

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

    private string CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;

    // List & filter (unreadOnly=true/false/null)
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool? unreadOnly = null)
    {
        var uid = CurrentUserId();
        var (items,total) = await _repo.FindAsync(uid, page, pageSize, unreadOnly);
        return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<object>(items, page, pageSize, total)));
    }

    // Đếm số chưa đọc
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
```

---

## 7) Program.cs – DI (nếu cần)

**Path:** `src/MyCabs.Api/Program.cs`

```csharp
// using MyCabs.Application.Realtime; // đảm bảo import

// Đăng ký DI
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// IRealtimeNotifier nên là Scoped (vì dùng repository Scoped)
builder.Services.AddScoped<IRealtimeNotifier, SignalRNotifier>();
```

> Nếu bạn còn file `src/MyCabs.Api/Realtime/IRealtimeNotifier.cs` thì **xoá** để tránh trùng interface với bản ở Application.

---

## 8) Frontend – bắt badge realtime

```js
import { HubConnectionBuilder } from '@microsoft/signalr';

const token = localStorage.getItem('accessToken');
const conn = new HubConnectionBuilder()
  .withUrl('http://localhost:5000/hubs/notifications', { accessTokenFactory: () => token })
  .build();

conn.on('unread_count', ({ count }) => {
  // cập nhật badge ở navbar
  console.log('UNREAD:', count);
});

await conn.start();

// Đồng bộ lần đầu:
const res = await fetch('http://localhost:5000/api/notifications/unread-count', { headers: { Authorization: `Bearer ${token}` } });
const { data } = await res.json();
setBadge(data.count);
```

---

## 9) Test nhanh

1. **Login** lấy JWT.
2. **GET** `/api/notifications/unread-count` → `{ count: N }`.
3. Tạo 1 notification test (API bạn đã có) → client nhận `notification` + `unread_count` mới.
4. **POST** `/api/notifications/mark-read-bulk` body: `{ "ids": ["<id1>","<id2>"] }` → nhận event `unread_count` giảm.
5. **POST** `/api/notifications/mark-all-read` → `unread_count = 0`.
6. **DELETE** `/api/notifications/{id}` → item biến mất khỏi list.

---

## 10) Gợi ý Hook sự kiện

- **Wallet thấp**: sau khi cập nhật số dư ví, nếu `< threshold` → `PushNotificationAsync(userId, payload)` rồi gọi `PushUnreadCountAsync(userId)`.
- **Có rating mới**: khi rider tạo rating cho Company/Driver → gửi thông báo cho owner tương ứng.

> Các hook này bạn có thể thêm thẳng ở `FinanceService` (số dư ví) và `RiderService.CreateRatingAsync` (rating mới).

---

## 10) Hooks – Thông báo **rating mới** & **ví thấp**

### 10.1) Domain constant (loại thông báo)

**Path:** `src/MyCabs.Domain/Constants/NotificationKinds.cs`

```csharp
namespace MyCabs.Domain.Constants;

public static class NotificationKinds
{
    public const string RatingNew = "rating_new";
    public const string WalletLowBalance = "wallet_low_balance";
}
```

### 10.2) Application – mở rộng NotificationService với `PublishAsync`

**Path:** `src/MyCabs.Application/Services/NotificationService.cs`

> Thêm vào interface và implement (nếu file của bạn đã có thì chỉ cần giữ đúng chữ ký dưới).

```csharp
// interface
public interface INotificationService
{
    Task PublishAsync(string userId, CreateNotificationDto dto); // NEW
    Task<long> GetUnreadCountAsync(string userId);
    Task<long> MarkAllReadAsync(string userId);
    Task<long> MarkReadBulkAsync(string userId, IEnumerable<string> ids);
    Task<bool> MarkReadAsync(string userId, string id);
    Task<bool> DeleteAsync(string userId, string id);
}

// class NotificationService: bổ sung method dưới
public async Task PublishAsync(string userId, CreateNotificationDto dto)
{
    if (!MongoDB.Bson.ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
    var n = new Notification
    {
        Id = MongoDB.Bson.ObjectId.GenerateNewId(),
        UserId = uid,
        Type = dto.Type,
        Title = dto.Title,
        Message = dto.Message,
        Data = dto.Data != null
            ? MongoDB.Bson.BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dto.Data))
            : null,
        CreatedAt = DateTime.UtcNow
    };
    await _repo.CreateAsync(n);

    var payload = new NotificationDto(
        n.Id.ToString(), n.Type, n.Title, n.Message, n.IsRead, n.CreatedAt, n.ReadAt, dto.Data
    );
    await _rt.NotifyUserAsync(userId, "notification", payload);
}
```

### 10.3) RiderService – bắn thông báo khi có **rating mới**

**Path:** `src/MyCabs.Application/Services/RiderService.cs`

> Sau khi lưu rating thành công, lấy **ownerUserId** của company/driver để gửi notif.

```csharp
using MyCabs.Domain.Constants; // NotificationKinds

public class RiderService : IRiderService
{
    private readonly INotificationService _notif;
    private readonly ICompanyRepository _companies;
    private readonly IDriverRepository _drivers;
    // ... inject _notif, _companies, _drivers qua ctor

    public async Task<string> CreateRatingAsync(string riderUserId, CreateRatingDto dto)
    {
        // 1) Lưu rating như hiện tại → giả sử trả về entity r
        var r = await _ratingRepo.CreateAsync(dto.ToEntity(riderUserId));

        // 2) Xác định owner nhận thông báo
        string? ownerUserId = null; string subjectTitle = "";
        if (dto.SubjectType?.ToLowerInvariant() == "company")
        {
            var c = await _companies.GetByIdAsync(dto.SubjectId);
            ownerUserId = c?.OwnerUserId.ToString();
            subjectTitle = c?.Name ?? "Company";
        }
        else if (dto.SubjectType?.ToLowerInvariant() == "driver")
        {
            var d = await _drivers.GetByIdAsync(dto.SubjectId);
            ownerUserId = d?.UserId.ToString();
            subjectTitle = d?.DisplayName ?? "Driver"; // tuỳ entity
        }

        if (!string.IsNullOrEmpty(ownerUserId))
        {
            await _notif.PublishAsync(ownerUserId!, new CreateNotificationDto
            {
                Type = NotificationKinds.RatingNew,
                Title = $"Bạn có đánh giá mới cho {subjectTitle}",
                Message = $"{dto.Stars}/5★: {dto.Comment}",
                Data = new Dictionary<string, object?>
                {
                    ["ratingId"] = r.Id.ToString(),
                    ["subjectType"] = dto.SubjectType,
                    ["subjectId"] = dto.SubjectId,
                    ["stars"] = dto.Stars,
                    ["comment"] = dto.Comment
                }
            });
        }
        return r.Id.ToString();
    }
}
```

> Nếu `Driver` không có `DisplayName/UserId`, thay bằng thuộc tính tương ứng trong entity của bạn.

### 10.4) FinanceService – thông báo **ví thấp**

**Path:** `src/MyCabs.Application/Services/FinanceService.cs`

> Gọi hook sau những giao dịch **trừ tiền** (pay-salary, membership, v.v.).

```csharp
using Microsoft.Extensions.Configuration;
using MyCabs.Domain.Constants; // NotificationKinds

public class FinanceService : IFinanceService
{
    private readonly IConfiguration _cfg;
    private readonly INotificationService _notif;
    private const decimal DEFAULT_THRESHOLD = 200_000M; // 200k

    public FinanceService(/* ... */, IConfiguration cfg, INotificationService notif /* ... */)
    {
        _cfg = cfg; _notif = notif; /* gán các field khác như cũ */
    }

    // Helper
    private async Task MaybeNotifyLowBalanceAsync(string ownerUserId, Wallet w)
    {
        var threshold = _cfg.GetValue<decimal?>("Finance:LowBalanceThreshold") ?? DEFAULT_THRESHOLD;
        if (w.Balance < threshold)
        {
            await _notif.PublishAsync(ownerUserId, new CreateNotificationDto
            {
                Type = NotificationKinds.WalletLowBalance,
                Title = "Số dư ví thấp",
                Message = $"Số dư còn {w.Balance:N0} < ngưỡng {threshold:N0}",
                Data = new Dictionary<string, object?>
                {
                    ["walletId"] = w.Id.ToString(),
                    ["balance"] = w.Balance,
                    ["threshold"] = threshold
                }
            });
        }
    }

    // Ví dụ trong PaySalary (Company → Driver)
    public async Task<bool> PaySalaryAsync(string companyId, string driverId, decimal amount)
    {
        // ... logic trừ ví company, cộng ví driver, ghi transaction như hiện tại ...
        // Sau khi cập nhật ví company:
        var c = await _companyRepo.GetByIdAsync(companyId);
        var companyWallet = await _walletRepo.GetByOwnerAsync("company", companyId);
        if (c != null && companyWallet != null)
            await MaybeNotifyLowBalanceAsync(c.OwnerUserId.ToString(), companyWallet);
        return true;
    }

    // Ví dụ trong MembershipPay (Company → Admin)
    public async Task<bool> PayMembershipAsync(string companyId, string plan, string cycle)
    {
        // ... trừ ví company + transaction ...
        var c = await _companyRepo.GetByIdAsync(companyId);
        var companyWallet = await _walletRepo.GetByOwnerAsync("company", companyId);
        if (c != null && companyWallet != null)
            await MaybeNotifyLowBalanceAsync(c.OwnerUserId.ToString(), companyWallet);
        return true;
    }
}
```

### 10.5) App settings (tùy chọn)

**Path:** `src/MyCabs.Api/appsettings.Development.json`

```json
{
  "Finance": {
    "LowBalanceThreshold": 200000
  }
}
```

> Nếu không cấu hình, service dùng `DEFAULT_THRESHOLD = 200000`.

### 10.6) Test nhanh

1. **Login** company owner → mở kết nối SignalR (frontend) hoặc quan sát console backend.
2. Gọi API **pay-salary** hoặc **membership/pay** sao cho số dư ví giảm **dưới** `LowBalanceThreshold` → nhận notif `wallet_low_balance` + badge `unread_count` cập nhật.
3. Dùng account Rider tạo **rating mới** (đối tượng `company` hoặc `driver`) → owner của đối tượng nhận notif `rating_new` + badge cập nhật.
4. Kiểm tra `/api/notifications` và `/api/notifications/unread-count` để xác nhận.

