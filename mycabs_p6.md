# MyCabs – Email OTP (Verify & Reset) MVP

> Module OTP qua email phục vụ **xác minh email** sau đăng ký và **đặt lại mật khẩu**. Thiết kế theo Clean Architecture: Application không phụ thuộc ASP.NET, gửi email qua interface `IEmailSender` (có DevConsole & SMTP).

---

## 0) Luồng tổng quan

1. **Request OTP**: client gửi email + purpose (`verify_email` | `reset_password`). Server tạo **mã 6 chữ số**, **băm bằng BCrypt**, lưu MongoDB với TTL 5 phút, gửi email.
2. **Verify OTP**: client gửi lại email + mã. Server kiểm tra (hết hạn/đã dùng/sai quá 5 lần), nếu hợp lệ thì **consume**. Với `verify_email` → set `EmailVerified=true` cho user.
3. **Reset password (nếu dùng OTP đặt lại mật khẩu)**: client gọi `/reset-password` kèm email + OTP + `newPassword` → verify & update hash.

---

## 1) Domain (Entity)

**Path:** `src/MyCabs.Domain/Entities/EmailOtp.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace MyCabs.Domain.Entities;

[BsonIgnoreExtraElements]
public class EmailOtp
{
    [BsonId] public ObjectId Id { get; set; }

    [BsonElement("emailLower")] public string EmailLower { get; set; } = string.Empty; // lưu lowercase
    [BsonElement("purpose")] public string Purpose { get; set; } = "verify_email";    // verify_email | reset_password

    [BsonElement("codeHash")] public string CodeHash { get; set; } = string.Empty;      // BCrypt hash
    [BsonElement("expiresAt")] public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(5);

    [BsonElement("attemptCount")] public int AttemptCount { get; set; } = 0;            // tăng khi verify sai
    [BsonElement("consumedAt")] public DateTime? ConsumedAt { get; set; }               // null = chưa dùng

    [BsonElement("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [BsonElement("updatedAt")] public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

---

## 2) Domain (Interfaces)

**Path:** `src/MyCabs.Domain/Interfaces/IEmailOtpRepository.cs`

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public interface IEmailOtpRepository
{
    Task InsertAsync(EmailOtp doc);
    Task<EmailOtp?> GetLatestActiveAsync(string emailLower, string purpose);
    Task<bool> ConsumeAsync(string id);
    Task IncrementAttemptAsync(string id);
    Task EnsureIndexesAsync();
}
```

> (Patch) **User repo** – cần một vài method phục vụ OTP.

**Path:** `src/MyCabs.Domain/Interfaces/IUserRepository.cs` (thêm method)

```csharp
using MyCabs.Domain.Entities;

namespace MyCabs.Domain.Interfaces;

public partial interface IUserRepository
{
    Task<User?> GetByEmailAsync(string emailLower);
    Task<bool> SetEmailVerifiedAsync(string emailLower);
    Task<bool> UpdatePasswordHashAsync(string emailLower, string newHash);
}
```

> Nếu file của bạn không dùng `partial`, có thể thêm trực tiếp 3 method này vào interface đang có.

---

## 3) Infrastructure (Repositories)

**Path:** `src/MyCabs.Infrastructure/Repositories/EmailOtpRepository.cs`

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MyCabs.Infrastructure.Persistence;
using MyCabs.Infrastructure.Startup;

namespace MyCabs.Infrastructure.Repositories;

public class EmailOtpRepository : IEmailOtpRepository, IIndexInitializer
{
    private readonly IMongoCollection<EmailOtp> _col;
    public EmailOtpRepository(IMongoContext ctx) => _col = ctx.GetCollection<EmailOtp>("email_otps");

    public Task InsertAsync(EmailOtp doc) => _col.InsertOneAsync(doc);

    public async Task<EmailOtp?> GetLatestActiveAsync(string emailLower, string purpose)
    {
        var now = DateTime.UtcNow;
        var f = Builders<EmailOtp>.Filter.Eq(x => x.EmailLower, emailLower)
              & Builders<EmailOtp>.Filter.Eq(x => x.Purpose, purpose)
              & Builders<EmailOtp>.Filter.Gt(x => x.ExpiresAt, now)
              & Builders<EmailOtp>.Filter.Eq(x => x.ConsumedAt, null);
        return await _col.Find(f).SortByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
    }

