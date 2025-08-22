# MyCabs – Admin Dashboards (Payments/Wallet/Users)

> Tổng hợp số liệu cho **Payments / Wallet / Users**: API thống kê nhanh theo khoảng thời gian (from–to), chuỗi thời gian (daily), top entities, ví thấp… Sử dụng MongoDB Aggregation. Không phụ thuộc frontend.

---

## 1) DTOs (Application)

**Path:** `src/MyCabs.Application/DTOs/AdminDashDtos.cs`

```csharp
namespace MyCabs.Application.DTOs;

public record DateRangeQuery(DateTime? From = null, DateTime? To = null);
public record PagingQuery(int Page = 1, int PageSize = 20);

// Tổng quan
public record AdminOverviewDto(
    long UsersTotal,
    long CompaniesTotal,
    long DriversTotal,
    decimal WalletsTotalBalance,
    long TxCount,
    decimal TxAmount,
    IReadOnlyDictionary<string, decimal> TxAmountByType,
    IReadOnlyDictionary<string, long> TxCountByType
);

// Time-series (daily)
public record TimePointDto(string Date, long Count, decimal Amount);

// Top entity
public record TopCompanyDto(string CompanyId, string? Name, decimal Amount, long Count);
public record TopDriverDto(string DriverId, string? Name, decimal Amount, long Count);

// Ví thấp
public record LowWalletDto(string WalletId, string OwnerType, string OwnerId, decimal Balance, decimal? Threshold);
```

---

## 2) Domain – Interface Repository

**Path:** `src/MyCabs.Domain/Interfaces/IAdminReportRepository.cs`

```csharp
using MyCabs.Application.DTOs;

namespace MyCabs.Domain.Interfaces;

public interface IAdminReportRepository
{
    Task<AdminOverviewDto> GetOverviewAsync(DateTime from, DateTime to);
    Task<IEnumerable<TimePointDto>> GetTransactionsDailyAsync(DateTime from, DateTime to);
    Task<IEnumerable<TopCompanyDto>> GetTopCompaniesAsync(DateTime from, DateTime to, int limit);
    Task<IEnumerable<TopDriverDto>> GetTopDriversAsync(DateTime from, DateTime to, int limit);
    Task<IEnumerable<LowWalletDto>> GetLowWalletsAsync(decimal threshold, int limit, string ownerType = "Company");
}
```

> Giao diện dùng **Application DTOs** để tránh duplicate model map thêm lần nữa.

---

## 3) Infrastructure – Mongo Implementation

