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
        var doc = new EmailOtp
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId(),
            EmailLower = emailLower,
            Purpose = dto.Purpose,
            CodeHash = hash,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
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