# MyCabs – Wallet & Transactions (Mock) **(Standalone)**

> Tài liệu rút gọn, **chỉ còn module Wallet/Transactions** (đã bỏ Company & Driver module khỏi canvas để tiết kiệm dung lượng). Mục tiêu: mô phỏng **top-up**, **trả lương Company→Driver**, **thanh toán membership Company**. Toàn bộ response theo **ApiEnvelope**.

---

## 1) Domain (Entities)

\`\`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Wallet
{
    [BsonId] public ObjectId Id { get; set; }

    [BsonElement("ownerType")] // Company | Driver
    public string OwnerType { get; set; } = "Company";

    [BsonElement("ownerId")]   // Company.Id hoặc Driver.Id
    public ObjectId OwnerId { get; set; }

    [BsonElement("balance")]   // Decimal128 trong Mongo
    public decimal Balance { get; set; } = 0m;

    [BsonElement("lowBalanceThreshold")]
    public decimal LowBalanceThreshold { get; set; } = 100_000m;

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

\`\`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Transaction
{
    [BsonId] public ObjectId Id { get; set; }

    [BsonElement("type")]   // Topup | Salary | Membership
    public string Type { get; set; } = "Topup";

    [BsonElement("status")] // Pending | Completed | Failed
    public string Status { get; set; } = "Pending";

    [BsonElement("amount")] public decimal Amount { get; set; }

    [BsonElement("fromWalletId")] public ObjectId? FromWalletId { get; set; }
    [BsonElement("toWalletId")]   public ObjectId? ToWalletId { get; set; }

    [BsonElement("companyId")] public ObjectId? CompanyId { get; set; }
    [BsonElement("driverId")]  public ObjectId? DriverId { get; set; }

    [BsonElement("note")] public string? Note { get; set; }

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## 2) Domain (Interfaces)

\`\`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IWalletRepository
{
    Task<Wallet> GetOrCreateAsync(string ownerType, string ownerId);
    Task<Wallet?> GetByOwnerAsync(string ownerType, string ownerId);
    Task<bool> TryDebitAsync(string walletId, decimal amount);
    Task CreditAsync(string walletId, decimal amount);
    Task EnsureIndexesAsync();
}
```

\`\`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface ITransactionRepository
{
    Task CreateAsync(Transaction tx);
    Task<(IEnumerable<Transaction> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? type, string? status);
    Task<(IEnumerable<Transaction> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? type, string? status);
    Task EnsureIndexesAsync();
}
```

**Patch Interface Company (thêm 1 method):** `src/MyCabs.Domain/Interfaces/ICompanyRepository.cs`

```csharp
Task UpdateMembershipAsync(string companyId, MembershipInfo info);
```

---

## 3) Infrastructure (Repositories)

\`\`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class WalletRepository : IWalletRepository, IIndexInitializer
{
    private readonly IMongoCollection<Wallet> _col;
    public WalletRepository(IMongoContext ctx) => _col = ctx.GetCollection<Wallet>("wallets");

    public async Task<Wallet> GetOrCreateAsync(string ownerType, string ownerId)
    {
        if (!ObjectId.TryParse(ownerId, out var oid)) throw new ArgumentException("Invalid ownerId");
        var w = await _col.Find(x => x.OwnerType == ownerType && x.OwnerId == oid).FirstOrDefaultAsync();
        if (w != null) return w;
        w = new Wallet { Id = ObjectId.GenerateNewId(), OwnerType = ownerType, OwnerId = oid };
        await _col.InsertOneAsync(w); return w;
    }

    public async Task<Wallet?> GetByOwnerAsync(string ownerType, string ownerId)
    {
        if (!ObjectId.TryParse(ownerId, out var oid)) return null;
        return await _col.Find(x => x.OwnerType == ownerType && x.OwnerId == oid).FirstOrDefaultAsync();
    }

    public async Task<bool> TryDebitAsync(string walletId, decimal amount)
    {
        if (!ObjectId.TryParse(walletId, out var wid)) throw new ArgumentException("Invalid walletId");
        var filter = Builders<Wallet>.Filter.Eq(x => x.Id, wid) & Builders<Wallet>.Filter.Gte(x => x.Balance, amount);
        var update = Builders<Wallet>.Update.Inc(x => x.Balance, -amount).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(filter, update);
        return res.ModifiedCount == 1;
    }

    public async Task CreditAsync(string walletId, decimal amount)
    {
        if (!ObjectId.TryParse(walletId, out var wid)) throw new ArgumentException("Invalid walletId");
        var update = Builders<Wallet>.Update.Inc(x => x.Balance, amount).Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _col.UpdateOneAsync(x => x.Id == wid, update);
    }

    public async Task EnsureIndexesAsync()
    {
        var uniqueOwner = new CreateIndexModel<Wallet>(
            Builders<Wallet>.IndexKeys.Ascending(x => x.OwnerType).Ascending(x => x.OwnerId),
            new CreateIndexOptions { Unique = true }
        );
        var byUpdated = new CreateIndexModel<Wallet>(Builders<Wallet>.IndexKeys.Descending(x => x.UpdatedAt));
        await _col.Indexes.CreateManyAsync(new[] { uniqueOwner, byUpdated });
    }
}
```