**Path:** `src/MyCabs.Infrastructure/Repositories/AdminReportRepository.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;

namespace MyCabs.Infrastructure.Repositories;

public class AdminReportRepository : IAdminReportRepository
{
    private readonly IMongoCollection<User> _users;
    private readonly IMongoCollection<Company> _companies;
    private readonly IMongoCollection<Driver> _drivers;
    private readonly IMongoCollection<Wallet> _wallets;
    private readonly IMongoCollection<Transaction> _txs;

    public AdminReportRepository(IMongoContext ctx)
    {
        _users = ctx.GetCollection<User>("users");
        _companies = ctx.GetCollection<Company>("companies");
        _drivers = ctx.GetCollection<Driver>("drivers");
        _wallets = ctx.GetCollection<Wallet>("wallets");
        _txs = ctx.GetCollection<Transaction>("transactions");
    }

    public async Task<AdminOverviewDto> GetOverviewAsync(DateTime from, DateTime to)
    {
        // 1) Totals
        var usersTotal = await _users.CountDocumentsAsync(FilterDefinition<User>.Empty);
        var companiesTotal = await _companies.CountDocumentsAsync(FilterDefinition<Company>.Empty);
        var driversTotal = await _drivers.CountDocumentsAsync(FilterDefinition<Driver>.Empty);

        // 2) Wallet sums
        var walletsAgg = await _wallets.Aggregate()
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "sum", new BsonDocument("$sum", "$balance") } })
            .FirstOrDefaultAsync();
        var walletsTotalBalance = walletsAgg == null ? 0 : walletsAgg["sum"].ToDecimal();

        // 3) Transactions in range
        var match = Builders<Transaction>.Filter.Gte(x => x.CreatedAt, from) & Builders<Transaction>.Filter.Lte(x => x.CreatedAt, to);

        var txCount = await _txs.CountDocumentsAsync(match);

        var txAmountAgg = await _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument { { "_id", BsonNull.Value }, { "amount", new BsonDocument("$sum", "$amount") } })
            .FirstOrDefaultAsync();
        var txAmount = txAmountAgg == null ? 0 : txAmountAgg["amount"].ToDecimal();

        // 4) Breakdown by Type
        var byType = await _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument {
                {"_id", "$type"},
                {"amount", new BsonDocument("$sum", "$amount")},
                {"count", new BsonDocument("$sum", 1)}
            })
            .ToListAsync();
        var amountByType = byType.ToDictionary(d => d["_id"].AsString, d => d["amount"].ToDecimal());
        var countByType = byType.ToDictionary(d => d["_id"].AsString, d => d["count"].ToInt64());

        return new AdminOverviewDto(usersTotal, companiesTotal, driversTotal, walletsTotalBalance, txCount, txAmount, amountByType, countByType);
    }

    public async Task<IEnumerable<TimePointDto>> GetTransactionsDailyAsync(DateTime from, DateTime to)
    {
        var match = Builders<Transaction>.Filter.Gte(x => x.CreatedAt, from) & Builders<Transaction>.Filter.Lte(x => x.CreatedAt, to);
        // group by day using $dateToString to keep compat
        var pipeline = _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument {
                {"_id", new BsonDocument("$dateToString", new BsonDocument{ {"format","%Y-%m-%d"}, {"date","$createdAt"} })},
                {"amount", new BsonDocument("$sum", "$amount")},
                {"count", new BsonDocument("$sum", 1)}
            })
            .Sort(new BsonDocument("_id", 1));
        var docs = await pipeline.ToListAsync();
        return docs.Select(d => new TimePointDto(d["_id"].AsString, d["count"].ToInt64(), d["amount"].ToDecimal()));
    }

    public async Task<IEnumerable<TopCompanyDto>> GetTopCompaniesAsync(DateTime from, DateTime to, int limit)
    {
        var match = Builders<Transaction>.Filter.Gte(x => x.CreatedAt, from) & Builders<Transaction>.Filter.Lte(x => x.CreatedAt, to)
                  & Builders<Transaction>.Filter.Ne(x => x.CompanyId, null);
        var pipeline = _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument{
                {"_id","$companyId"},
                {"amount", new BsonDocument("$sum","$amount")},
                {"count", new BsonDocument("$sum", 1)}
            })
            .Sort(new BsonDocument("amount", -1))
            .Limit(limit)
            .Lookup("companies", "_id", "_id", "cmp")
            .Project(new BsonDocument{
                {"companyId", new BsonDocument("$toString", "$_id")},
                {"name", new BsonDocument("$let", new BsonDocument{{"vars", new BsonDocument("c", new BsonDocument("$arrayElemAt", new BsonArray{ "$cmp", 0 }))},{"in", "$$c.name"}})},
                {"amount", 1},
                {"count", 1}
            });
        var docs = await pipeline.ToListAsync();
        return docs.Select(d => new TopCompanyDto(
            d["companyId"].AsString,
            d.Contains("name") && d["name"].BsonType != BsonType.Null ? d["name"].AsString : null,
            d["amount"].ToDecimal(),
            d["count"].ToInt64()
        ));
    }

    public async Task<IEnumerable<TopDriverDto>> GetTopDriversAsync(DateTime from, DateTime to, int limit)
    {
        var match = Builders<Transaction>.Filter.Gte(x => x.CreatedAt, from) & Builders<Transaction>.Filter.Lte(x => x.CreatedAt, to)
                  & Builders<Transaction>.Filter.Ne(x => x.DriverId, null);
        var pipeline = _txs.Aggregate()
            .Match(match)
            .Group(new BsonDocument{
                {"_id","$driverId"},
                {"amount", new BsonDocument("$sum","$amount")},
                {"count", new BsonDocument("$sum", 1)}
            })
            .Sort(new BsonDocument("amount", -1))
            .Limit(limit)
            .Lookup("drivers", "_id", "_id", "drv")
            .Project(new BsonDocument{
                {"driverId", new BsonDocument("$toString", "$_id")},
                {"name", new BsonDocument("$let", new BsonDocument{{"vars", new BsonDocument("d", new BsonDocument("$arrayElemAt", new BsonArray{ "$drv", 0 }))},{"in", "$$d.fullName"}})},
                {"amount", 1},
                {"count", 1}
            });
        var docs = await pipeline.ToListAsync();
        return docs.Select(d => new TopDriverDto(
            d["driverId"].AsString,
            d.Contains("name") && d["name"].BsonType != BsonType.Null ? d["name"].AsString : null,
            d["amount"].ToDecimal(),
            d["count"].ToInt64()
        ));
    }

    public async Task<IEnumerable<LowWalletDto>> GetLowWalletsAsync(decimal threshold, int limit, string ownerType = "Company")
    {
        var f = Builders<Wallet>.Filter.Eq(x => x.OwnerType, ownerType) & Builders<Wallet>.Filter.Lt(x => x.Balance, threshold);
        var items = await _wallets.Find(f).SortBy(x => x.Balance).Limit(limit).ToListAsync();
        return items.Select(w => new LowWalletDto(w.Id.ToString(), w.OwnerType, w.OwnerId.ToString(), w.Balance, w.LowBalanceThreshold));
    }
}
```

