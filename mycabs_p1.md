# mycabs.com – MVP Blueprint (ADSE Kỳ 3)

> Scope: Uber-like MVP running **locally**. Payments are **mocked** (no Stripe). Stack: **ASP.NET Core Web API + MongoDB + React (Vite)**. Auth via **JWT + BCrypt + RBAC** (Admin/Rider/Driver/Company).

---

## 1) Review & Recommendations

### 1.1 Kiến trúc & công nghệ (đạt yêu cầu)

- **Backend**: ASP.NET Core Web API + Clean Architecture (Controller → Service → Repository). Phù hợp cho MongoDB + tách lớp rõ ràng.
- **DB**: MongoDB phù hợp use cases có tốc độ thay đổi cao, chat, notification. Hướng **denormalized** với **embedded** documents cho các sub-entities đọc thường xuyên.
- **Frontend**: React (Vite) + React-Bootstrap + Toastify + Axios: đủ nhẹ cho MVP.
- **Bảo mật**: JWT + BCrypt + RBAC: phù hợp.

### 1.2 Tối ưu đề xuất

- **ID chuẩn**: dùng `ObjectId` (MongoDB) cho tất cả collections; expose `id` (string) ra ngoài API.
- **Soft-delete**: trường `isActive`/`isDeleted` để deactivate tài khoản & ngăn mất dữ liệu liên quan (lịch sử chat, payment log).
- **Audit fields**: `createdAt`, `updatedAt`, `createdBy`, `updatedBy` cho tất cả docs.
- **Search/pagination**: server-side với index phù hợp (compound index cho filter phổ biến).
- **DTO validation**: FluentValidation hoặc DataAnnotations; chuẩn hoá lỗi 422 (ValidationProblemDetails).
- **Notifications/Chat**: dùng **SignalR** (ASP.NET) cho realtime thay vì socket thuần → giảm boilerplate.
- **Config**: phân tách `appsettings.Development.json`; secret (JWT, SMTP) qua biến môi trường.

---

## 2) Kiến trúc tổng thể

```
Client (React Vite)
  ├─ Pages (Auth, Company, Driver, Rider, Admin)
  ├─ Redux/Context (auth, entities, ui)
  └─ API layer (Axios + interceptors)

ASP.NET Core Web API
  ├─ Presentation: Controllers (REST, Swagger)
  ├─ Application: Services (business rules), DTOs, Validators
  ├─ Domain: Entities (POCOs), Enums, Interfaces (Repositories)
  └─ Infrastructure: Mongo Context, Repositories, External adapters (Email, SignalR)

MongoDB (Atlas/local)
  ├─ users, companies, drivers, riders, services,
  ├─ memberships, wallets, transactions, invitations,
  ├─ applications, ratings, chats, messages, notifications
```

**Realtime**: SignalR Hub(s)

- `ChatHub`: 1-1 messaging (Rider↔Driver, Rider↔Company, Company↔Driver), read receipts.
- `NotificationHub`: đẩy toast/indicator read/unread.

---

## 3) ERD MongoDB (định hướng embed)

> Nguyên tắc:
>
> - **Embed** khi dữ liệu phụ thuộc vòng đời cha / đọc cùng nhau thường xuyên (VD: `company.services`).
> - **Reference** khi dữ liệu dùng chung/độc lập, cần join nhiều nơi (VD: `users`).

**Collections chính**

1. **users** (tất cả roles)

```json
{
  "_id": ObjectId,
  "email": "string", "passwordHash": "string", "fullName": "string",
  "role": "Admin|Rider|Driver|Company",
  "isActive": true,
  "phone": "string", "avatarUrl": "string",
  "createdAt": ISODate, "updatedAt": ISODate
}
```

2. **companies**

```json
{
  "_id": ObjectId,
  "ownerUserId": ObjectId,  // ref users
  "name": "string", "description": "string", "address": "string",
  "services": [
    { "serviceId": ObjectId, "type": "taxi|xe_om|hang_hoa|tour", "title": "string", "basePrice": 10000 }
  ],
  "membership": { "plan": "Free|Basic|Premium", "billingCycle": "monthly|quarterly", "expiresAt": ISODate },
  "walletId": ObjectId,
  "createdAt": ISODate, "updatedAt": ISODate
}
```

3. **drivers**

```json
{
  "_id": ObjectId,
  "userId": ObjectId, // ref users
  "bio": "string",
  "vehicles": [{ "type": "car|bike|truck", "plate": "string" }],
  "companyId": ObjectId, // nullable when not contracted
  "status": "available|busy|offline",
  "walletId": ObjectId,
  "createdAt": ISODate, "updatedAt": ISODate
}
```

4. **riders**