\`\`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class TransactionRepository : ITransactionRepository, IIndexInitializer
{
    private readonly IMongoCollection<Transaction> _col;
    public TransactionRepository(IMongoContext ctx) => _col = ctx.GetCollection<Transaction>("transactions");

    public Task CreateAsync(Transaction tx) => _col.InsertOneAsync(tx);

    public async Task<(IEnumerable<Transaction> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? type, string? status)
    {
        if (!ObjectId.TryParse(companyId, out var cid)) return (Enumerable.Empty<Transaction>(), 0);
        var f = Builders<Transaction>.Filter.Eq(x => x.CompanyId, cid);
        if (!string.IsNullOrWhiteSpace(type)) f &= Builders<Transaction>.Filter.Eq(x => x.Type, type);
        if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Transaction>.Filter.Eq(x => x.Status, status);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task<(IEnumerable<Transaction> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? type, string? status)
    {
        if (!ObjectId.TryParse(driverId, out var did)) return (Enumerable.Empty<Transaction>(), 0);
        var f = Builders<Transaction>.Filter.Eq(x => x.DriverId, did);
        if (!string.IsNullOrWhiteSpace(type)) f &= Builders<Transaction>.Filter.Eq(x => x.Type, type);
        if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Transaction>.Filter.Eq(x => x.Status, status);
        var total = await _col.CountDocumentsAsync(f);
        var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
        return (items, total);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Descending(x => x.CreatedAt));
        var ix2 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Ascending(x => x.CompanyId));
        var ix3 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Ascending(x => x.DriverId));
        var ix4 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Ascending(x => x.Type));
        var ix5 = new CreateIndexModel<Transaction>(Builders<Transaction>.IndexKeys.Ascending(x => x.Status));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2, ix3, ix4, ix5 });
    }
}
```

**Patch CompanyRepository** (thêm implement): `src/MyCabs.Infrastructure/Repositories/CompanyRepository.cs`

```csharp
public Task UpdateMembershipAsync(string companyId, MembershipInfo info)
{
    if (!ObjectId.TryParse(companyId, out var oid)) throw new ArgumentException("Invalid companyId");
    var update = Builders<Company>.Update.Set(x => x.Membership, info).Set(x => x.UpdatedAt, DateTime.UtcNow);
    return _col.UpdateOneAsync(x => x.Id == oid, update);
}
```

---

## 4) Application (DTOs & Validators)

\`\`

```csharp
namespace MyCabs.Application.DTOs;

public record TopUpDto(decimal Amount, string? Note);
// DriverId = _id của USER role=Driver (dùng để tìm Driver profile)
public record PaySalaryDto(string DriverId, decimal Amount, string? Note);
public record PayMembershipDto(string Plan, string BillingCycle, decimal Amount, string? Note);
public record TransactionsQuery(int Page = 1, int PageSize = 10, string? Type = null, string? Status = null);

public record WalletDto(string Id, string OwnerType, string OwnerId, decimal Balance, decimal LowBalanceThreshold);
public record TransactionDto(string Id, string Type, string Status, decimal Amount,
    string? FromWalletId, string? ToWalletId, string? CompanyId, string? DriverId,
    string? Note, DateTime CreatedAt);
```

\`\`

```csharp
using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class TopUpDtoValidator : AbstractValidator<TopUpDto>
{ public TopUpDtoValidator() { RuleFor(x => x.Amount).GreaterThan(0); } }

public class PaySalaryDtoValidator : AbstractValidator<PaySalaryDto>
{
    public PaySalaryDtoValidator()
    { RuleFor(x => x.DriverId).NotEmpty(); RuleFor(x => x.Amount).GreaterThan(0); }
}

