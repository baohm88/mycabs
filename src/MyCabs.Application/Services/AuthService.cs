using BCrypt.Net;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;
using MongoDB.Bson;

namespace MyCabs.Application.Services;

public interface IAuthService
{
    Task<(bool ok, string? error, string? token)> RegisterAsync(RegisterDto dto);
    Task<(bool ok, string? error, string? token)> LoginAsync(LoginDto dto);
}

public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IJwtTokenService _jwt;
    public AuthService(IUserRepository users, IJwtTokenService jwt) { _users = users; _jwt = jwt; }

    public async Task<(bool ok, string? error, string? token)> RegisterAsync(RegisterDto dto)
    {
        // 1) chuẩn hoá email nhập vào
        var emailLower = dto.Email.Trim().ToLowerInvariant();

        // 2) kiểm tra tồn tại theo EmailLower
        var existed = await _users.GetByEmailAsync(emailLower);
        if (existed != null) return (false, "Email already exists", null);
        var user = new User
        {
            Id = ObjectId.GenerateNewId(),
            Email = dto.Email.Trim(),
            EmailLower = emailLower,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, 11),
            FullName = dto.FullName?.Trim() ?? "",
            Role = dto.Role,
            EmailVerified = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _users.CreateAsync(user);
        var token = _jwt.Generate(user);
        return (true, null, token);
    }

    public async Task<(bool ok, string? error, string? token)> LoginAsync(LoginDto dto)
    {
        var emailLower = dto.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(emailLower);
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return (false, "Invalid credentials", null);
        if (!user.IsActive) return (false, "Account deactivated", null);
        var token = _jwt.Generate(user);
        return (true, null, token);
    }
}