```json
{ "_id": ObjectId, "userId": ObjectId, "defaultAddresses": [{"label":"Home","lat":0,"lng":0}] }
```

5. **wallets** & **transactions** (tách riêng, reference bởi company/driver)

```json
// wallets
{ "_id": ObjectId, "ownerType": "Company|Driver", "ownerId": ObjectId, "balance": 0, "lowBalanceThreshold": 100000 }

// transactions (mock payment & salary)
{
  "_id": ObjectId,
  "type": "membership|salary",
  "from": {"type":"Company|Admin", "id": ObjectId},
  "to":   {"type":"Admin|Driver",  "id": ObjectId},
  "amount": 100000,
  "status": "Pending|Completed|Failed",
  "note": "string",
  "createdAt": ISODate
}
```

6. **applications** (driver ứng tuyển company)

```json
{ "_id": ObjectId, "driverId": ObjectId, "companyId": ObjectId, "status": "Pending|Approved|Rejected", "createdAt": ISODate }
```

7. **invitations** (company mời driver)

```json
{ "_id": ObjectId, "companyId": ObjectId, "driverId": ObjectId, "status": "Pending|Accepted|Declined", "createdAt": ISODate }
```

8. **ratings** (rider đánh giá company/service)

```json
{ "_id": ObjectId, "riderId": ObjectId, "companyId": ObjectId, "serviceId": ObjectId, "stars": 1, "comment": "string", "createdAt": ISODate }
```

9. **chats** & **messages**

```json
// chats
{ "_id": ObjectId, "participants": [{"type":"Rider|Driver|Company","id":ObjectId}], "lastMessageAt": ISODate }

// messages (index chatId, createdAt)
{ "_id": ObjectId, "chatId": ObjectId, "sender": {"type":"Rider|Driver|Company","id":ObjectId}, "text":"string", "readBy": [ObjectId], "createdAt": ISODate }
```

10. **notifications**

```json
{ "_id": ObjectId, "userId": ObjectId, "type": "WalletLow|Invite|Application|Payment", "data": {}, "isRead": false, "createdAt": ISODate }
```

**Index gợi ý**

- `users(email)` unique; `companies(ownerUserId)`; `drivers(userId)`; `applications(companyId, status)`; `invitations(driverId, status)`; `transactions(type, status, createdAt)`; `messages(chatId, createdAt)`; `notifications(userId, isRead, createdAt)`.

---

## 4) Luồng API chính + DTO/Validation

### 4.1 Auth

- `POST /api/auth/register` `{email, password, fullName, role}` → tạo `users` + (company/driver/rider profile rỗng tương ứng). Password: BCrypt. Email unique.
- `POST /api/auth/login` `{email, password}` → JWT `{accessToken, role, user}`.
- `PUT /api/auth/profile` auth required → cập nhật thông tin cơ bản.
- `POST /api/auth/reset-password` (OTP qua email, mock OK).
- `PATCH /api/auth/deactivate` → `isActive=false`.

**DTO/Validation**

```csharp
public record RegisterDto(string Email, string Password, string FullName, string Role);
// Email: required + format; Password: min 8, 1 upper, 1 lower, 1 digit; Role in enum.
```

### 4.2 Company

- `GET /api/companies` query: search/sort/paginate.
- `GET /api/companies/{id}`: chi tiết + services (embed).
- `POST /api/companies/{id}/services` (Company) thêm dịch vụ.
- `POST /api/companies/{id}/invite` (Company) `{driverId}` → tạo `invitations`.
- `POST /api/companies/{id}/membership/checkout` (mock) → tạo `transactions` type `membership` (Company→Admin) status `Pending`→`Completed`.
- `POST /api/companies/{id}/salary/pay` (mock) `{driverId, amount}` → `transactions` type `salary` (Company→Driver).
- `GET /api/companies/{id}/wallet` → số dư + cảnh báo low balance.

### 4.3 Driver

- `GET /api/drivers/openings` (companies đang tuyển) + filter.
- `POST /api/drivers/apply` `{companyId}` → `applications` (Pending).
- `POST /api/drivers/invitations/{inviteId}/respond` `{action: Accept|Decline}`.
- `GET /api/drivers/me/transactions` → lịch sử `salary` + balance.

### 4.4 Rider

- `GET /api/riders/companies` (search, filter, paginate)
- `GET /api/riders/companies/{id}` + drivers
- `POST /api/riders/ratings` `{companyId, serviceId, stars (1..5), comment?}`

### 4.5 Admin

- `GET /api/admin/users` (search/filter/sort/paginate)
- `PATCH /api/admin/users/{id}/deactivate`
- `GET /api/admin/transactions?type=membership|salary`

### 4.6 Chat (SignalR)

- REST bootstrap: `POST /api/chats/start` `{participantType,id}` → chatId
- SignalR events: `SendMessage(chatId, text)`, `MarkRead(chatId, messageId)`

