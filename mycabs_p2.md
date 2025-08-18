# MyCabs – Company Module (Add-on)

> Đây là phần **bổ sung mới**, tiếp nối skeleton đã chạy được (Auth + JWT + Mongo + Envelope). Nội dung tập trung vào **Company**: entity, repository, service, controller với các endpoint:
>
> -   `GET /api/companies` — danh sách có search/filter/sort/paginate
> -   `GET /api/companies/{id}` — chi tiết
> -   `POST /api/companies/{id}/services` — thêm dịch vụ (yêu cầu `[Authorize(Roles="Company,Admin")]`)
>
> Đồng thời cập nhật **DI + DbInitializer** để khởi tạo index cho nhiều repository.

---

## 1) Domain (Entities)

**File**: `src/MyCabs.Domain/Entities/Company.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Company
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("ownerUserId")]
    public ObjectId OwnerUserId { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("description")]
    public string? Description { get; set; }

    [BsonElement("address")]
    public string? Address { get; set; }

    [BsonElement("services")]
    public List<CompanyServiceItem> Services { get; set; } = new();

    [BsonElement("membership")]
    public MembershipInfo? Membership { get; set; }

    [BsonElement("walletId")]
    public ObjectId? WalletId { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public class CompanyServiceItem
{
    [BsonElement("serviceId")]
    public string ServiceId { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("type")]
    public string Type { get; set; } = "taxi"; // taxi|xe_om|hang_hoa|tour

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("basePrice")]
    public decimal BasePrice { get; set; }
}

[BsonIgnoreExtraElements]
public class MembershipInfo
{
    [BsonElement("plan")]
    public string Plan { get; set; } = "Free"; // Free|Basic|Premium

    [BsonElement("billingCycle")]
    public string BillingCycle { get; set; } = "monthly"; // monthly|quarterly

    [BsonElement("expiresAt")]
    public DateTime? ExpiresAt { get; set; }
}
```

---

## 2) Domain (Interfaces)

**File**: `src/MyCabs.Domain/Interfaces/ICompanyRepository.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface ICompanyRepository
{
    Task<(IEnumerable<Company> Items, long Total)> FindAsync(
        int page, int pageSize, string? search, string? plan, string? serviceType, string? sort);

    Task<Company?> GetByIdAsync(string id);
    Task AddServiceAsync(string companyId, CompanyServiceItem item);
    Task EnsureIndexesAsync();
}
```

**File (Infrastructure startup contract)**: `src/MyCabs.Infrastructure/Startup/IIndexInitializer.cs`

```csharp
namespace MyCabs.Infrastructure.Startup;

public interface IIndexInitializer
{
    Task EnsureIndexesAsync();
}
```

> `IIndexInitializer` cho phép **DbInitializer** gọi `EnsureIndexesAsync()` trên nhiều repository cùng lúc (users/companies/...).

---

## 3) Infrastructure (Repository)

**File**: `src/MyCabs.Infrastructure/Repositories/CompanyRepository.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class CompanyRepository : ICompanyRepository, IIndexInitializer
{
    private readonly IMongoCollection<Company> _col;
    public CompanyRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<Company>("companies");
    }

    public async Task<(IEnumerable<Company> Items, long Total)> FindAsync(
        int page, int pageSize, string? search, string? plan, string? serviceType, string? sort)
    {
        var filter = Builders<Company>.Filter.Empty;
        var fb = Builders<Company>.Filter;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            filter &= (fb.Regex(x => x.Name, new BsonRegularExpression(s, "i"))
                    | fb.Regex(x => x.Description, new BsonRegularExpression(s, "i")));
        }
        if (!string.IsNullOrWhiteSpace(plan))
            filter &= fb.Eq(x => x.Membership!.Plan, plan);
        if (!string.IsNullOrWhiteSpace(serviceType))
            filter &= fb.ElemMatch(x => x.Services, sv => sv.Type == serviceType);

        // sort: "-createdAt" | "createdAt" | "name" | "-name"
        SortDefinition<Company> sortDef = Builders<Company>.Sort.Descending(x => x.CreatedAt);
        if (!string.IsNullOrWhiteSpace(sort))
        {
            var s = sort.Trim();
            bool desc = s.StartsWith("-");
            var field = desc ? s.Substring(1) : s;
            sortDef = field switch
            {
                "name" => desc ? Builders<Company>.Sort.Descending(x => x.Name) : Builders<Company>.Sort.Ascending(x => x.Name),
                _ => desc ? Builders<Company>.Sort.Descending(x => x.CreatedAt) : Builders<Company>.Sort.Ascending(x => x.CreatedAt)
            };
        }

        var total = await _col.CountDocumentsAsync(filter);
        var items = await _col.Find(filter)
            .Sort(sortDef)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<Company?> GetByIdAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return null;
        return await _col.Find(x => x.Id == oid).FirstOrDefaultAsync();
    }

    public Task AddServiceAsync(string companyId, CompanyServiceItem item)
    {
        if (!ObjectId.TryParse(companyId, out var oid))
            throw new ArgumentException("Invalid companyId", nameof(companyId));

        var update = Builders<Company>.Update
            .Push(x => x.Services, item)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        return _col.UpdateOneAsync(x => x.Id == oid, update);
    }

    public async Task EnsureIndexesAsync()
    {
        var idx1 = new CreateIndexModel<Company>(Builders<Company>.IndexKeys.Ascending(x => x.Name));
        var idx2 = new CreateIndexModel<Company>(Builders<Company>.IndexKeys.Ascending(x => x.CreatedAt));
        var idx3 = new CreateIndexModel<Company>(Builders<Company>.IndexKeys.Ascending(x => x.Membership!.Plan));
        var idx4 = new CreateIndexModel<Company>(Builders<Company>.IndexKeys.Ascending("services.type"));
        await _col.Indexes.CreateManyAsync(new[] { idx1, idx2, idx3, idx4 });
    }
}
```

