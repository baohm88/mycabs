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