> Lưu ý: các field như `name`, `fullName` trong lookup cần trùng với entity hiện có. Nếu khác, chỉnh lại key trong `Project`.

---

## 4) Application – Service Facade

**Path:** `src/MyCabs.Application/Services/AdminReportService.cs`

```csharp
using MyCabs.Application.DTOs;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IAdminReportService
{
    Task<AdminOverviewDto> OverviewAsync(DateRangeQuery q);
    Task<IEnumerable<TimePointDto>> TransactionsDailyAsync(DateRangeQuery q);
    Task<IEnumerable<TopCompanyDto>> TopCompaniesAsync(DateRangeQuery q, int limit = 10);
    Task<IEnumerable<TopDriverDto>> TopDriversAsync(DateRangeQuery q, int limit = 10);
    Task<IEnumerable<LowWalletDto>> LowWalletsAsync(decimal? threshold = null, int limit = 20, string ownerType = "Company");
}

public class AdminReportService : IAdminReportService
{
    private readonly IAdminReportRepository _repo;
    public AdminReportService(IAdminReportRepository repo) { _repo = repo; }

    private static (DateTime from, DateTime to) Normalize(DateRangeQuery q)
    {
        var to = q.To?.ToUniversalTime() ?? DateTime.UtcNow;
        var from = q.From?.ToUniversalTime() ?? to.AddDays(-30);
        return (from, to);
    }

    public Task<AdminOverviewDto> OverviewAsync(DateRangeQuery q)
    { var (f,t) = Normalize(q); return _repo.GetOverviewAsync(f, t); }

    public Task<IEnumerable<TimePointDto>> TransactionsDailyAsync(DateRangeQuery q)
    { var (f,t) = Normalize(q); return _repo.GetTransactionsDailyAsync(f, t); }

    public Task<IEnumerable<TopCompanyDto>> TopCompaniesAsync(DateRangeQuery q, int limit = 10)
    { var (f,t) = Normalize(q); return _repo.GetTopCompaniesAsync(f, t, limit); }

    public Task<IEnumerable<TopDriverDto>> TopDriversAsync(DateRangeQuery q, int limit = 10)
    { var (f,t) = Normalize(q); return _repo.GetTopDriversAsync(f, t, limit); }

    public Task<IEnumerable<LowWalletDto>> LowWalletsAsync(decimal? threshold = null, int limit = 20, string ownerType = "Company")
    { return _repo.GetLowWalletsAsync(threshold ?? 200_000M, limit, ownerType); }
}
```

---

## 5) API – Controller