> Nhớ cài gói nếu thiếu: `MongoDB.Driver`

Ngoài ra, cập nhật `UserRepository` để implement `IIndexInitializer` (nếu chưa): **Patch** trong `src/MyCabs.Infrastructure/Repositories/UserRepository.cs`

```csharp
using MyCabs.Infrastructure.Startup; // thêm using

namespace MyCabs.Infrastructure.Repositories;
public class UserRepository : IUserRepository, IIndexInitializer
{
    // ... giữ nguyên phần còn lại
}
```

---

## 4) Application (DTOs & Validators)

**File**: `src/MyCabs.Application/DTOs/CompanyDtos.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Application.DTOs;

public record CompaniesQuery(
    int Page = 1,
    int PageSize = 10,
    string? Search = null,
    string? Plan = null,
    string? ServiceType = null,
    string? Sort = "-createdAt"
);

public record AddCompanyServiceDto(string Type, string Title, decimal BasePrice);

public record CompanyDto(
    string Id,
    string Name,
    string? Description,
    string? Address,
    string? Plan,
    string? BillingCycle,
    DateTime? ExpiresAt,
    IEnumerable<CompanyServiceItem> Services
)
{
    public static CompanyDto FromEntity(Company c) => new(
        c.Id.ToString(),
        c.Name,
        c.Description,
        c.Address,
        c.Membership?.Plan,
        c.Membership?.BillingCycle,
        c.Membership?.ExpiresAt,
        c.Services
    );
}
```

**File**: `src/MyCabs.Application/Validation/CompanyValidators.cs`

```csharp
using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class AddCompanyServiceDtoValidator : AbstractValidator<AddCompanyServiceDto>
{
    public AddCompanyServiceDtoValidator()
    {
        RuleFor(x => x.Type).NotEmpty().Must(t => new[] { "taxi", "xe_om", "hang_hoa", "tour" }.Contains(t));
        RuleFor(x => x.Title).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BasePrice).GreaterThanOrEqualTo(0);
    }
}
```

---

## 5) Application (Service)

**File**: `src/MyCabs.Application/Services/CompanyService.cs`

```csharp
using MyCabs.Application.DTOs;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface ICompanyService
{
    Task<(IEnumerable<CompanyDto> Items, long Total)> GetCompaniesAsync(CompaniesQuery q);
    Task<CompanyDto?> GetByIdAsync(string id);
    Task AddServiceAsync(string companyId, AddCompanyServiceDto dto);
}

public class CompanyService : ICompanyService
{
    private readonly ICompanyRepository _repo;
    public CompanyService(ICompanyRepository repo) { _repo = repo; }

    public async Task<(IEnumerable<CompanyDto> Items, long Total)> GetCompaniesAsync(CompaniesQuery q)
    {
        var (items, total) = await _repo.FindAsync(q.Page, q.PageSize, q.Search, q.Plan, q.ServiceType, q.Sort);
        return (items.Select(CompanyDto.FromEntity), total);
    }

    public async Task<CompanyDto?> GetByIdAsync(string id)
    {
        var c = await _repo.GetByIdAsync(id);
        return c is null ? null : CompanyDto.FromEntity(c);
    }

    public async Task AddServiceAsync(string companyId, AddCompanyServiceDto dto)
    {
        var item = new Domain.Entities.CompanyServiceItem
        {
            Type = dto.Type,
            Title = dto.Title,
            BasePrice = dto.BasePrice
        };
        await _repo.AddServiceAsync(companyId, item);
    }
}
```