public class PayMembershipDtoValidator : AbstractValidator<PayMembershipDto>
{
    public PayMembershipDtoValidator()
    {
        RuleFor(x => x.Plan).NotEmpty().Must(p => new[] { "Free", "Basic", "Premium" }.Contains(p));
        RuleFor(x => x.BillingCycle).NotEmpty().Must(c => c is "monthly" or "quarterly");
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
    }
}
```

---

## 5) Application (Service)

\`\`

```csharp
using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IFinanceService
{
    Task<WalletDto> GetCompanyWalletAsync(string companyId);
    Task<(IEnumerable<TransactionDto> Items, long Total)> GetCompanyTransactionsAsync(string companyId, TransactionsQuery q);
    Task<WalletDto> GetDriverWalletAsync(string driverId);
    Task<(IEnumerable<TransactionDto> Items, long Total)> GetDriverTransactionsAsync(string driverId, TransactionsQuery q);
    Task<bool> TopUpCompanyAsync(string companyId, TopUpDto dto);
    Task<(bool ok, string? err)> PaySalaryAsync(string companyId, PaySalaryDto dto);
    Task<(bool ok, string? err)> PayMembershipAsync(string companyId, PayMembershipDto dto);
}

public class FinanceService : IFinanceService
{
    private readonly IWalletRepository _wallets;
    private readonly ITransactionRepository _txs;
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;

    public FinanceService(IWalletRepository wallets, ITransactionRepository txs, IDriverRepository drivers, ICompanyRepository companies)
    { _wallets = wallets; _txs = txs; _drivers = drivers; _companies = companies; }

    static WalletDto Map(Wallet w) => new(w.Id.ToString(), w.OwnerType, w.OwnerId.ToString(), w.Balance, w.LowBalanceThreshold);
    static TransactionDto Map(Transaction t) => new(
        t.Id.ToString(), t.Type, t.Status, t.Amount,
        t.FromWalletId?.ToString(), t.ToWalletId?.ToString(), t.CompanyId?.ToString(), t.DriverId?.ToString(),
        t.Note, t.CreatedAt
    );

    public async Task<WalletDto> GetCompanyWalletAsync(string companyId) => Map(await _wallets.GetOrCreateAsync("Company", companyId));
    public async Task<(IEnumerable<TransactionDto> Items, long Total)> GetCompanyTransactionsAsync(string companyId, TransactionsQuery q)
    { var (items,total)=await _txs.FindForCompanyAsync(companyId,q.Page,q.PageSize,q.Type,q.Status); return (items.Select(Map), total); }

    public async Task<WalletDto> GetDriverWalletAsync(string driverId) => Map(await _wallets.GetOrCreateAsync("Driver", driverId));
    public async Task<(IEnumerable<TransactionDto> Items, long Total)> GetDriverTransactionsAsync(string driverId, TransactionsQuery q)
    { var (items,total)=await _txs.FindForDriverAsync(driverId,q.Page,q.PageSize,q.Type,q.Status); return (items.Select(Map), total); }

    public async Task<bool> TopUpCompanyAsync(string companyId, TopUpDto dto)
    {
        var w = await _wallets.GetOrCreateAsync("Company", companyId);
        await _wallets.CreditAsync(w.Id.ToString(), dto.Amount);
        await _txs.CreateAsync(new Transaction {
            Id = ObjectId.GenerateNewId(), Type = "Topup", Status = "Completed", Amount = dto.Amount,
            FromWalletId = null, ToWalletId = w.Id, CompanyId = ObjectId.Parse(companyId), DriverId = null,
            Note = dto.Note, CreatedAt = DateTime.UtcNow
        });
        return true;
    }

    public async Task<(bool ok, string? err)> PaySalaryAsync(string companyId, PaySalaryDto dto)
    {
        var compW = await _wallets.GetOrCreateAsync("Company", companyId);
        var driver = await _drivers.GetByUserIdAsync(dto.DriverId) ?? throw new InvalidOperationException("DRIVER_NOT_FOUND");
        var drvW = await _wallets.GetOrCreateAsync("Driver", driver.Id.ToString());
        var debited = await _wallets.TryDebitAsync(compW.Id.ToString(), dto.Amount);
        var tx = new Transaction {
            Id = ObjectId.GenerateNewId(), Type = "Salary", Status = debited?"Completed":"Failed", Amount = dto.Amount,
            FromWalletId = compW.Id, ToWalletId = drvW.Id, CompanyId = ObjectId.Parse(companyId), DriverId = driver.Id,
            Note = dto.Note, CreatedAt = DateTime.UtcNow
        };
        if (!debited) { await _txs.CreateAsync(tx); return (false, "INSUFFICIENT_FUNDS"); }
        await _wallets.CreditAsync(drvW.Id.ToString(), dto.Amount);
        await _txs.CreateAsync(tx);
        return (true, null);
    }

