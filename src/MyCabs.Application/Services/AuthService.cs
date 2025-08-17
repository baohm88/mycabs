using BCrypt.Net;
using MyCabs.Application.DTOs;
using MyCabs.Domain.Entities;
using MyCabs.Domain.Interfaces;

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
        var exists = await _users.FindByEmailAsync(dto.Email.Trim().ToLower());
        if (exists != null) return (false, "Email already exists", null);
        var user = new User
        {
            Email = dto.Email.Trim().ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            FullName = dto.FullName,
            Role = dto.Role,
        };
        await _users.CreateAsync(user);
        var token = _jwt.Generate(user);
        return (true, null, token);
    }

    public async Task<(bool ok, string? error, string? token)> LoginAsync(LoginDto dto)
    {
        var user = await _users.FindByEmailAsync(dto.Email.Trim().ToLower());
        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return (false, "Invalid credentials", null);
        if (!user.IsActive) return (false, "Account deactivated", null);
        var token = _jwt.Generate(user);
        return (true, null, token);
    }
}