**Path:** `src/MyCabs.Api/Controllers/AdminReportsController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/admin/reports")]
[Authorize(Roles = "Admin")] // CHANGED: restrict to Admin role
public class AdminReportsController : ControllerBase
{
    private readonly IAdminReportService _svc;
    public AdminReportsController(IAdminReportService svc) { _svc = svc; }

    [HttpGet("overview")] // ?from=2025-01-01&to=2025-01-31
    public async Task<IActionResult> Overview([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var data = await _svc.OverviewAsync(new DateRangeQuery(from, to));
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [HttpGet("tx-daily")] // time-series daily
    public async Task<IActionResult> TxDaily([FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var data = await _svc.TransactionsDailyAsync(new DateRangeQuery(from, to));
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [HttpGet("top-companies")] // ?limit=10
    public async Task<IActionResult> TopCompanies([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int limit = 10)
    {
        var data = await _svc.TopCompaniesAsync(new DateRangeQuery(from, to), limit);
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [HttpGet("top-drivers")] // ?limit=10
    public async Task<IActionResult> TopDrivers([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int limit = 10)
    {
        var data = await _svc.TopDriversAsync(new DateRangeQuery(from, to), limit);
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }

    [HttpGet("low-wallets")] // ?threshold=200000&limit=20&ownerType=Company
    public async Task<IActionResult> LowWallets([FromQuery] decimal? threshold = null, [FromQuery] int limit = 20, [FromQuery] string ownerType = "Company")
    {
        var data = await _svc.LowWalletsAsync(threshold, limit, ownerType);
        return Ok(ApiEnvelope.Ok(HttpContext, data));
    }
}
```

> Controller **bọc** dữ liệu trong `ApiEnvelope.Ok(...)` như các API khác.

---

## 6) Program.cs – DI

**Path:** `src/MyCabs.Api/Program.cs`

```csharp
using MyCabs.Domain.Interfaces;           // IAdminReportRepository
using MyCabs.Infrastructure.Repositories; // AdminReportRepository
using MyCabs.Application.Services;        // IAdminReportService, AdminReportService

// ... sau phần đăng ký các repos/services khác
builder.Services.AddScoped<IAdminReportRepository, AdminReportRepository>();
builder.Services.AddScoped<IAdminReportService, AdminReportService>();

// CHANGED: DI cho JWT – map interface ở Application sang implementation ở API
// Tránh khai báo interface trùng tên ở API
builder.Services.AddSingleton<MyCabs.Application.IJwtTokenService, MyCabs.Api.Jwt.JwtTokenService>();
```

---

## 7) Gợi ý Indexes (tuỳ chọn)

Nếu volume lớn, thêm index để aggregation nhanh hơn:

**Path:** `src/MyCabs.Infrastructure/Repositories/TransactionRepository.cs` (hoặc nơi khởi tạo index)

```csharp
// đảm bảo có index
// { createdAt: 1 }, { type: 1, createdAt: -1 }, { companyId: 1, createdAt: -1 }, { driverId: 1, createdAt: -1 }
```

**Path:** `src/MyCabs.Infrastructure/Startup/DbInitializer.cs` (nếu bạn quản lý tập trung)

```csharp
// gọi EnsureIndexes của Transaction/Wallet nếu chưa có
```

---

## 8) Test nhanh (Swagger/Postman)

1. **Overview** – 30 ngày gần nhất (mặc định):

```
GET /api/admin/reports/overview
```

hoặc:

```
GET /api/admin/reports/overview?from=2025-02-01&to=2025-02-28
```

2. **Time-series** giao dịch theo ngày:

```
GET /api/admin/reports/tx-daily?from=2025-02-01&to=2025-02-28
```

3. **Top companies** theo tổng amount:

```
GET /api/admin/reports/top-companies?from=2025-02-01&to=2025-02-28&limit=5
```

4. **Top drivers** theo tổng amount:

```
GET /api/admin/reports/top-drivers?from=2025-02-01&to=2025-02-28&limit=5
```

5. **Ví thấp** (Company wallets dưới ngưỡng):

```
GET /api/admin/reports/low-wallets?threshold=200000&limit=20
```

---

## 9) Gợi ý frontend

- **Overview cards:** usersTotal, companiesTotal, driversTotal, walletsTotalBalance, txCount, txAmount.
- **Bar/line chart:** `/tx-daily` → Amount & Count theo ngày.
- **Tables:** Top companies/drivers.
- **Alert list:** ví thấp (low-wallets) với nút nạp tiền.

---

## 10) Bảo mật & mở rộng