### 4.7 Notifications (SignalR)

- `GET /api/notifications/me` (read/unread)
- `PATCH /api/notifications/{id}/read`
- Server push qua `NotificationHub` khi: low wallet, new invite/application, payment completed/failed.

---

## 5) Skeleton Project

### 5.1 Backend structure

```
src/
  MyCabs.Api          // Presentation
  MyCabs.Application  // Services, DTOs, Validators
  MyCabs.Domain       // Entities, Enums, Interfaces
  MyCabs.Infrastructure// Mongo, Repos, Adapters (Email, SignalR)
```

**Commands**

```bash
dotnet new webapi -o MyCabs.Api
# tạo classlib
dotnet new classlib -o MyCabs.Application
dotnet new classlib -o MyCabs.Domain
dotnet new classlib -o MyCabs.Infrastructure

# add refs
dotnet add MyCabs.Api reference MyCabs.Application MyCabs.Infrastructure MyCabs.Domain
dotnet add MyCabs.Infrastructure package MongoDB.Driver
dotnet add MyCabs.Api package Swashbuckle.AspNetCore
dotnet add MyCabs.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add MyCabs.Api package BCrypt.Net-Next
dotnet add MyCabs.Api package Microsoft.AspNetCore.SignalR
```

**Mongo Context (Infrastructure)**

```csharp
public class MongoSettings { public string ConnectionString {get;set;} = ""; public string Database {get;set;} = ""; }

public interface IMongoContext {
  IMongoCollection<T> GetCollection<T>(string name);
}

public class MongoContext : IMongoContext {
  private readonly IMongoDatabase _db;
  public MongoContext(IOptions<MongoSettings> opts) {
    var client = new MongoClient(opts.Value.ConnectionString);
    _db = client.GetDatabase(opts.Value.Database);
  }
  public IMongoCollection<T> GetCollection<T>(string name) => _db.GetCollection<T>(name);
}
```

**Entities (Domain)** (ví dụ Users)

```csharp
public class User {
  public ObjectId Id { get; set; }
  public string Email { get; set; } = string.Empty;
  public string PasswordHash { get; set; } = string.Empty;
  public string FullName { get; set; } = string.Empty;
  public string Role { get; set; } = "Rider";
  public bool IsActive { get; set; } = true;
  public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
  public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**Repository interface + impl (Users)**

```csharp
public interface IUserRepository {
  Task<User?> FindByEmailAsync(string email);
  Task CreateAsync(User user);
}

public class UserRepository : IUserRepository {
  private readonly IMongoCollection<User> _col;
  public UserRepository(IMongoContext ctx){ _col = ctx.GetCollection<User>("users"); }
  public Task<User?> FindByEmailAsync(string email) => _col.Find(x=>x.Email==email).FirstOrDefaultAsync();
  public Task CreateAsync(User u) => _col.InsertOneAsync(u);
}
```

**AuthService (Application)**

```csharp
public class AuthService {
  private readonly IUserRepository _users;
  private readonly IJwtTokenService _jwt;
  public AuthService(IUserRepository users, IJwtTokenService jwt){ _users = users; _jwt = jwt; }

  public async Task<(bool ok, string? error, string? token)> RegisterAsync(RegisterDto dto){
    var exists = await _users.FindByEmailAsync(dto.Email);
    if(exists!=null) return (false, "Email already exists", null);
    var user = new User{
      Email = dto.Email.Trim().ToLower(),
      PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
      FullName = dto.FullName,
      Role = dto.Role,
    };
    await _users.CreateAsync(user);
    var token = _jwt.Generate(user);
    return (true, null, token);
  }
}
```

**AuthController (Api)**

```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase {
  private readonly AuthService _svc;
  public AuthController(AuthService svc){ _svc = svc; }

  [HttpPost("register")]
  public async Task<IActionResult> Register(RegisterDto dto){
    var (ok, err, token) = await _svc.RegisterAsync(dto);
    if(!ok) return Conflict(new{message=err});
    return Ok(new{ accessToken = token });
  }
}
```

**JWT Token Service (Api/Infrastructure)**

```csharp
public interface IJwtTokenService { string Generate(User user); }
```

> Tương tự triển khai Repositories/Services cho `companies`, `drivers`, `applications`, `invitations`, `wallets`, `transactions`.

### 5.2 Frontend structure

```
mycabs-ui/
  src/
    app/ (store.ts, hooks)
    api/ (axios.ts, endpoints.ts)
    features/
      auth/
      companies/
      drivers/
      riders/
      admin/
    components/
    pages/