---

## 6) API (Controller)

**File**: `src/MyCabs.Api/Controllers/CompaniesController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Common;
using MyCabs.Api.Common; // ApiEnvelope
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyService _svc;
    public CompaniesController(ICompanyService svc) { _svc = svc; }

    [HttpGet]
    public async Task<IActionResult> GetCompanies([FromQuery] CompaniesQuery q)
    {
        var (items, total) = await _svc.GetCompaniesAsync(q);
        var page = q.Page <= 0 ? 1 : q.Page;
        var pageSize = q.PageSize <= 0 ? 10 : q.PageSize;
        var payload = new PagedResult<CompanyDto>(items, page, pageSize, total);
        return Ok(ApiEnvelope.Ok(HttpContext, payload));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var dto = await _svc.GetByIdAsync(id);
        if (dto is null) return NotFound(ApiEnvelope.Fail(HttpContext, "COMPANY_NOT_FOUND", "Company not found", 404));
        return Ok(ApiEnvelope.Ok(HttpContext, dto));
    }

    [Authorize(Roles = "Company,Admin")]
    [HttpPost("{id}/services")]
    public async Task<IActionResult> AddService(string id, [FromBody] AddCompanyServiceDto dto)
    {
        await _svc.AddServiceAsync(id, dto);
        return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Service added" }));
    }
}
```

---

## 7) Đăng ký DI & DbInitializer (Program.cs)

**Patch** trong `src/MyCabs.Api/Program.cs`

```csharp
using MyCabs.Application.Services;      // ICompanyService, CompanyService
using MyCabs.Domain.Interfaces;         // ICompanyRepository
using MyCabs.Infrastructure.Repositories; // CompanyRepository
using MyCabs.Infrastructure.Startup;      // IIndexInitializer

// Sau các dòng đăng ký UserRepository
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<ICompanyService, CompanyService>();

// Cho DbInitializer nhận nhiều repo có index
builder.Services.AddScoped<IIndexInitializer, UserRepository>();
builder.Services.AddScoped<IIndexInitializer, CompanyRepository>();
```

**Cập nhật DbInitializer**: thay vì inject `IUserRepository`, chuyển sang nhận **nhiều** `IIndexInitializer`. **File**: `src/MyCabs.Infrastructure/Startup/DbInitializer.cs`

```csharp
namespace MyCabs.Infrastructure.Startup;

public class DbInitializer
{
    private readonly IEnumerable<IIndexInitializer> _inits;
    public DbInitializer(IEnumerable<IIndexInitializer> inits) { _inits = inits; }

    public async Task EnsureIndexesAsync()
    {
        foreach (var i in _inits)
            await i.EnsureIndexesAsync();
    }
}
```

> Không cần đổi chỗ gọi trong `Program.cs` — vẫn:
>
> ```csharp
> using (var scope = app.Services.CreateScope())
> {
>     var init = scope.ServiceProvider.GetRequiredService<DbInitializer>();
>     await init.EnsureIndexesAsync();
> }
> ```

---

## 8) Kiểm thử nhanh (Swagger)

-   `GET /api/companies` — thử các query:
    -   `?page=1&pageSize=10`
    -   `?search=car`
    -   `?plan=Premium`
    -   `?serviceType=taxi`
    -   `?sort=-name` hoặc `?sort=createdAt`
-   `GET /api/companies/{id}` — với id hợp lệ
-   `POST /api/companies/{id}/services` — **Authorize** với role `Company` hoặc `Admin` (Bearer JWT); body mẫu:

```json
{
    "type": "taxi",
    "title": "Taxi 4 chỗ",
    "basePrice": 15000
}
```

---

## 9) Seed mẫu (tuỳ chọn)

Bạn có thể thêm một company mẫu để test nhanh. **File**: `seed_companies.json`

```json
[
    {
        "_id": { "$oid": "66fdc0f12b3a8a0b6c111111" },
        "ownerUserId": { "$oid": "66fdc0f12b3a8a0b6c222222" },
        "name": "Alpha Cab Co",
        "description": "Dịch vụ taxi và xe ôm",
        "address": "HCMC",
        "services": [
            {
                "serviceId": "S1",
                "type": "taxi",
                "title": "Taxi 4 chỗ",
                "basePrice": 12000
            }
        ],
        "membership": {
            "plan": "Basic",
            "billingCycle": "monthly",
            "expiresAt": { "$date": 1737840000000 }
        },
        "createdAt": { "$date": 1733788800000 },
        "updatedAt": { "$date": 1733788800000 }
    }
]
```