- Thêm `[Authorize(Policy = "AdminOnly")]` hoặc roles claim cho controller.
- Bộ lọc thêm `status=Completed` cho aggregation nếu bạn muốn loại `Failed`.
- Có thể thêm endpoint `GET /payments/summary-by-type` nếu cần tách chi tiết hơn (Salary/Topup/Membership).
- Nếu cần realtime cho dashboard, bắn SignalR event khi tạo `Transaction` mới để client gọi refetch.



---

## 11) Realtime auto‑refresh cho Admin Dashboards (SignalR)

Tự động cập nhật dashboard khi phát sinh **Transaction** mới, tái sử dụng SignalR stack đang có.

### 11.1) Application – Realtime interface

**Path:** `src/MyCabs.Application/Realtime/IAdminRealtime.cs`

```csharp
namespace MyCabs.Application.Realtime;

using MyCabs.Application.DTOs;

public interface IAdminRealtime
{
    Task TxCreatedAsync(TransactionDto dto);
}
```

### 11.2) API – Hub & Notifier

**A. Hub**\
**Path:** `src/MyCabs.Api/Hubs/AdminHub.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MyCabs.Api.Hubs;

[Authorize(Roles = "Admin")] // CHANGED: admin-only hub
public class AdminHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value
                   ?? Context.User?.FindFirst("role")?.Value;
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            await Groups.AddToGroupAsync(Context.ConnectionId, "admins");

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value
                   ?? Context.User?.FindFirst("role")?.Value;
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "admins");

        await base.OnDisconnectedAsync(exception);
    }
}
```

**B. Notifier**\
**Path:** `src/MyCabs.Api/Realtime/AdminHubNotifier.cs`

```csharp
using Microsoft.AspNetCore.SignalR;
using MyCabs.Api.Hubs;
using MyCabs.Application.DTOs;
using MyCabs.Application.Realtime;

namespace MyCabs.Api.Realtime;

public class AdminHubNotifier : IAdminRealtime
{
    private readonly IHubContext<AdminHub> _hub;
    public AdminHubNotifier(IHubContext<AdminHub> hub) { _hub = hub; }

    public Task TxCreatedAsync(TransactionDto dto)
        => _hub.Clients.Group("admins").SendAsync("admin:tx:new", dto);
}
```

### 11.3) Program.cs – Đăng ký DI & map hub

**Path:** `src/MyCabs.Api/Program.cs`

```csharp
// using MyCabs.Application.Realtime; // add
// using MyCabs.Api.Realtime;         // add
// using MyCabs.Api.Hubs;             // add

// ... DI (sau phần Services/AddSignalR và các repo/service khác)
builder.Services.AddSingleton<IAdminRealtime, AdminHubNotifier>();

// ... Endpoints
app.MapHub<AdminHub>("/hubs/admin");
```

> Lưu ý: Bạn đã có `builder.Services.AddSignalR();` ở trên cho Notifications – giữ nguyên.

### 11.4) Application – Hook trong FinanceService

Gọi realtime sau khi tạo **Transaction** để dashboard refetch ngay.

**Path:** `src/MyCabs.Application/Services/FinanceService.cs`

- Thêm `using MyCabs.Application.Realtime;`
- Bổ sung dependency và gọi notifier ở 3 luồng: TopUp, PaySalary, PayMembership