    public async Task<bool> ConsumeAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return false;
        var upd = Builders<EmailOtp>.Update.Set(x => x.ConsumedAt, DateTime.UtcNow).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _col.UpdateOneAsync(x => x.Id == oid && x.ConsumedAt == null, upd);
        return res.ModifiedCount > 0;
    }

    public async Task IncrementAttemptAsync(string id)
    {
        if (!ObjectId.TryParse(id, out var oid)) return;
        var upd = Builders<EmailOtp>.Update.Inc(x => x.AttemptCount, 1).Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _col.UpdateOneAsync(x => x.Id == oid, upd);
    }

    public async Task EnsureIndexesAsync()
    {
        // TTL xoá doc sau khi hết hạn ~10 phút buffer (TTL tính theo seconds, không chính xác tuyệt đối)
        var ttl = new CreateIndexModel<EmailOtp>(Builders<EmailOtp>.IndexKeys.Ascending(x => x.ExpiresAt), new CreateIndexOptions { ExpireAfter = TimeSpan.FromMinutes(10) });
        var combo = new CreateIndexModel<EmailOtp>(Builders<EmailOtp>.IndexKeys
            .Ascending(x => x.EmailLower).Ascending(x => x.Purpose).Descending(x => x.CreatedAt));
        await _col.Indexes.CreateManyAsync(new[] { ttl, combo });
    }
}
```

> (Patch) **UserRepository** – implement 3 method nếu chưa có.

**Path:** `src/MyCabs.Infrastructure/Repositories/UserRepository.cs` (chèn thêm vào trong class)

```csharp
using MongoDB.Bson;
using MongoDB.Driver;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

public partial class UserRepository : IUserRepository
{
    private readonly IMongoCollection<User> _users;

    public Task<User?> GetByEmailAsync(string emailLower)
        => _users.Find(x => x.EmailLower == emailLower).FirstOrDefaultAsync();

    public async Task<bool> SetEmailVerifiedAsync(string emailLower)
    {
        var upd = Builders<User>.Update.Set(x => x.EmailVerified, true).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _users.UpdateOneAsync(x => x.EmailLower == emailLower, upd);
        return res.ModifiedCount > 0;
    }

    public async Task<bool> UpdatePasswordHashAsync(string emailLower, string newHash)
    {
        var upd = Builders<User>.Update.Set(x => x.PasswordHash, newHash).Set(x => x.UpdatedAt, DateTime.UtcNow);
        var res = await _users.UpdateOneAsync(x => x.EmailLower == emailLower, upd);
        return res.ModifiedCount > 0;
    }
}
```

> Nếu class của bạn không phải `partial`, có thể dán trực tiếp các method này vào class hiện tại (không dùng `partial`). Trường `EmailLower` và `EmailVerified` nên tồn tại trong entity `User`.

---

## 4) Application (DTOs, Validators, EmailSender, Service)

**Path:** `src/MyCabs.Application/DTOs/OtpDtos.cs`

```csharp
namespace MyCabs.Application.DTOs;

public record RequestEmailOtpDto(string Email, string Purpose); // verify_email | reset_password
public record VerifyEmailOtpDto(string Email, string Purpose, string Code);
public record ResetPasswordWithOtpDto(string Email, string Code, string NewPassword);
```

**Path:** `src/MyCabs.Application/Validation/OtpValidators.cs`

```csharp
using FluentValidation;
using MyCabs.Application.DTOs;

namespace MyCabs.Application.Validation;

public class RequestEmailOtpValidator : AbstractValidator<RequestEmailOtpDto>
{
    public RequestEmailOtpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Purpose).NotEmpty().Must(p => p is "verify_email" or "reset_password");
    }
}

public class VerifyEmailOtpValidator : AbstractValidator<VerifyEmailOtpDto>
{
    public VerifyEmailOtpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Purpose).NotEmpty().Must(p => p is "verify_email" or "reset_password");
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
    }
}

public class ResetPasswordWithOtpValidator : AbstractValidator<ResetPasswordWithOtpDto>
{
    public ResetPasswordWithOtpValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Code).NotEmpty().Length(6).Matches("^[0-9]{6}$");
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(6);
    }
}
```

**Path:** `src/MyCabs.Application/Services/EmailSender.cs`

```csharp
namespace MyCabs.Application.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, string? textBody = null);
}
```

**Path:** `src/MyCabs.Api/Email/DevConsoleEmailSender.cs`

```csharp
using Microsoft.Extensions.Logging;
using MyCabs.Application.Services;

namespace MyCabs.Api.Email;

public class DevConsoleEmailSender : IEmailSender
{
    private readonly ILogger<DevConsoleEmailSender> _logger;
    private readonly IConfiguration _cfg;
    public DevConsoleEmailSender(ILogger<DevConsoleEmailSender> logger, IConfiguration cfg){ _logger = logger; _cfg = cfg; }