Import:

```bash
mongoimport --db mycabs --collection companies --file seed_companies.json --jsonArray
```

---

## 10) Ghi chú

-   Các response đều bọc theo **ApiEnvelope**. Danh sách dùng `PagedResult<T>`.
-   Khi mở rộng: thêm `PUT /api/companies/{id}` (update), `DELETE service` (pull từ mảng), `membership checkout` → `transactions` (mock).

---

# Add-on: CLI seed để tạo Company mẫu

> Tạo một **.NET console** nhỏ để chèn 1 company gắn với tài khoản Company trong `users`.

## Tạo project CLI

```bash
# từ thư mục gốc (chứa MyCabs.sln)
dotnet new console -o src/MyCabs.Cli
dotnet sln add src/MyCabs.Cli/MyCabs.Cli.csproj

# cài packages cần thiết
dotnet add src/MyCabs.Cli package MongoDB.Driver
```

## Program.cs (copy nguyên nội dung này)

**File**: `src/MyCabs.Cli/Program.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;

// ==== CLI options ====
string mongo = GetArg("--mongo") ?? "mongodb://localhost:27017";
string dbName = GetArg("--db") ?? "mycabs";
string? ownerId = GetArg("--owner-id");
string? ownerEmail = GetArg("--owner-email");

if (string.IsNullOrWhiteSpace(ownerId) && string.IsNullOrWhiteSpace(ownerEmail))
{
    Console.WriteLine("Usage: dotnet run -- --owner-id <ObjectId> OR --owner-email <email> [--mongo <uri>] [--db <name>]");
    return;
}

var client = new MongoClient(mongo);
var db = client.GetDatabase(dbName);
var usersCol = db.GetCollection<BsonDocument>("users");
var companiesCol = db.GetCollection<Company>("companies");

// ==== Resolve owner ObjectId ====
ObjectId ownerOid;
if (!string.IsNullOrWhiteSpace(ownerId))
{
    if (!ObjectId.TryParse(ownerId, out ownerOid)) { Console.WriteLine("Invalid --owner-id"); return; }
}
else
{
    var u = await usersCol.Find(new BsonDocument("Email", ownerEmail)).FirstOrDefaultAsync();
    if (u == null) { Console.WriteLine($"User with Email={ownerEmail} not found"); return; }
    ownerOid = u["_id"].AsObjectId;
}

// ==== Check existing company for this owner ====
var existed = await companiesCol.Find(c => c.OwnerUserId == ownerOid).FirstOrDefaultAsync();
if (existed != null)
{
    Console.WriteLine($"Company already exists: {existed.Id}");
    return;
}

// ==== Create sample company ====
var company = new Company
{
    Id = ObjectId.GenerateNewId(),
    OwnerUserId = ownerOid,
    Name = "Company 1",
    Description = "Mẫu công ty để test",
    Address = "HCMC",
    Membership = new MembershipInfo { Plan = "Basic", BillingCycle = "monthly", ExpiresAt = DateTime.UtcNow.AddMonths(1) },
    Services = new List<CompanyServiceItem> {
        new CompanyServiceItem { Type = "taxi", Title = "Taxi 4 chỗ", BasePrice = 12000 },
        new CompanyServiceItem { Type = "xe_om", Title = "Xe ôm nhanh", BasePrice = 8000 }
    },
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
};

await companiesCol.InsertOneAsync(company);
Console.WriteLine($"Created company with Id={company.Id}");

// ==== Helpers & local types (copy từ Domain để tránh phụ thuộc) ====
static string? GetArg(string name)
{
    var idx = Array.FindIndex(Environment.GetCommandLineArgs(), x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
    if (idx >= 0 && idx + 1 < Environment.GetCommandLineArgs().Length)
        return Environment.GetCommandLineArgs()[idx + 1];
    return null;
}

public class Company
{
    public ObjectId Id { get; set; }
    public ObjectId OwnerUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Address { get; set; }
    public List<CompanyServiceItem> Services { get; set; } = new();
    public MembershipInfo? Membership { get; set; }
    public ObjectId? WalletId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CompanyServiceItem
{
    public string ServiceId { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Type { get; set; } = "taxi";
    public string Title { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
}

public class MembershipInfo
{
    public string Plan { get; set; } = "Free";
    public string BillingCycle { get; set; } = "monthly";
    public DateTime? ExpiresAt { get; set; }
}
```

## Cách chạy (theo dữ liệu bạn cung cấp)