```csharp
using MyCabs.Application.Realtime; // <— add

public class FinanceService : IFinanceService
{
    private readonly IWalletRepository _wallets;
    private readonly ITransactionRepository _txs;
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;
    private readonly IConfiguration _cfg;
    private readonly INotificationService _notif;
    private readonly IAdminRealtime _adminRt;          // <— add

    public FinanceService(
        IWalletRepository wallets,
        ITransactionRepository txs,
        IDriverRepository drivers,
        ICompanyRepository companies,
        IConfiguration cfg,
        INotificationService notif,
        IAdminRealtime adminRt)                          // <— add
    {
        _wallets = wallets; _txs = txs; _drivers = drivers; _companies = companies;
        _cfg = cfg; _notif = notif; _adminRt = adminRt;   // <— add
    }

    // ... (giữ nguyên các helper Map(...))

    public async Task<bool> TopUpCompanyAsync(string companyId, TopUpDto dto)
    {
        var w = await _wallets.GetOrCreateAsync("Company", companyId);
        await _wallets.CreditAsync(w.Id.ToString(), dto.Amount);
        var tx = new Transaction
        {
            Id = ObjectId.GenerateNewId(),
            Type = "Topup",
            Status = "Completed",
            Amount = dto.Amount,
            FromWalletId = null,
            ToWalletId = w.Id,
            CompanyId = ObjectId.Parse(companyId),
            DriverId = null,
            Note = dto.Note,
            CreatedAt = DateTime.UtcNow
        };
        await _txs.CreateAsync(tx);
        await _adminRt.TxCreatedAsync(Map(tx));           // <— push realtime
        return true;
    }

    public async Task<(bool ok, string? err)> PaySalaryAsync(string companyId, PaySalaryDto dto)
    {
        var compW = await _wallets.GetOrCreateAsync("Company", companyId);
        var driver = await _drivers.GetByUserIdAsync(dto.DriverId) ?? throw new InvalidOperationException("DRIVER_NOT_FOUND");
        var drvW = await _wallets.GetOrCreateAsync("Driver", driver.Id.ToString());
        var debited = await _wallets.TryDebitAsync(compW.Id.ToString(), dto.Amount);
        var tx = new Transaction
        {
            Id = ObjectId.GenerateNewId(),
            Type = "Salary",
            Status = debited ? "Completed" : "Failed",
            Amount = dto.Amount,
            FromWalletId = compW.Id,
            ToWalletId = drvW.Id,
            CompanyId = ObjectId.Parse(companyId),
            DriverId = driver.Id,
            Note = dto.Note,
            CreatedAt = DateTime.UtcNow
        };
        await _txs.CreateAsync(tx);
        await _adminRt.TxCreatedAsync(Map(tx));           // <— push realtime
        if (!debited) return (false, "INSUFFICIENT_FUNDS");

        await _wallets.CreditAsync(drvW.Id.ToString(), dto.Amount);
        var company = await _companies.GetByIdAsync(companyId);
        var freshWallet = await _wallets.GetOrCreateAsync("Company", companyId);
        if (company != null)
            await MaybeNotifyLowBalanceAsync(company.OwnerUserId.ToString(), freshWallet);
        return (true, null);
    }

    public async Task<(bool ok, string? err)> PayMembershipAsync(string companyId, PayMembershipDto dto)
    {
        var compW = await _wallets.GetOrCreateAsync("Company", companyId);
        var debited = dto.Amount <= 0 ? true : await _wallets.TryDebitAsync(compW.Id.ToString(), dto.Amount);
        var tx = new Transaction
        {
            Id = ObjectId.GenerateNewId(),
            Type = "Membership",
            Status = debited ? "Completed" : "Failed",
            Amount = dto.Amount,
            FromWalletId = compW.Id,
            ToWalletId = null,
            CompanyId = ObjectId.Parse(companyId),
            DriverId = null,
            Note = dto.Note ?? $"Plan={dto.Plan}; Cycle={dto.BillingCycle}",
            CreatedAt = DateTime.UtcNow
        };
        await _txs.CreateAsync(tx);
        await _adminRt.TxCreatedAsync(Map(tx));           // <— push realtime
        if (!debited) return (false, "INSUFFICIENT_FUNDS");

        var expires = DateTime.UtcNow.AddMonths(dto.BillingCycle == "quarterly" ? 3 : 1);
        await _companies.UpdateMembershipAsync(companyId, new MembershipInfo
        {
            Plan = dto.Plan,
            BillingCycle = dto.BillingCycle,
            ExpiresAt = expires
        });

        var company = await _companies.GetByIdAsync(companyId);
        var freshWallet = await _wallets.GetOrCreateAsync("Company", companyId);
        if (company != null)
            await MaybeNotifyLowBalanceAsync(company.OwnerUserId.ToString(), freshWallet);
        return (true, null);
    }
}
```

### 11.5) Frontend gợi ý (admin): subscribe và refetch