    public async Task<(bool ok, string? err)> PayMembershipAsync(string companyId, PayMembershipDto dto)
    {
        var compW = await _wallets.GetOrCreateAsync("Company", companyId);
        var debited = dto.Amount <= 0 ? true : await _wallets.TryDebitAsync(compW.Id.ToString(), dto.Amount);
        await _txs.CreateAsync(new Transaction {
            Id = ObjectId.GenerateNewId(), Type = "Membership", Status = debited?"Completed":"Failed", Amount = dto.Amount,
            FromWalletId = compW.Id, ToWalletId = null, CompanyId = ObjectId.Parse(companyId), DriverId = null,
            Note = dto.Note ?? $"Plan={dto.Plan}; Cycle={dto.BillingCycle}", CreatedAt = DateTime.UtcNow
        });
        if (!debited) return (false, "INSUFFICIENT_FUNDS");
        var expires = DateTime.UtcNow.AddMonths(dto.BillingCycle == "quarterly" ? 3 : 1);
        await _companies.UpdateMembershipAsync(companyId, new MembershipInfo { Plan = dto.Plan, BillingCycle = dto.BillingCycle, ExpiresAt = expires });
        return (true, null);
    }
}
```

---

## 6) API (Controllers – patch)

### CompaniesController (thêm trường & action)

\`\`

```csharp
using MyCabs.Application.Services; // IFinanceService
using MyCabs.Application.DTOs;      // TopUpDto, PaySalaryDto, PayMembershipDto, TransactionsQuery

// trong class CompaniesController
private readonly IFinanceService _finance;
public CompaniesController(ICompanyService svc, IFinanceService finance) { _svc = svc; _finance = finance; }

[Authorize(Roles = "Company,Admin")]
[HttpGet("{id}/wallet")] public async Task<IActionResult> GetWallet(string id)
 => Ok(ApiEnvelope.Ok(HttpContext, await _finance.GetCompanyWalletAsync(id)));

[Authorize(Roles = "Company,Admin")]
[HttpGet("{id}/transactions")] public async Task<IActionResult> GetTransactions(string id, [FromQuery] TransactionsQuery q)
{ var (items,total)=await _finance.GetCompanyTransactionsAsync(id,q); return Ok(ApiEnvelope.Ok(HttpContext,new PagedResult<TransactionDto>(items,q.Page,q.PageSize,total))); }

[Authorize(Roles = "Company,Admin")]
[HttpPost("{id}/wallet/topup")] public async Task<IActionResult> TopUp(string id,[FromBody]TopUpDto dto)
{ await _finance.TopUpCompanyAsync(id,dto); return Ok(ApiEnvelope.Ok(HttpContext,new{message="Topup completed"})); }

[Authorize(Roles = "Company,Admin")]
[HttpPost("{id}/pay-salary")] public async Task<IActionResult> PaySalary(string id,[FromBody]PaySalaryDto dto)
{ var (ok,err)=await _finance.PaySalaryAsync(id,dto); if(!ok&&err=="INSUFFICIENT_FUNDS") return BadRequest(ApiEnvelope.Fail(HttpContext,"INSUFFICIENT_FUNDS","Company wallet has insufficient funds",400)); return Ok(ApiEnvelope.Ok(HttpContext,new{message="Salary paid"})); }

[Authorize(Roles = "Company,Admin")]
[HttpPost("{id}/membership/pay")] public async Task<IActionResult> PayMembership(string id,[FromBody]PayMembershipDto dto)
{ var (ok,err)=await _finance.PayMembershipAsync(id,dto); if(!ok&&err=="INSUFFICIENT_FUNDS") return BadRequest(ApiEnvelope.Fail(HttpContext,"INSUFFICIENT_FUNDS","Company wallet has insufficient funds",400)); return Ok(ApiEnvelope.Ok(HttpContext,new{message="Membership updated"})); }
```

### DriversController (ví & lịch sử của driver đăng nhập)

\`\`

```csharp
using System.Security.Claims;
using MyCabs.Application.Services; // IFinanceService
using MyCabs.Application.DTOs;      // TransactionsQuery
using MyCabs.Domain.Interfaces;     // IDriverRepository

// trong class DriversController
private readonly IDriverRepository _drivers;
public DriversController(IDriverService svc, IDriverRepository drivers) { _svc = svc; _drivers = drivers; }

[Authorize(Roles = "Driver")]
[HttpGet("me/wallet")] public async Task<IActionResult> MyWallet([FromServices] IFinanceService finance)
{
    var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
    var d = await _drivers.GetByUserIdAsync(uid);
    if (d is null) return NotFound(ApiEnvelope.Fail(HttpContext,"DRIVER_NOT_FOUND","Driver not found",404));
    return Ok(ApiEnvelope.Ok(HttpContext, await finance.GetDriverWalletAsync(d.Id.ToString())));
}