```

**Commands**

```bash
npm create vite@latest mycabs-ui -- --template react
cd mycabs-ui && npm i axios react-bootstrap bootstrap react-toastify @reduxjs/toolkit react-redux
```

**Axios base + interceptors**

```ts
// src/api/axios.ts
import axios from 'axios';
const api = axios.create({ baseURL: 'http://localhost:5000/api' });
api.interceptors.request.use(cfg => {
  const t = localStorage.getItem('token');
  if(t) cfg.headers.Authorization = `Bearer ${t}`;
  return cfg;
});
export default api;
```

**Redux store**

```ts
// src/app/store.ts
import { configureStore } from '@reduxjs/toolkit';
import auth from '../features/auth/authSlice';
export const store = configureStore({ reducer: { auth } });
export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
```

**Auth slice (rút gọn)**

```ts
// src/features/auth/authSlice.ts
import { createSlice, createAsyncThunk } from '@reduxjs/toolkit';
import api from '../../api/axios';
export const register = createAsyncThunk('auth/register', async (dto:any)=>{
  const {data} = await api.post('/auth/register', dto); return data;
});
const slice = createSlice({
  name: 'auth',
  initialState: { token:null as string|null, status:'idle' },
  reducers: { logout(s){ s.token=null; localStorage.removeItem('token'); } },
  extraReducers: b=>{
    b.addCase(register.fulfilled, (s,a)=>{ s.token=a.payload.accessToken; localStorage.setItem('token', s.token!); });
  }
});
export default slice.reducer;
```

---

## 6) Seed data (MongoDB)

> Lưu file `seed.json` và import bằng `mongoimport`.

```json
{
  "users": [
    { "email":"admin@mycabs.com","passwordHash":"$2a$10$HASH","fullName":"Admin","role":"Admin","isActive":true },
    { "email":"c1@mycabs.com","passwordHash":"$2a$10$HASH","fullName":"Company 1","role":"Company","isActive":true },
    { "email":"d1@mycabs.com","passwordHash":"$2a$10$HASH","fullName":"Driver 1","role":"Driver","isActive":true },
    { "email":"r1@mycabs.com","passwordHash":"$2a$10$HASH","fullName":"Rider 1","role":"Rider","isActive":true }
  ]
}
```****

Command:

```bash
mongoimport --db mycabs --collection users --file seed.json --jsonArray
```

---

## 7) Payment Mock Flow

- **Membership (Company→Admin)**: tạo `transaction(Pending)` → giả lập xử lý (timer/endpoint `/complete`) → cập nhật `Completed` + tăng `Admin` sổ phụ (hoặc bỏ qua, chỉ log).
- **Salary (Company→Driver)**: tương tự; khi `Completed`, tăng balance `driver.wallet` và giảm `company.wallet`. Nếu `company.wallet` < threshold → push notification `WalletLow`.

---

## 8) Realtime (SignalR)

- **ChatHub**: `Join(userId)`, `SendMessage(chatId, text)`, `MarkRead(chatId, messageId)`.
- **NotificationHub**: `Notify(userId, payload)` khi có invite/application/payment/low-balance.

---

## 9) Swagger & Error Handling

- **Chuẩn hoá JSON response** theo 1 envelope thống nhất (success & error). Xem chi tiết triển khai ở **mục 14**.
- Swagger hiển thị Bearer JWT và có thể thử trực tiếp.
- Tất cả lỗi (validation/401/403/exception) đều trả về **envelope** thay vì HTML hay ProblemDetails mặc định.

**Mẫu envelope**

```json
// Success
{ "success": true, "data": { /* ... */ }, "meta": null, "traceId": "..." }

// Error
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR|UNAUTHORIZED|FORBIDDEN|USER_ALREADY_EXISTS|INTERNAL_SERVER_ERROR",
    "message": "...",
    "fields": { "Email": ["'Email' is not a valid email address."] },
    "details": null
  },
  "traceId": "..."
}
```

---

## 10) Lộ trình triển khai MVP (tuần tự)

1. Auth (register/login, JWT) + Users collection + Swagger.
2. Companies (CRUD + services embed) + search/pagination.
3. Drivers (profile + apply + invitations) + transactions (mock) + wallets.
4. Ratings + Admin user management.
5. SignalR Chat + Notifications.
6. React pages (Auth, Company list/detail, Driver openings, Admin users) + wiring API + Toastify.

> Khi cần mở rộng Phase 2: booking realtime, định vị bản đồ, tính giá cước động, Stripe.

---

## 11) Checklist QA nhanh

-

---

## 12) Backend — Step-by-step coding guide (paste & run)

### 12.1 Chuẩn bị môi trường

- **.NET SDK**: 8.0
- **MongoDB**: cài local hoặc Docker:
  ```bash
  docker run -d --name mycabs-mongo -p 27017:27017 mongo:7
  ```

### 12.2 Tạo solution & projects

```bash
mkdir mycabs && cd mycabs
mkdir src