```ts
import * as signalR from "@microsoft/signalr";

const conn = new signalR.HubConnectionBuilder()
  .withUrl("/hubs/admin", { accessTokenFactory: () => accessToken })
  .withAutomaticReconnect()
  .build();

conn.on("admin:tx:new", (tx) => {
  // tx: TransactionDto — gọi lại APIs / cập nhật chart, bảng
  refetchAll();
});

await conn.start();
```

**Test nhanh:** chạy 1 request TopUp/Salary/Membership → mở console frontend, thấy event `admin:tx:new` bắn về.



---

## 11.6) Bổ sung JWT Bearer cho hub `/hubs/admin`

Trong `Program.cs`, mở rộng hook nhận token từ query cho **SignalR Admin Hub** (ngoài Notifications đã có) **và đảm bảo map đúng role claim**:

```csharp
using System.Security.Claims; // NEW
using Microsoft.IdentityModel.Tokens;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!)),
            RoleClaimType = ClaimTypes.Role // NEW: để [Authorize(Roles="Admin")] hoạt động khi token dùng claim role chuẩn
            // Nếu token của bạn dùng key "role" thuần: thay bằng RoleClaimType = "role"
        };

        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"].ToString();
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/hubs/notifications") || path.StartsWithSegments("/hubs/admin")))
                {
                    ctx.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
```

> **Ghi chú:** Đảm bảo JWT chứa claim vai trò là **Admin**. Ví dụ lúc phát token, thêm claim `new Claim(ClaimTypes.Role, "Admin")`. `/hubs/admin`

Trong `Program.cs`, mở rộng hook nhận token từ query cho **SignalR Admin Hub** (ngoài Notifications đã có):

```csharp
// ... bên trong AddJwtBearer(...)
OnMessageReceived = ctx =>
{
    var accessToken = ctx.Request.Query["access_token"].ToString();
    var path = ctx.HttpContext.Request.Path;
    if (!string.IsNullOrEmpty(accessToken) &&
        (path.StartsWithSegments("/hubs/notifications") || path.StartsWithSegments("/hubs/admin")))
    {
        ctx.Token = accessToken;
    }
    return Task.CompletedTask;
}
```

> Việc này cho phép client SignalR truyền `?access_token=...` khi kết nối tới `/hubs/admin`.



---

## 12) JWT Token – thêm role claim (Admin) & khớp interface `Application.IJwtTokenService`

**Path:** `src/MyCabs.Api/Jwt/JwtTokenService.cs`\
**Status:** CHANGED – class `JwtTokenService` **implement** `MyCabs.Application.IJwtTokenService` và method **phải tên **`` (khớp `Application/IJwtTokenService.cs`). Đồng thời **không** khai báo interface `IJwtTokenService` ở API để tránh mơ hồ namespace.

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MyCabs.Domain.Entities;

namespace MyCabs.Api.Jwt;

public class JwtTokenService : MyCabs.Application.IJwtTokenService
{
    private readonly IConfiguration _cfg;
    public JwtTokenService(IConfiguration cfg) { _cfg = cfg; }

    // CHANGED: đúng chữ ký interface ở Application
    public string Generate(User user)
    {
        var issuer   = _cfg["Jwt:Issuer"]   ?? "mycabs.local";
        var audience = _cfg["Jwt:Audience"] ?? "mycabs.local";
        var key      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var creds    = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var minutes  = int.TryParse(_cfg["Jwt:AccessTokenMinutes"], out var m) ? m : 120;
        var expires  = DateTime.UtcNow.AddMinutes(minutes);

        var role = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,  user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email,user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString()),
            // NEW: role claim để [Authorize(Roles="Admin")] hoạt động
            new(ClaimTypes.Role, role),
            new("role", role) // mirror nếu client đọc key "role"
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

**Ghi chú:**

- Nếu từng có file interface `src/MyCabs.Api/Jwt/IJwtTokenService.cs` thì **xoá** để tránh trùng với `MyCabs.Application.IJwtTokenService`.
- Trong `Program.cs` đã thêm DI: `AddSingleton<MyCabs.Application.IJwtTokenService, MyCabs.Api.Jwt.JwtTokenService>()`.
- `JwtBearer` đã set `RoleClaimType = ClaimTypes.Role` và `OnMessageReceived` hỗ trợ `/hubs/notifications` & `/hubs/admin` (xem mục 11.6).

