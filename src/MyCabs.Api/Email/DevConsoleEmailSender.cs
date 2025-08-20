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
        // Cách 1: một dòng
        _logger.LogInformation(
            "[EMAIL-DEV] To={To} | Subject={Subject} | Body={Body}",
            to, subject, textBody ?? htmlBody
        );

        // (Tuỳ chọn) Cách 2: xuống dòng bằng \n
        // _logger.LogInformation("[EMAIL-DEV] To={To} | Subject={Subject}\n{Body}", to, subject, textBody ?? htmlBody);

        // (Tuỳ chọn) Cách 3: chuỗi verbatim cho phép xuống dòng
        // _logger.LogInformation(@"[EMAIL-DEV] To={To} | Subject={Subject}
        // {Body}", to, subject, textBody ?? htmlBody);

        return Task.CompletedTask;
    }
}