    public Task SendAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        _logger.LogInformation("[EMAIL-DEV] To={To} | Subject={Subject}
{Body}", to, subject, textBody ?? htmlBody);
        return Task.CompletedTask;
    }
}
```

**(Optional)** SMTP sender nếu bạn có SMTP thật:

**Path:** `src/MyCabs.Api/Email/SmtpEmailSender.cs`

```csharp
using System.Net;
using System.Net.Mail;
using MyCabs.Application.Services;

namespace MyCabs.Api.Email;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _cfg;
    public SmtpEmailSender(IConfiguration cfg){ _cfg = cfg; }

    public async Task SendAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        var host = _cfg["Email:Smtp:Host"]!;
        var port = int.Parse(_cfg["Email:Smtp:Port"] ?? "587");
        var user = _cfg["Email:Smtp:User"];
        var pass = _cfg["Email:Smtp:Pass"];
        var fromAddr = _cfg["Email:FromAddress"] ?? "noreply@mycabs.local";
        var fromName = _cfg["Email:FromName"] ?? "MyCabs";

        using var client = new SmtpClient(host, port){ EnableSsl = bool.Parse(_cfg["Email:Smtp:EnableSsl"] ?? "true") };
        if (!string.IsNullOrEmpty(user)) client.Credentials = new NetworkCredential(user, pass);
        using var msg = new MailMessage(){ From = new MailAddress(fromAddr, fromName), Subject = subject, Body = htmlBody, IsBodyHtml = true };
        msg.To.Add(to);
        await client.SendMailAsync(msg);
    }
}
```

**Path:** `src/MyCabs.Application/Services/EmailOtpService.cs`

```csharp
using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

namespace MyCabs.Application.Services;

public interface IEmailOtpService
{
    Task RequestAsync(RequestEmailOtpDto dto);
    Task<bool> VerifyAsync(VerifyEmailOtpDto dto);
    Task<bool> ResetPasswordWithOtpAsync(ResetPasswordWithOtpDto dto);
}

public class EmailOtpService : IEmailOtpService
{
    private readonly IEmailOtpRepository _repo;
    private readonly IUserRepository _users;
    private readonly IEmailSender _sender;

    public EmailOtpService(IEmailOtpRepository repo, IUserRepository users, IEmailSender sender)
    { _repo = repo; _users = users; _sender = sender; }

    public async Task RequestAsync(RequestEmailOtpDto dto)
    {
        var emailLower = dto.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower);
        if (user == null) throw new InvalidOperationException("EMAIL_NOT_FOUND");

        var code = GenerateCode6();
        var hash = BCrypt.Net.BCrypt.HashPassword(code, workFactor: 10);
        var doc = new EmailOtp{
            Id = MongoDB.Bson.ObjectId.GenerateNewId(),
            EmailLower = emailLower, Purpose = dto.Purpose,
            CodeHash = hash, ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await _repo.InsertAsync(doc);

        var subj = dto.Purpose == "reset_password" ? "MyCabs password reset code" : "MyCabs email verification code";
        var body = $"<p>Your code is: <b>{code}</b> (valid 5 minutes)</p>";
        await _sender.SendAsync(dto.Email, subj, body, $"Your code is: {code}");
    }

    public async Task<bool> VerifyAsync(VerifyEmailOtpDto dto)
    {
        var emailLower = dto.Email.Trim().ToLowerInvariant();
        var doc = await _repo.GetLatestActiveAsync(emailLower, dto.Purpose);
        if (doc == null || doc.ExpiresAt <= DateTime.UtcNow) return false;
        if (doc.AttemptCount >= 5) return false;

        var ok = BCrypt.Net.BCrypt.Verify(dto.Code, doc.CodeHash);
        if (!ok)
        {
            await _repo.IncrementAttemptAsync(doc.Id.ToString());
            return false;
        }

        await _repo.ConsumeAsync(doc.Id.ToString());
        if (dto.Purpose == "verify_email")
            await _users.SetEmailVerifiedAsync(emailLower);
        return true;
    }

    public async Task<bool> ResetPasswordWithOtpAsync(ResetPasswordWithOtpDto dto)
    {
        var ok = await VerifyAsync(new VerifyEmailOtpDto(dto.Email, "reset_password", dto.Code));
        if (!ok) return false;
        var emailLower = dto.Email.Trim().ToLowerInvariant();
        var newHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, workFactor: 11);
        return await _users.UpdatePasswordHashAsync(emailLower, newHash);
    }

    private static string GenerateCode6()
    {
        // sinh 6 chữ số ngẫu nhiên, bảo mật
        var bytes = RandomNumberGenerator.GetBytes(4);
        var num = BitConverter.ToUInt32(bytes, 0) % 1000000u;
        return num.ToString("D6");
    }
}
```

---

## 5) API (Controller)

**Path:** `src/MyCabs.Api/Controllers/OtpController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MyCabs.Application.DTOs;
using MyCabs.Application.Services;

