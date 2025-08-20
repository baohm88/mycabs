using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyCabs.Application.Services;

namespace MyCabs.Api.Email;

public class DevConsoleEmailSender : IEmailSender
{
    private readonly ILogger<DevConsoleEmailSender> _logger;
    private readonly IConfiguration _cfg;

    public DevConsoleEmailSender(ILogger<DevConsoleEmailSender> logger, IConfiguration cfg)
    {
        _logger = logger;
        _cfg = cfg;
    }

    public Task SendAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        Console.WriteLine($"[EMAIL-DEV] To={to} | Subject={subject} | Body={textBody ?? htmlBody}");
        _logger.LogInformation("[EMAIL-DEV] To={To} | Subject={Subject} | Body={Body}", to, subject, textBody ?? htmlBody);
        return Task.CompletedTask;
    }

}