[Authorize(Roles = "Driver")]
[HttpGet("me/transactions")] public async Task<IActionResult> MyTransactions([FromQuery] TransactionsQuery q, [FromServices] IFinanceService finance)
{
    var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
    var d = await _drivers.GetByUserIdAsync(uid);
    if (d is null) return NotFound(ApiEnvelope.Fail(HttpContext,"DRIVER_NOT_FOUND","Driver not found",404));
    var (items,total)=await finance.GetDriverTransactionsAsync(d.Id.ToString(),q);
    return Ok(ApiEnvelope.Ok(HttpContext,new PagedResult<TransactionDto>(items,q.Page,q.PageSize,total)));
}
```

---

## 7) DI & DbInitializer (Program.cs)

\`\`\*\* (thêm DI)\*\*

```csharp
using MyCabs.Domain.Interfaces;           // IWalletRepository, ITransactionRepository
using MyCabs.Infrastructure.Repositories; // WalletRepository, TransactionRepository
using MyCabs.Application.Services;        // IFinanceService, FinanceService
using MyCabs.Infrastructure.Startup;      // IIndexInitializer

builder.Services.AddScoped<IWalletRepository, WalletRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IFinanceService, FinanceService>();

builder.Services.AddScoped<IIndexInitializer, WalletRepository>();
builder.Services.AddScoped<IIndexInitializer, TransactionRepository>();
```

---

## 8) Test nhanh (Postman/Swagger)

- **Top up ví Company**

```
POST /api/companies/{companyId}/wallet/topup
{ "amount": 500000, "note": "mock momo" }
```

- **Pay membership**

```
POST /api/companies/{companyId}/membership/pay
{ "plan": "Basic", "billingCycle": "monthly", "amount": 200000, "note": "mock" }
```

- **Pay salary** (DriverId = \_id của **USER** role Driver)

```
POST /api/companies/{companyId}/pay-salary
{ "driverId": "68a1...userId...", "amount": 150000, "note": "Aug-2025" }
```

- **Xem ví/transactions**

```
GET /api/companies/{id}/wallet
GET /api/companies/{id}/transactions?page=1&pageSize=10&type=Salary&status=Completed
GET /api/drivers/me/wallet        (Role=Driver)
GET /api/drivers/me/transactions  (Role=Driver)
```

---

## 9) Seed mẫu (mongosh)

```js
// Ví Company
const companyId = ObjectId("<CompanyId>");
db.wallets.updateOne(
  { ownerType: "Company", ownerId: companyId },
  { $setOnInsert: { balance: 0, lowBalanceThreshold: 100000, createdAt: new Date(), updatedAt: new Date() } },
  { upsert: true }
);

// Ví Driver
const driverId = ObjectId("<DriverId>");
db.wallets.updateOne(
  { ownerType: "Driver", ownerId: driverId },
  { $setOnInsert: { balance: 0, lowBalanceThreshold: 50000, createdAt: new Date(), updatedAt: new Date() } },
  { upsert: true }
);
```

---

### Ghi chú

- MVP **không** dùng multi-document transaction; `Salary` thực hiện `TryDebit` → `Credit` → log `Transaction`. Nếu cần ACID, bật replica set local và dùng session.
- Số tiền có thể lưu `long` theo **đồng** nếu muốn tuyệt đối an toàn về tiền tệ.



---

# Hiring Loop (Company ↔ Driver)

> Bổ sung luồng tuyển dụng: Company xem/duyệt **applications**, gửi **invitations**; Driver xem danh sách của mình. Tối ưu để copy–paste ngắn gọn, tái dùng repo có sẵn.

## 1) Patch Domain (optional – thêm `note` cho Invitation)

`` (thêm thuộc tính – không phá vỡ dữ liệu cũ)

```csharp
[BsonElement("note")]
public string? Note { get; set; }
```

## 2) DTOs

``

```csharp
namespace MyCabs.Application.DTOs;

public record InviteDriverDto(string DriverUserId, string? Note);
public record ApplicationsQuery(int Page = 1, int PageSize = 10, string? Status = null);
public record InvitationsQuery(int Page = 1, int PageSize = 10, string? Status = null);

public record ApplicationDto(string Id, string DriverId, string CompanyId, string Status, DateTime CreatedAt);
public record InvitationDto(string Id, string CompanyId, string DriverId, string Status, DateTime CreatedAt, string? Note);
```

## 3) Repository patches

### 3.1 `ApplicationRepository`

**Interface** `src/MyCabs.Domain/Interfaces/IApplicationRepository.cs`

```csharp
Task<Application?> GetByIdAsync(string appId);
Task UpdateStatusAsync(string appId, string status);
Task<(IEnumerable<Application> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? status);
Task<(IEnumerable<Application> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? status);
```

**Implement** `src/MyCabs.Infrastructure/Repositories/ApplicationRepository.cs`

```csharp
public async Task<Application?> GetByIdAsync(string appId)
{
    if (!ObjectId.TryParse(appId, out var id)) return null;
    return await _col.Find(x => x.Id == id).FirstOrDefaultAsync();
}