> User Company 1 có `_id = 68a193eee917988d73683076` và Email `c1@mycabs.com`.

Chạy bằng **owner-id**:

```bash
# macOS/Linux
dotnet run --project src/MyCabs.Cli/MyCabs.Cli.csproj -- \
  --owner-id 68a193eee917988d73683076 --mongo mongodb://localhost:27017 --db mycabs
```

Hoặc bằng **owner-email**:

```bash
dotnet run --project src/MyCabs.Cli/MyCabs.Cli.csproj -- \
  --owner-email c1@mycabs.com --mongo mongodb://localhost:27017 --db mycabs
```

> Kết quả sẽ in `Created company with Id=<ObjectId>` hoặc thông báo đã tồn tại.

## Ghi chú

-   CLI sử dụng **MongoDB.Driver** trực tiếp (không phụ thuộc project Domain/Infrastructure) để tránh kéo thêm references.
-   Nếu bạn muốn tái sử dụng **Domain.Entities.Company**, bạn có thể:\
    `dotnet add src/MyCabs.Cli/MyCabs.Cli.csproj reference src/MyCabs.Domain/MyCabs.Domain.csproj` rồi bỏ 3 class local ở cuối file.

---

# MyCabs – Driver Module (Add-on)

> Module **Driver** bổ sung theo cùng phong cách với Company: Domain → Repository → Service → Controller, dùng **ApiEnvelope** và MongoDB (camelCase). Các endpoint chính:
>
> -   `GET /api/drivers/openings` — xem danh sách company (search/filter/sort/paginate)
> -   `POST /api/drivers/apply` — driver nộp đơn vào company _(JWT: Role=Driver)_
> -   `POST /api/drivers/invitations/{inviteId}/respond` — driver chấp nhận/từ chối lời mời _(JWT: Role=Driver)_
> -   `GET /api/drivers/me/transactions` — lịch sử giao dịch _(stub, sẽ nối với Transactions/Wallet ở bước sau)_

---

## 1) Domain (Entities)