namespace MyCabs.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class OtpController : ControllerBase
{
    private readonly IEmailOtpService _svc;
    public OtpController(IEmailOtpService svc){ _svc = svc; }

    [HttpPost("request")] // body: { email, purpose }
    public async Task<IActionResult> RequestOtp([FromBody] RequestEmailOtpDto dto)
    {
        try {
            await _svc.RequestAsync(dto);
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "OTP sent" }));
        } catch (InvalidOperationException ex) when (ex.Message == "EMAIL_NOT_FOUND") {
            // tránh lộ email tồn tại: vẫn trả OK
            return Ok(ApiEnvelope.Ok(HttpContext, new { message = "OTP sent" }));
        }
    }

    [HttpPost("verify")] // body: { email, purpose, code }
    public async Task<IActionResult> Verify([FromBody] VerifyEmailOtpDto dto)
    {
        var ok = await _svc.VerifyAsync(dto);
        if (!ok) return BadRequest(ApiEnvelope.Fail(HttpContext, "OTP_INVALID", "OTP invalid or expired", 400));
        return Ok(ApiEnvelope.Ok(HttpContext, new { verified = true }));
    }

    [HttpPost("reset-password")] // body: { email, code, newPassword }
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordWithOtpDto dto)
    {
        var ok = await _svc.ResetPasswordWithOtpAsync(dto);
        if (!ok) return BadRequest(ApiEnvelope.Fail(HttpContext, "OTP_INVALID", "OTP invalid/expired or user not found", 400));
        return Ok(ApiEnvelope.Ok(HttpContext, new { reset = true }));
    }
}
```

---

## 6) Program.cs (DI)

**Path:** `src/MyCabs.Api/Program.cs`

```csharp
using MyCabs.Application.Services;            // IEmailSender, IEmailOtpService
using MyCabs.Domain.Interfaces;               // IEmailOtpRepository, IUserRepository
using MyCabs.Infrastructure.Repositories;     // EmailOtpRepository
using MyCabs.Infrastructure.Startup;          // IIndexInitializer
using MyCabs.Api.Email;                       // DevConsoleEmailSender / SmtpEmailSender

// Email sender: chọn DevConsole cho môi trường dev
var emailProvider = builder.Configuration["Email:Provider"] ?? "DevConsole";
if (emailProvider == "Smtp")
    builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
else
    builder.Services.AddSingleton<IEmailSender, DevConsoleEmailSender>();

builder.Services.AddScoped<IEmailOtpRepository, EmailOtpRepository>();
builder.Services.AddScoped<IEmailOtpService, EmailOtpService>();

builder.Services.AddScoped<IIndexInitializer, EmailOtpRepository>();
```

---

## 7) appsettings.json (đoạn cấu hình)

**Path:** `src/MyCabs.Api/appsettings.Development.json`

```json
{
  "Email": {
    "Provider": "DevConsole", // DevConsole | Smtp
    "FromName": "MyCabs OTP",
    "FromAddress": "noreply@mycabs.local",
    "Smtp": {
      "Host": "smtp.gmail.com",
      "Port": 587,
      "User": "",
      "Pass": "",
      "EnableSsl": true
    }
  }
}
```

---

## 8) Test nhanh (Swagger/Postman)

1. **Request OTP**

```
POST /api/otp/request
{ "email": "<email đăng ký>", "purpose": "verify_email" }
```

- Dev mode: xem **console** của API sẽ log mã OTP (do `DevConsoleEmailSender`).

2. **Verify OTP**

```
POST /api/otp/verify
{ "email": "<email>", "purpose": "verify_email", "code": "123456" }
```

- Trả `{ verified: true }`.
- User sẽ được set `EmailVerified=true`.

3. **Reset password** (tuỳ chọn)

```
POST /api/otp/request
{ "email": "<email>", "purpose": "reset_password" }

POST /api/otp/reset-password
{ "email": "<email>", "code": "123456", "newPassword": "P@ss1234" }
```

- Trả `{ reset: true }` và thay `PasswordHash` của user.

---

## 9) Lưu ý & mở rộng

- Giới hạn **5 lần nhập sai** mỗi OTP (`AttemptCount >= 5`).
- Có thể **reuse** OTP còn hạn để tránh spam: tra `GetLatestActiveAsync`, nếu muốn có thể gửi lại cùng mã.
- Nên lưu thêm `lastSentAt`/`resendCount` để rate-limit.
- Email template có thể chuyển sang RazorClassLibrary sau này.
- Trên production, dùng `SmtpEmailSender` + secret từ vault.