public Task UpdateStatusAsync(string appId, string status)
{
    if (!ObjectId.TryParse(appId, out var id)) throw new ArgumentException("Invalid appId");
    var update = Builders<Application>.Update.Set(x => x.Status, status);
    return _col.UpdateOneAsync(x => x.Id == id, update);
}

public async Task<(IEnumerable<Application> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? status)
{
    if (!ObjectId.TryParse(companyId, out var cid)) return (Enumerable.Empty<Application>(), 0);
    var f = Builders<Application>.Filter.Eq(x => x.CompanyId, cid);
    if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Application>.Filter.Eq(x => x.Status, status);
    var total = await _col.CountDocumentsAsync(f);
    var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
    return (items, total);
}

public async Task<(IEnumerable<Application> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? status)
{
    if (!ObjectId.TryParse(driverId, out var did)) return (Enumerable.Empty<Application>(), 0);
    var f = Builders<Application>.Filter.Eq(x => x.DriverId, did);
    if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Application>.Filter.Eq(x => x.Status, status);
    var total = await _col.CountDocumentsAsync(f);
    var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
    return (items, total);
}
```

### 3.2 `InvitationRepository`

**Interface** `src/MyCabs.Domain/Interfaces/IInvitationRepository.cs`

```csharp
Task CreateAsync(string companyId, string driverId, string? note);
Task<(IEnumerable<Invitation> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? status);
Task<(IEnumerable<Invitation> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? status);
```

**Implement** `src/MyCabs.Infrastructure/Repositories/InvitationRepository.cs`

```csharp
public async Task CreateAsync(string companyId, string driverId, string? note)
{
    if (!ObjectId.TryParse(companyId, out var cid)) throw new ArgumentException("Invalid companyId");
    if (!ObjectId.TryParse(driverId, out var did)) throw new ArgumentException("Invalid driverId");
    var inv = new Invitation { Id = ObjectId.GenerateNewId(), CompanyId = cid, DriverId = did, Status = "Pending", CreatedAt = DateTime.UtcNow, Note = note };
    await _col.InsertOneAsync(inv);
}

public async Task<(IEnumerable<Invitation> Items, long Total)> FindForCompanyAsync(string companyId, int page, int pageSize, string? status)
{
    if (!ObjectId.TryParse(companyId, out var cid)) return (Enumerable.Empty<Invitation>(), 0);
    var f = Builders<Invitation>.Filter.Eq(x => x.CompanyId, cid);
    if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Invitation>.Filter.Eq(x => x.Status, status);
    var total = await _col.CountDocumentsAsync(f);
    var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
    return (items, total);
}

public async Task<(IEnumerable<Invitation> Items, long Total)> FindForDriverAsync(string driverId, int page, int pageSize, string? status)
{
    if (!ObjectId.TryParse(driverId, out var did)) return (Enumerable.Empty<Invitation>(), 0);
    var f = Builders<Invitation>.Filter.Eq(x => x.DriverId, did);
    if (!string.IsNullOrWhiteSpace(status)) f &= Builders<Invitation>.Filter.Eq(x => x.Status, status);
    var total = await _col.CountDocumentsAsync(f);
    var items = await _col.Find(f).SortByDescending(x => x.CreatedAt).Skip((page-1)*pageSize).Limit(pageSize).ToListAsync();
    return (items, total);
}
```

## 4) Service

``

```csharp
using MongoDB.Bson;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IHiringService
{
    Task<(IEnumerable<ApplicationDto> Items,long Total)> GetCompanyApplicationsAsync(string companyId, ApplicationsQuery q);
    Task ApproveApplicationAsync(string companyId, string appId);
    Task RejectApplicationAsync(string companyId, string appId);
    Task InviteDriverAsync(string companyId, InviteDriverDto dto);
    Task<(IEnumerable<InvitationDto> Items,long Total)> GetCompanyInvitationsAsync(string companyId, InvitationsQuery q);

    Task<(IEnumerable<ApplicationDto> Items,long Total)> GetMyApplicationsAsync(string userId, ApplicationsQuery q);
    Task<(IEnumerable<InvitationDto> Items,long Total)> GetMyInvitationsAsync(string userId, InvitationsQuery q);
}

public class HiringService : IHiringService
{
    private readonly IApplicationRepository _apps;
    private readonly IInvitationRepository _invites;
    private readonly IDriverRepository _drivers;