**File**: `src/MyCabs.Domain/Entities/Driver.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Driver
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("userId")]
    public ObjectId UserId { get; set; }

    [BsonElement("bio")]
    public string? Bio { get; set; }

    [BsonElement("companyId")]
    public ObjectId? CompanyId { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "offline"; // available|busy|offline

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**File**: `src/MyCabs.Domain/Entities/Application.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Application
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("driverId")]
    public ObjectId DriverId { get; set; }

    [BsonElement("companyId")]
    public ObjectId CompanyId { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "Pending"; // Pending|Approved|Rejected

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**File**: `src/MyCabs.Domain/Entities/Invitation.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class Invitation
{
    [BsonId]
    public ObjectId Id { get; set; }

    [BsonElement("companyId")]
    public ObjectId CompanyId { get; set; }

    [BsonElement("driverId")]
    public ObjectId DriverId { get; set; }

    [BsonElement("status")]
    public string Status { get; set; } = "Pending"; // Pending|Accepted|Declined

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## 2) Domain (Interfaces)

**File**: `src/MyCabs.Domain/Interfaces/IDriverRepository.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IDriverRepository
{
    Task<Driver?> GetByUserIdAsync(string userId);
    Task<Driver> CreateIfMissingAsync(string userId);
    Task SetCompanyAsync(string driverId, string companyId);
}
```

**File**: `src/MyCabs.Domain/Interfaces/IApplicationRepository.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IApplicationRepository
{
    Task<bool> ExistsPendingAsync(string driverId, string companyId);
    Task CreateAsync(string driverId, string companyId);
    Task EnsureIndexesAsync();
}
```

**File**: `src/MyCabs.Domain/Interfaces/IInvitationRepository.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IInvitationRepository
{
    Task<Invitation?> GetByIdAsync(string inviteId);
    Task UpdateStatusAsync(string inviteId, string status);
    Task EnsureIndexesAsync();
}
```

---

## 3) Infrastructure (Repositories)

**File**: `src/MyCabs.Infrastructure/Repositories/DriverRepository.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class DriverRepository : IDriverRepository, IIndexInitializer
{
    private readonly IMongoCollection<Driver> _col;
    public DriverRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<Driver>("drivers");
    }

    public async Task<Driver?> GetByUserIdAsync(string userId)
    {
        if (!ObjectId.TryParse(userId, out var uid)) return null;
        return await _col.Find(x => x.UserId == uid).FirstOrDefaultAsync();
    }

    public async Task<Driver> CreateIfMissingAsync(string userId)
    {
        if (!ObjectId.TryParse(userId, out var uid)) throw new ArgumentException("Invalid userId");
        var d = await _col.Find(x => x.UserId == uid).FirstOrDefaultAsync();
        if (d != null) return d;
        d = new Driver { Id = ObjectId.GenerateNewId(), UserId = uid, Status = "available" };
        await _col.InsertOneAsync(d);
        return d;
    }

    public async Task SetCompanyAsync(string driverId, string companyId)
    {
        if (!ObjectId.TryParse(driverId, out var did)) throw new ArgumentException("Invalid driverId");
        if (!ObjectId.TryParse(companyId, out var cid)) throw new ArgumentException("Invalid companyId");
        var update = Builders<Driver>.Update
            .Set(x => x.CompanyId, cid)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _col.UpdateOneAsync(x => x.Id == did, update);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Driver>(Builders<Driver>.IndexKeys.Ascending(x => x.UserId), new CreateIndexOptions { Unique = true });
        var ix2 = new CreateIndexModel<Driver>(Builders<Driver>.IndexKeys.Ascending(x => x.CompanyId));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2 });
    }
}
```

**File**: `src/MyCabs.Infrastructure/Repositories/ApplicationRepository.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class ApplicationRepository : IApplicationRepository, IIndexInitializer
{
    private readonly IMongoCollection<Application> _col;
    public ApplicationRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<Application>("applications");
    }

    public async Task<bool> ExistsPendingAsync(string driverId, string companyId)
    {
        if (!ObjectId.TryParse(driverId, out var did)) return false;
        if (!ObjectId.TryParse(companyId, out var cid)) return false;
        var filter = Builders<Application>.Filter.And(
            Builders<Application>.Filter.Eq(x => x.DriverId, did),
            Builders<Application>.Filter.Eq(x => x.CompanyId, cid),
            Builders<Application>.Filter.Eq(x => x.Status, "Pending")
        );
        var count = await _col.CountDocumentsAsync(filter);
        return count > 0;
    }

    public async Task CreateAsync(string driverId, string companyId)
    {
        if (!ObjectId.TryParse(driverId, out var did)) throw new ArgumentException("Invalid driverId");
        if (!ObjectId.TryParse(companyId, out var cid)) throw new ArgumentException("Invalid companyId");
        var app = new Application { Id = ObjectId.GenerateNewId(), DriverId = did, CompanyId = cid, Status = "Pending", CreatedAt = DateTime.UtcNow };
        await _col.InsertOneAsync(app);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Application>(Builders<Application>.IndexKeys.Ascending(x => x.DriverId));
        var ix2 = new CreateIndexModel<Application>(Builders<Application>.IndexKeys.Ascending(x => x.CompanyId));
        var ix3 = new CreateIndexModel<Application>(Builders<Application>.IndexKeys.Ascending(x => x.Status));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2, ix3 });
    }
}
```

**File**: `src/MyCabs.Infrastructure/Repositories/InvitationRepository.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class InvitationRepository : IInvitationRepository, IIndexInitializer
{
    private readonly IMongoCollection<Invitation> _col;
    public InvitationRepository(IMongoContext ctx)
    {
        _col = ctx.GetCollection<Invitation>("invitations");
    }

    public async Task<Invitation?> GetByIdAsync(string inviteId)
    {
        if (!ObjectId.TryParse(inviteId, out var iid)) return null;
        return await _col.Find(x => x.Id == iid).FirstOrDefaultAsync();
    }

    public async Task UpdateStatusAsync(string inviteId, string status)
    {
        if (!ObjectId.TryParse(inviteId, out var iid)) throw new ArgumentException("Invalid inviteId");
        var update = Builders<Invitation>.Update
            .Set(x => x.Status, status)
            .Set(x => x.CreatedAt, DateTime.UtcNow);
        await _col.UpdateOneAsync(x => x.Id == iid, update);
    }

    public async Task EnsureIndexesAsync()
    {
        var ix1 = new CreateIndexModel<Invitation>(Builders<Invitation>.IndexKeys.Ascending(x => x.DriverId));
        var ix2 = new CreateIndexModel<Invitation>(Builders<Invitation>.IndexKeys.Ascending(x => x.CompanyId));
        var ix3 = new CreateIndexModel<Invitation>(Builders<Invitation>.IndexKeys.Ascending(x => x.Status));
        await _col.Indexes.CreateManyAsync(new[] { ix1, ix2, ix3 });
    }
}
```

---

## 4) Application (DTOs & Validators)

**File**: `src/MyCabs.Application/DTOs/DriverDtos.cs`

```csharp
namespace MyCabs.Application.DTOs;

