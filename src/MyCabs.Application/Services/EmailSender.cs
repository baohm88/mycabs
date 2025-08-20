namespace MyCabs.Application.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, string? textBody = null);
}