    public HiringService(IApplicationRepository apps, IInvitationRepository invites, IDriverRepository drivers)
    { _apps = apps; _invites = invites; _drivers = drivers; }

    static ApplicationDto MapApp(MyCabs.Domain.Entities.Application a)
        => new(a.Id.ToString(), a.DriverId.ToString(), a.CompanyId.ToString(), a.Status, a.CreatedAt);
    static InvitationDto MapInv(MyCabs.Domain.Entities.Invitation i)
        => new(i.Id.ToString(), i.CompanyId.ToString(), i.DriverId.ToString(), i.Status, i.CreatedAt, i.Note);

    public async Task<(IEnumerable<ApplicationDto> Items, long Total)> GetCompanyApplicationsAsync(string companyId, ApplicationsQuery q)
    { var (items,total)=await _apps.FindForCompanyAsync(companyId,q.Page,q.PageSize,q.Status); return (items.Select(MapApp), total); }

    public async Task ApproveApplicationAsync(string companyId, string appId)
    {
        var app = await _apps.GetByIdAsync(appId) ?? throw new InvalidOperationException("APPLICATION_NOT_FOUND");
        if (app.CompanyId.ToString()!=companyId) throw new InvalidOperationException("FORBIDDEN");
        await _apps.UpdateStatusAsync(appId, "Approved");
        await _drivers.SetCompanyAsync(app.DriverId.ToString(), companyId);
    }

    public async Task RejectApplicationAsync(string companyId, string appId)
    {
        var app = await _apps.GetByIdAsync(appId) ?? throw new InvalidOperationException("APPLICATION_NOT_FOUND");
        if (app.CompanyId.ToString()!=companyId) throw new InvalidOperationException("FORBIDDEN");
        await _apps.UpdateStatusAsync(appId, "Rejected");
    }

    public async Task InviteDriverAsync(string companyId, InviteDriverDto dto)
    {
        // từ userId → driver profile
        var driver = await _drivers.GetByUserIdAsync(dto.DriverUserId) ?? throw new InvalidOperationException("DRIVER_NOT_FOUND");
        await _invites.CreateAsync(companyId, driver.Id.ToString(), dto.Note);
    }

    public async Task<(IEnumerable<InvitationDto> Items, long Total)> GetCompanyInvitationsAsync(string companyId, InvitationsQuery q)
    { var (items,total)=await _invites.FindForCompanyAsync(companyId,q.Page,q.PageSize,q.Status); return (items.Select(MapInv), total); }

    public async Task<(IEnumerable<ApplicationDto> Items, long Total)> GetMyApplicationsAsync(string userId, ApplicationsQuery q)
    { var d = await _drivers.GetByUserIdAsync(userId) ?? throw new InvalidOperationException("DRIVER_NOT_FOUND"); var (items,total)=await _apps.FindForDriverAsync(d.Id.ToString(),q.Page,q.PageSize,q.Status); return (items.Select(MapApp), total); }

    public async Task<(IEnumerable<InvitationDto> Items, long Total)> GetMyInvitationsAsync(string userId, InvitationsQuery q)
    { var d = await _drivers.GetByUserIdAsync(userId) ?? throw new InvalidOperationException("DRIVER_NOT_FOUND"); var (items,total)=await _invites.FindForDriverAsync(d.Id.ToString(),q.Page,q.PageSize,q.Status); return (items.Select(MapInv), total); }
}
```

## 5) Controllers (patch ngắn)

### 5.1 CompaniesController

Thêm field/ctor & các action (Role=`Company,Admin`).

```csharp
using MyCabs.Application.Services; // IHiringService
using MyCabs.Application.DTOs;      // HiringDtos

private readonly IHiringService _hiring;
public CompaniesController(ICompanyService svc, IFinanceService finance, IHiringService hiring)
{ _svc = svc; _finance = finance; _hiring = hiring; }

[Authorize(Roles="Company,Admin")]
[HttpGet("{id}/applications")] public async Task<IActionResult> GetApplications(string id, [FromQuery] ApplicationsQuery q)
{ var (items,total)=await _hiring.GetCompanyApplicationsAsync(id,q); return Ok(ApiEnvelope.Ok(HttpContext,new PagedResult<ApplicationDto>(items,q.Page,q.PageSize,total))); }