public record DriverApplyDto(string CompanyId);
public record InvitationRespondDto(string Action); // Accept | Decline
```

**File**: `src/MyCabs.Application/Validation/DriverValidators.cs`

```csharp
using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class DriverApplyDtoValidator : AbstractValidator<DriverApplyDto>
{
    public DriverApplyDtoValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}

public class InvitationRespondDtoValidator : AbstractValidator<InvitationRespondDto>
{
    public InvitationRespondDtoValidator()
    {
        RuleFor(x => x.Action)
            .NotEmpty()
            .Must(a => a is "Accept" or "Decline")
            .WithMessage("Action must be Accept or Decline");
    }
}
```

---

## 5) Application (Service)

**File**: `src/MyCabs.Application/Services/DriverService.cs`

```csharp
using MyCabs.Application.DTOs;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IDriverService
{
    Task<(IEnumerable<CompanyDto> Items, long Total)> GetOpeningsAsync(CompaniesQuery q);
    Task ApplyAsync(string userId, DriverApplyDto dto);
    Task RespondInvitationAsync(string userId, string inviteId, string action);
}

public class DriverService : IDriverService
{
    private readonly IDriverRepository _drivers;
    private readonly ICompanyRepository _companies;
    private readonly IApplicationRepository _apps;
    private readonly IInvitationRepository _invites;

    public DriverService(IDriverRepository drivers, ICompanyRepository companies, IApplicationRepository apps, IInvitationRepository invites)
    {
        _drivers = drivers; _companies = companies; _apps = apps; _invites = invites;
    }

    public async Task<(IEnumerable<CompanyDto> Items, long Total)> GetOpeningsAsync(CompaniesQuery q)
    {
        var (items, total) = await _companies.FindAsync(q.Page, q.PageSize, q.Search, q.Plan, q.ServiceType, q.Sort);
        return (items.Select(CompanyDto.FromEntity), total);
    }

    public async Task ApplyAsync(string userId, DriverApplyDto dto)
    {
        // Đảm bảo driver profile tồn tại
        var driver = await _drivers.CreateIfMissingAsync(userId);

        // Company tồn tại?
        var comp = await _companies.GetByIdAsync(dto.CompanyId);
        if (comp is null) throw new InvalidOperationException("COMPANY_NOT_FOUND");

        // Đã có pending application chưa?
        if (await _apps.ExistsPendingAsync(driver.Id.ToString(), dto.CompanyId))
            throw new InvalidOperationException("APPLICATION_ALREADY_PENDING");

        await _apps.CreateAsync(driver.Id.ToString(), dto.CompanyId);
    }

    public async Task RespondInvitationAsync(string userId, string inviteId, string action)
    {
        var driver = await _drivers.CreateIfMissingAsync(userId);
        var inv = await _invites.GetByIdAsync(inviteId);
        if (inv is null || inv.DriverId != driver.Id)
            throw new InvalidOperationException("INVITATION_NOT_FOUND");

        var newStatus = action == "Accept" ? "Accepted" : "Declined";
        await _invites.UpdateStatusAsync(inviteId, newStatus);

        if (newStatus == "Accepted")
        {
            await _drivers.SetCompanyAsync(driver.Id.ToString(), inv.CompanyId.ToString());
        }
    }
}
```

---

## 6) API (Controller)

**File**: `src/MyCabs.Api/Controllers/DriversController.cs`

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Common;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DriversController : ControllerBase
{
    private readonly IDriverService _svc;
    public DriversController(IDriverService svc) { _svc = svc; }

    [HttpGet("openings")]
    public async Task<IActionResult> GetOpenings([FromQuery] CompaniesQuery q)
    {
        var (items, total) = await _svc.GetOpeningsAsync(q);
        var payload = new PagedResult<CompanyDto>(items, q.Page <= 0 ? 1 : q.Page, q.PageSize <= 0 ? 10 : q.PageSize, total);
        return Ok(ApiEnvelope.Ok(HttpContext, payload));
    }

    [Authorize(Roles = "Driver")]
    [HttpPost("apply")]
    public async Task<IActionResult> Apply([FromBody] DriverApplyDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) // sometimes mapped
                     ?? User.FindFirstValue("sub")                     // JwtRegisteredClaimNames.Sub
                     ?? string.Empty;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "UNAUTHORIZED", "Authentication is required", 401));

        try
        {
            await _svc.ApplyAsync(userId, dto);
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Application submitted" }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "COMPANY_NOT_FOUND")
        {
            return NotFound(ApiEnvelope.Fail(HttpContext, "COMPANY_NOT_FOUND", "Company not found", 404));
        }
        catch (InvalidOperationException ex) when (ex.Message == "APPLICATION_ALREADY_PENDING")
        {
            return Conflict(ApiEnvelope.Fail(HttpContext, "APPLICATION_ALREADY_PENDING", "Application already pending", 409));
        }
    }

    [Authorize(Roles = "Driver")]
    [HttpPost("invitations/{inviteId}/respond")]
    public async Task<IActionResult> Respond(string inviteId, [FromBody] InvitationRespondDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiEnvelope.Fail(HttpContext, "UNAUTHORIZED", "Authentication is required", 401));

        try
        {
            await _svc.RespondInvitationAsync(userId, inviteId, dto.Action);
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "Invitation updated" }));
        }
        catch (InvalidOperationException ex) when (ex.Message == "INVITATION_NOT_FOUND")
        {
            return NotFound(ApiEnvelope.Fail(HttpContext, "INVITATION_NOT_FOUND", "Invitation not found", 404));
        }
    }

    // Stub: sẽ nối với Transactions/Wallet về sau
    [Authorize(Roles = "Driver")]
    [HttpGet("me/transactions")]
    public IActionResult MyTransactions()
    {
        var payload = new PagedResult<object>(Array.Empty<object>(), 1, 10, 0);
        return Ok(ApiEnvelope.Ok(HttpContext, payload));
    }
}
```