# Solution
dotnet new sln -n MyCabs

# Projects
dotnet new webapi -o src/MyCabs.Api
dotnet new classlib -o src/MyCabs.Application
dotnet new classlib -o src/MyCabs.Domain
dotnet new classlib -o src/MyCabs.Infrastructure

# Add to solution
dotnet sln add src/*/*.csproj

# References
dotnet add src/MyCabs.Api/MyCabs.Api.csproj reference src/MyCabs.Application/MyCabs.Application.csproj src/MyCabs.Infrastructure/MyCabs.Infrastructure.csproj src/MyCabs.Domain/MyCabs.Domain.csproj
```

### 12.3 Cài NuGet packages

```bash
# API
dotnet add src/MyCabs.Api package Swashbuckle.AspNetCore
dotnet add src/MyCabs.Api package Microsoft.AspNetCore.Authentication.JwtBearer

# Security & tools
dotnet add src/MyCabs.Api package BCrypt.Net-Next

# Validation
dotnet add src/MyCabs.Application package FluentValidation
dotnet add src/MyCabs.Api package FluentValidation.AspNetCore

# MongoDB
dotnet add src/MyCabs.Infrastructure package MongoDB.Driver
```

> (Tùy chọn sau này) SignalR, AutoMapper có thể thêm sau khi cần realtime và mapping.

---

### 12.4 Cấu hình **Domain** (src/MyCabs.Domain)

**Entities/User.cs**

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

public class User {
    [BsonId]
    public ObjectId Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = "Rider"; // Admin|Rider|Driver|Company
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**Interfaces/IUserRepository.cs**

````csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IUserRepository {
    Task<User?> FindByEmailAsync(string email);
    Task CreateAsync(User user);
    // Cho phép khởi tạo index khi startup
    Task EnsureIndexesAsync();
}
```csharp
using MyCabs.Domain.Entities;

enamespace MyCabs.Domain.Interfaces;

public interface IUserRepository {
    Task<User?> FindByEmailAsync(string email);
    Task CreateAsync(User user);
}
````

> Bạn có thể tạo thêm `Company`, `Driver`… sau, nhưng skeleton Auth cần `User` trước.

---

### 12.5 **Infrastructure** (Mongo + Repository)

**Settings/MongoSettings.cs**

```csharp
namespace MyCabs.Infrastructure.Settings;

public class MongoSettings {
    public string ConnectionString { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
}
```

**Persistence/MongoContext.cs**

```csharp
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MyCabs.Infrastructure.Settings;

namespace MyCabs.Infrastructure.Persistence;

public interface IMongoContext {
    IMongoCollection<T> GetCollection<T>(string name);
}

public class MongoContext : IMongoContext {
    private readonly IMongoDatabase _db;
    public MongoContext(IOptions<MongoSettings> opts) {
        var client = new MongoClient(opts.Value.ConnectionString);
        _db = client.GetDatabase(opts.Value.Database);
    }
    public IMongoCollection<T> GetCollection<T>(string name) => _db.GetCollection<T>(name);
}
```

**Repositories/UserRepository.cs**

```csharp
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;

namespace MyCabs.Infrastructure.Repositories;

public class UserRepository : IUserRepository {
    private readonly IMongoCollection<User> _col;
    public UserRepository(IMongoContext ctx) {
        _col = ctx.GetCollection<User>("users");
    }

    // Trả về User? nên viết async/await để khớp nullability
    public async Task<User?> FindByEmailAsync(string email)
    {
        User? user = await _col
            .Find(x => x.Email == email)
            .FirstOrDefaultAsync();
        return user;
    }

    public Task CreateAsync(User u)
        => _col.InsertOneAsync(u);

    public async Task EnsureIndexesAsync() {
        var ix = new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true }
        );
        await _col.Indexes.CreateOneAsync(ix);
    }
}
```

csharp using MongoDB.Driver; using MyCabs.Domain.Entities; using MyCabs.Domain.Interfaces; using MyCabs.Infrastructure.Persistence;

namespace MyCabs.Infrastructure.Repositories;

public class UserRepository : IUserRepository { private readonly IMongoCollection \_col; public UserRepository(IMongoContext ctx) { \_col = ctx.GetCollection("users"); } public Task\<User?> FindByEmailAsync(string email) => \_col.Find(x => x.Email == email).FirstOrDefaultAsync();

```
public Task CreateAsync(User u)
    => _col.InsertOneAsync(u);

public async Task EnsureIndexesAsync() {
    var ix = new CreateIndexModel<User>(Builders<User>.IndexKeys.Ascending(u => u.Email), new CreateIndexOptions { Unique = true });
    await _col.Indexes.CreateOneAsync(ix);
}
```

}

````

**Startup helpers/DbInitializer.cs**
```csharp
using MyCabs.Domain.Interfaces;

namespace MyCabs.Infrastructure.Startup;

public class DbInitializer {
    private readonly IUserRepository _users;
    public DbInitializer(IUserRepository users) { _users = users; }
    public async Task EnsureIndexesAsync() {
        await _users.EnsureIndexesAsync();
    }
}
```csharp
using MyCabs.Infrastructure.Repositories;

namespace MyCabs.Infrastructure.Startup;

public class DbInitializer {
    private readonly UserRepository _users;
    public DbInitializer(UserRepository users) { _users = users; }
    public async Task EnsureIndexesAsync() {
        await _users.EnsureIndexesAsync();
    }
}
````

---

### 12.6 **Application** (DTO, Validation, Services)

**DTOs/AuthDtos.cs**

```csharp
namespace MyCabs.Application.DTOs;

public record RegisterDto(string Email, string Password, string FullName, string Role);
public record LoginDto(string Email, string Password);
```

**Validation/AuthValidators.cs**

```csharp
using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class RegisterDtoValidator : AbstractValidator<RegisterDto> {
    public RegisterDtoValidator() {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password)
            .NotEmpty().MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Password needs an uppercase letter")
            .Matches("[a-z]").WithMessage("Password needs a lowercase letter")
            .Matches("[0-9]").WithMessage("Password needs a digit");
        RuleFor(x => x.FullName).NotEmpty();
        RuleFor(x => x.Role).NotEmpty().Must(r => new[]{"Admin","Rider","Driver","Company"}.Contains(r));
    }
}

public class LoginDtoValidator : AbstractValidator<LoginDto> {
    public LoginDtoValidator() {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
```

**Services/AuthService.cs**

```csharp
using BCrypt.Net;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IAuthService {
    Task<(bool ok, string? error, string? token)> RegisterAsync(RegisterDto dto);
    Task<(bool ok, string? error, string? token)> LoginAsync(LoginDto dto);
}

public class AuthService : IAuthService {
    private readonly IUserRepository _users;
    private readonly IJwtTokenService _jwt;
    public AuthService(IUserRepository users, IJwtTokenService jwt) { _users = users; _jwt = jwt; }

    public async Task<(bool ok, string? error, string? token)> RegisterAsync(RegisterDto dto) {
        var exists = await _users.FindByEmailAsync(dto.Email.Trim().ToLower());
        if (exists != null) return (false, "Email already exists", null);
        var user = new User {
            Email = dto.Email.Trim().ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FullName = dto.FullName,
            Role = dto.Role,
        };
        await _users.CreateAsync(user);
        var token = _jwt.Generate(user);
        return (true, null, token);
    }

    public async Task<(bool ok, string? error, string? token)> LoginAsync(LoginDto dto) {
        var user = await _users.FindByEmailAsync(dto.Email.Trim().ToLower());
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return (false, "Invalid credentials", null);
        if (!user.IsActive) return (false, "Account deactivated", null);
        var token = _jwt.Generate(user);
        return (true, null, token);
    }
}
```

**Jwt/IJwtTokenService.cs** (Application)

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Application;

public interface IJwtTokenService {
    string Generate(User user);
}
```

---

### 12.7 **API** (Program + Controllers + JWT impl)

**appsettings.Development.json** (trong `src/MyCabs.Api`)

```json
{
  "Mongo": { "ConnectionString": "mongodb://localhost:27017", "Database": "mycabs" },
  "Jwt": {
    "Issuer": "mycabs.local",
    "Audience": "mycabs.local",
    "Key": "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET",
    "AccessTokenMinutes": 120
  },
  "AllowedHosts": "*"
}
```

**Program.cs** (thay nội dung mặc định)

```csharp
using System.Text;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MyCabs.Application;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;
using MyCabs.Application.Validation;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Repositories;
using MyCabs.Infrastructure.Settings;
using MyCabs.Infrastructure.Startup;
using MyCabs.Api.Jwt;

var builder = WebApplication.CreateBuilder(args);

// Controllers + FluentValidation
builder.Services.AddControllers();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterDtoValidator>();

// Swagger + JWT Bearer in UI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c => {
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MyCabs API", Version = "v1" });
    var securityScheme = new OpenApiSecurityScheme {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    };
    c.AddSecurityDefinition("Bearer", securityScheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement {{ securityScheme, new string[] { } }});
});

// CORS for frontend
builder.Services.AddCors(o => o.AddPolicy("ui", p => p
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
));

// Options
builder.Services.Configure<MongoSettings>(builder.Configuration.GetSection("Mongo"));

// Mongo + repos
builder.Services.AddSingleton<IMongoContext, MongoContext>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<DbInitializer>();

// JWT
var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => {
        o.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
    });
builder.Services.AddAuthorization();

// Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

var app = builder.Build();

// Init indexes
using (var scope = app.Services.CreateScope()) {
    var init = scope.ServiceProvider.GetRequiredService<DbInitializer>();
    await init.EnsureIndexesAsync();
}

if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("ui");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
```

**Jwt/JwtTokenService.cs** (trong `src/MyCabs.Api`)

````csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MyCabs.Application;
using MyCabs.Domain.Entities;

namespace MyCabs.Api.Jwt;

public class JwtTokenService : IJwtTokenService {
    private readonly IConfiguration _cfg;
    public JwtTokenService(IConfiguration cfg) { _cfg = cfg; }
    public string Generate(User user) {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(int.Parse(_cfg["Jwt:AccessTokenMinutes"]!));

        var claims = new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"],
            audience: _cfg["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MyCabs.Application;
using MyCabs.Domain.Entities;

public class JwtTokenService : IJwtTokenService {
    private readonly IConfiguration _cfg;
    public JwtTokenService(IConfiguration cfg) { _cfg = cfg; }
    public string Generate(User user) {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(int.Parse(_cfg["Jwt:AccessTokenMinutes"]!));

        var claims = new List<Claim> {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Role, user.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"],
            audience: _cfg["Jwt:Audience"],
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
````

**Controllers/AuthController.cs**

```csharp
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase {
    private readonly IAuthService _svc;
    public AuthController(IAuthService svc) { _svc = svc; }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto) {
        var (ok, err, token) = await _svc.RegisterAsync(dto);
        if (!ok) return Conflict(new { message = err });
        return Ok(new { accessToken = token });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto) {
        var (ok, err, token) = await _svc.LoginAsync(dto);
        if (!ok) return Unauthorized(new { message = err });
        return Ok(new { accessToken = token });
    }
}
```

---

### 12.8 Chạy thử & kiểm tra nhanh

```bash
# Terminal 1: Mongo (nếu dùng Docker)
docker start mycabs-mongo

# Terminal 2: API
cd src/MyCabs.Api
dotnet run
```

- Mở Swagger: `https://localhost:5001/swagger` (hoặc `http://localhost:5000/swagger`)
- **Register** → copy `accessToken`
- Bấm nút **Authorize** (Swagger), dán `Bearer <token>`
- Gọi các API yêu cầu auth khác (khi bạn thêm sau).

> Lỗi email trùng: sẽ trả 409; sai mật khẩu: 401.

---

### 12.9 Ghi chú mở rộng (sau Auth)

- Tạo `Company` entity + repo + service + controller (`GET /companies`, `GET /companies/{id}`, `POST /companies/{id}/services`), thêm `[Authorize(Roles="Company,Admin")]`.
- Tạo `Driver` entity + `applications`, `invitations`, `transactions` theo blueprint.
- Wrapper response cho pagination: `{ items, page, pageSize, total }`.
- Middleware error handling: try-catch toàn cục để chuẩn hóa lỗi.

---

## 13) Tiếp theo muốn mình làm gì?

- Thêm **CompanyService** + **CompanyController** mẫu (CRUD + services embed)?
- Thêm **Transactions** (mock flow: membership/salary)?
- Viết **Postman Collection** sẵn request?

## 14) Chuẩn hoá response (Unified API Envelope)

### 14.1 Tạo envelope chung

**File**: `src/MyCabs.Api/Common/ApiEnvelope.cs`

```csharp
namespace MyCabs.Api.Common;

public record ApiError(string Code, string Message, IDictionary<string, string[]>? Fields = null, object? Details = null);

public record ApiEnvelope(
    bool Success,
    object? Data,
    ApiError? Error,
    string TraceId,
    object? Meta = null
)
{
    public static ApiEnvelope Ok(HttpContext ctx, object? data = null, object? meta = null)
        => new(true, data, null, ctx.TraceIdentifier, meta);

    public static ApiEnvelope Fail(
        HttpContext ctx,
        string code,
        string message,
        int statusCode,
        IDictionary<string, string[]>? fields = null,
        object? details = null
    )
        => new(false, null, new ApiError(code, message, fields, details), ctx.TraceIdentifier);
}
```

> Lưu ý: status code được đặt ở **controller/middleware**, `ApiEnvelope.Fail(...)` chỉ tạo body JSON.

---

### 14.2 Bắt exception toàn cục → trả envelope

**File**: `src/MyCabs.Api/Middleware/ExceptionHandlingMiddleware.cs`

```csharp
using MyCabs.Api.Common;

namespace MyCabs.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IWebHostEnvironment _env;
    public ExceptionHandlingMiddleware(RequestDelegate next, IWebHostEnvironment env)
    { _next = next; _env = env; }

    public async Task Invoke(HttpContext context)
    {
        try { await _next(context); }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var details = _env.IsDevelopment() ? new { ex.Message, ex.StackTrace } : null;
            var payload = ApiEnvelope.Fail(context, "INTERNAL_SERVER_ERROR", "Something went wrong", 500, null, details);
            await context.Response.WriteAsJsonAsync(payload);
        }
    }
}
```

**Đăng ký middleware** (trước `UseHttpsRedirection`): sửa `Program.cs`

```csharp
using MyCabs.Api.Middleware;
using MyCabs.Api.Common; // dùng ở các đoạn dưới

// ...
var app = builder.Build();

// Init indexes ...

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("ui");
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();
```

---

### 14.3 Chuẩn hoá lỗi **Validation** (400) thay cho ProblemDetails

Trong `Program.cs` thêm cấu hình `ApiBehaviorOptions`:

```csharp
using Microsoft.AspNetCore.Mvc;
using MyCabs.Api.Common;

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = ctx =>
    {
        var fields = ctx.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid" : e.ErrorMessage).ToArray()
            );
        var env = ApiEnvelope.Fail(ctx.HttpContext, "VALIDATION_ERROR", "One or more validation errors occurred.", 400, fields);
        return new BadRequestObjectResult(env);
    };
});
```

---

### 14.4 Chuẩn hoá **401/403** từ JWT

Thêm `Events` cho `JwtBearer` trong `Program.cs` để trả envelope khi thiếu/sai token hoặc cấm truy cập:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MyCabs.Api.Common;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // ... TokenValidationParameters như trước
        o.Events = new JwtBearerEvents
        {
            OnChallenge = ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                var body = ApiEnvelope.Fail(ctx.HttpContext, "UNAUTHORIZED", "Authentication is required", 401);
                return ctx.Response.WriteAsJsonAsync(body);
            },
            OnForbidden = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "application/json";
                var body = ApiEnvelope.Fail(ctx.HttpContext, "FORBIDDEN", "You do not have permission to access this resource", 403);
                return ctx.Response.WriteAsJsonAsync(body);
            }
        };
    });
```

---

### 14.5 Cập nhật Controllers dùng envelope

Ví dụ **AuthController**:

```csharp
using MyCabs.Api.Common;

[HttpPost("register")]
public async Task<IActionResult> Register([FromBody] RegisterDto dto)
{
    var (ok, err, token) = await _svc.RegisterAsync(dto);
    if (!ok) return Conflict(ApiEnvelope.Fail(HttpContext, "USER_ALREADY_EXISTS", err!, 409));
    return Ok(ApiEnvelope.Ok(HttpContext, new { accessToken = token }));
}

[HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginDto dto)
{
    var (ok, err, token) = await _svc.LoginAsync(dto);
    if (!ok)
    {
        var code = err == "Account deactivated" ? "ACCOUNT_DEACTIVATED" : "INVALID_CREDENTIALS";
        var status = err == "Account deactivated" ? 403 : 401;
        return StatusCode(status, ApiEnvelope.Fail(HttpContext, code, err!, status));
    }
    return Ok(ApiEnvelope.Ok(HttpContext, new { accessToken = token }));
}
```

---

### 14.6 Pagination envelope helper (tuỳ chọn)

**File**: `src/MyCabs.Api/Common/PagedResult.cs`

```csharp
namespace MyCabs.Api.Common;

public record PagedResult<T>(IEnumerable<T> Items, int Page, int PageSize, long Total);
```

Khi trả về danh sách:

```csharp
return Ok(ApiEnvelope.Ok(HttpContext, new PagedResult<CompanyDto>(items, page, pageSize, total)));
```

---

### 14.7 Ví dụ response sau khi chuẩn hoá

**Success**

```json
{
  "success": true,
  "data": { "accessToken": "<JWT>" },
  "meta": null,
  "traceId": "0HMPK...."
}
```

**Validation error (400)**

```json
{
  "success": false,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "One or more validation errors occurred.",
    "fields": { "Email": ["'Email' is not a valid email address."] },
    "details": null
  },
  "traceId": "0HMPK...."
}
```

**Unauthorized (401)**

```json
{
  "success": false,
  "error": { "code": "UNAUTHORIZED", "message": "Authentication is required", "fields": null, "details": null },
  "traceId": "0HMPK...."
}
```

**Internal error (500)**

```json
{
  "success": false,
  "error": { "code": "INTERNAL_SERVER_ERROR", "message": "Something went wrong", "fields": null, "details": null },
  "traceId": "0HMPK...."
}
```

---

## 15) Cập nhật Checklist QA (liên quan envelope)

-