[Authorize(Roles="Company,Admin")]
[HttpPost("{id}/applications/{appId}/approve")] public async Task<IActionResult> ApproveApp(string id,string appId)
{ try { await _hiring.ApproveApplicationAsync(id,appId); return Ok(ApiEnvelope.Ok(HttpContext,new{message="Approved"})); }
  catch(InvalidOperationException ex) when(ex.Message=="APPLICATION_NOT_FOUND") { return NotFound(ApiEnvelope.Fail(HttpContext,"APPLICATION_NOT_FOUND","Application not found",404)); }
  catch(InvalidOperationException ex) when(ex.Message=="FORBIDDEN") { return Forbid(); } }

[Authorize(Roles="Company,Admin")]
[HttpPost("{id}/applications/{appId}/reject")] public async Task<IActionResult> RejectApp(string id,string appId)
{ try { await _hiring.RejectApplicationAsync(id,appId); return Ok(ApiEnvelope.Ok(HttpContext,new{message="Rejected"})); }
  catch(InvalidOperationException ex) when(ex.Message=="APPLICATION_NOT_FOUND") { return NotFound(ApiEnvelope.Fail(HttpContext,"APPLICATION_NOT_FOUND","Application not found",404)); }
  catch(InvalidOperationException ex) when(ex.Message=="FORBIDDEN") { return Forbid(); } }

[Authorize(Roles="Company,Admin")]
[HttpPost("{id}/invitations")] public async Task<IActionResult> Invite(string id,[FromBody] InviteDriverDto dto)
{ try { await _hiring.InviteDriverAsync(id,dto); return Ok(ApiEnvelope.Ok(HttpContext,new{message="Invitation sent"})); }
  catch(InvalidOperationException ex) when(ex.Message=="DRIVER_NOT_FOUND") { return NotFound(ApiEnvelope.Fail(HttpContext,"DRIVER_NOT_FOUND","Driver not found",404)); } }

[Authorize(Roles="Company,Admin")]
[HttpGet("{id}/invitations")] public async Task<IActionResult> GetInvitations(string id,[FromQuery] InvitationsQuery q)
{ var (items,total)=await _hiring.GetCompanyInvitationsAsync(id,q); return Ok(ApiEnvelope.Ok(HttpContext,new PagedResult<InvitationDto>(items,q.Page,q.PageSize,total))); }
```

### 5.2 DriversController

Thêm 2 api list (Role=`Driver`).

```csharp
using System.Security.Claims;
using MyCabs.Application.Services; // IHiringService
using MyCabs.Application.DTOs;      // ApplicationsQuery, InvitationsQuery

[Authorize(Roles="Driver")]
[HttpGet("me/applications")] public async Task<IActionResult> MyApplications([FromServices] IHiringService hiring,[FromQuery] ApplicationsQuery q)
{
    var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
    try { var (items,total)=await hiring.GetMyApplicationsAsync(uid,q); return Ok(ApiEnvelope.Ok(HttpContext,new PagedResult<ApplicationDto>(items,q.Page,q.PageSize,total))); }
    catch(InvalidOperationException ex) when(ex.Message=="DRIVER_NOT_FOUND") { return NotFound(ApiEnvelope.Fail(HttpContext,"DRIVER_NOT_FOUND","Driver not found",404)); }
}

[Authorize(Roles="Driver")]
[HttpGet("me/invitations")] public async Task<IActionResult> MyInvitations([FromServices] IHiringService hiring,[FromQuery] InvitationsQuery q)
{
    var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
    try { var (items,total)=await hiring.GetMyInvitationsAsync(uid,q); return Ok(ApiEnvelope.Ok(HttpContext,new PagedResult<InvitationDto>(items,q.Page,q.PageSize,total))); }
    catch(InvalidOperationException ex) when(ex.Message=="DRIVER_NOT_FOUND") { return NotFound(ApiEnvelope.Fail(HttpContext,"DRIVER_NOT_FOUND","Driver not found",404)); }
}
```

## 6) DI (Program.cs)

Thêm service vào DI.

```csharp
builder.Services.AddScoped<IHiringService, HiringService>();
```

> Repositories đã có sẵn (`ApplicationRepository`, `InvitationRepository`, `DriverRepository`) — không cần đăng ký mới nếu trước đó đã add. Nếu thiếu, nhớ `AddScoped` và thêm chúng vào danh sách `IIndexInitializer` nếu cần index.

---

### Test nhanh

1. Driver A `POST /api/drivers/apply` → Company X xem `GET /api/companies/{X}/applications`.
2. Company X `POST /api/companies/{X}/applications/{appId}/approve` → Driver A được set `companyId` (qua `DriverRepository.SetCompanyAsync`).
3. Company X `POST /api/companies/{X}/invitations` (body: `{ "driverUserId": "<driver user _id>", "note": "..." }`) → Driver B xem `GET /api/drivers/me/invitations` → `POST /api/drivers/invitations/{inviteId}/respond` (đã có) để Accept/Decline.