---

## 7) Đăng ký DI & DbInitializer (Program.cs)

**Patch** trong `src/MyCabs.Api/Program.cs`

```csharp
using MyCabs.Application.Services;        // IDriverService, DriverService
using MyCabs.Domain.Interfaces;           // IDriverRepository, IApplicationRepository, IInvitationRepository
using MyCabs.Infrastructure.Repositories; // DriverRepository, ApplicationRepository, InvitationRepository
using MyCabs.Infrastructure.Startup;      // IIndexInitializer

// Repositories
builder.Services.AddScoped<IDriverRepository, DriverRepository>();
builder.Services.AddScoped<IApplicationRepository, ApplicationRepository>();
builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();

// Services
builder.Services.AddScoped<IDriverService, DriverService>();

// Index initializers
builder.Services.AddScoped<IIndexInitializer, DriverRepository>();
builder.Services.AddScoped<IIndexInitializer, ApplicationRepository>();
builder.Services.AddScoped<IIndexInitializer, InvitationRepository>();
```

> `DbInitializer` hiện đã nhận `IEnumerable<IIndexInitializer>` nên sẽ tự gọi tạo index cho cả 3 repo mới.

---

## 8) Kiểm thử nhanh (Swagger/Postman)

1. **Openings** (không cần auth):
    - `GET /api/drivers/openings?serviceType=taxi&sort=-name`
2. **Login** bằng tài khoản **Driver** → lấy accessToken → Authorize.
3. **Apply**: `POST /api/drivers/apply`

```json
{ "companyId": "<ObjectId của company>" }
```

-   Nếu company không tồn tại → 404 `COMPANY_NOT_FOUND`.
-   Nếu đã pending → 409 `APPLICATION_ALREADY_PENDING`.

4. **Respond Invite**: `POST /api/drivers/invitations/{inviteId}/respond`

```json
{ "Action": "Accept" }
```

-   Nếu invite không thuộc về driver → 404 `INVITATION_NOT_FOUND`.

5. **My Transactions**: `GET /api/drivers/me/transactions` → hiện trả rỗng (stub).

---

## 9) Seed mẫu (tuỳ chọn)

**Driver** gắn với user role `Driver` (giả sử `_id` người dùng là `68a193eee917988d73683077`):

```js
// mongosh
db.drivers.insertOne({
    userId: ObjectId("68a193eee917988d73683077"),
    bio: "5 năm kinh nghiệm xe 4 chỗ",
    status: "available",
    createdAt: new Date(),
    updatedAt: new Date(),
});
```

**Application** mẫu (Pending):

```js
db.applications.insertOne({
    driverId: ObjectId("<driverId>"),
    companyId: ObjectId("<companyId>"),
    status: "Pending",
    createdAt: new Date(),
});
```

**Invitation** mẫu (Pending):

```js
db.invitations.insertOne({
    companyId: ObjectId("<companyId>"),
    driverId: ObjectId("<driverId>"),
    status: "Pending",
    createdAt: new Date(),
});
```

---

## 10) Ghi chú mở rộng

-   Bước kế tiếp có thể nối `MyTransactions` với **wallets/transactions** (mock) và push **notifications**/SignalR khi `application`/`invitation` đổi trạng thái.
-   Khi Company **Accept** application, có thể set `driver.companyId` ngay tại flow của Company (không chỉ khi driver Accept invite).

---
