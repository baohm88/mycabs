# MyCabs – Rider Discovery & Ratings (MVP)

> Canvas đã **xóa nội dung cũ** và thay bằng module **Rider**: tìm Company/Driver + đánh giá & bình luận. Mục tiêu: có thể **search** (server-side filter/sort/paginate) và **rate/comment** cho `Company` hoặc `Driver`. Tất cả API trả về theo **ApiEnvelope** như trước.

---

## 1) Domain (Entities)

``

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

``

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

``

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

``

```csharp
namespace MyCabs.Application.DTOs;

public record RiderCompaniesQuery(int Page=1,int PageSize=10,string? Search=null,string? ServiceType=null,string? Plan=null,string? Sort=null);
public record RiderDriversQuery(int Page=1,int PageSize=10,string? Search=null,string? CompanyId=null,string? Sort=null);

public record CreateRatingDto(string TargetType, string TargetId, int Stars, string? Comment);
public record RatingsQuery(string TargetType, string TargetId, int Page=1, int PageSize=10);

public record RatingDto(string Id, string TargetType, string TargetId, string UserId, int Stars, string? Comment, DateTime CreatedAt);
public record RatingSummaryDto(long Count, double Average);
```

``

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

``

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

``

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

