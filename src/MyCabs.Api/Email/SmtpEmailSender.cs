// using System.Net;
// using System.Net.Mail;
// using MyCabs.Application.Services;

// namespace MyCabs.Api.Email;

// public class SmtpEmailSender : IEmailSender
// {
//     private readonly IConfiguration _cfg;
//     public SmtpEmailSender(IConfiguration cfg) { _cfg = cfg; }

//     public async Task SendAsync(string to, string subject, string htmlBody, string? textBody = null)
//     {
//         var host = _cfg["Email:Smtp:Host"]!;
//         var port = int.Parse(_cfg["Email:Smtp:Port"] ?? "587");
//         var user = _cfg["Email:Smtp:User"];
//         var pass = _cfg["Email:Smtp:Pass"];
//         var fromAddr = _cfg["Email:FromAddress"] ?? "noreply@mycabs.local";
//         var fromName = _cfg["Email:FromName"] ?? "MyCabs";

//         using var client = new SmtpClient(host, port) { EnableSsl = bool.Parse(_cfg["Email:Smtp:EnableSsl"] ?? "true") };
//         if (!string.IsNullOrEmpty(user)) client.Credentials = new NetworkCredential(user, pass);
//         using var msg = new MailMessage() { From = new MailAddress(fromAddr, fromName), Subject = subject, Body = htmlBody, IsBodyHtml = true };
//         msg.To.Add(to);
//         await client.SendMailAsync(msg);
//     }
// }


using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyCabs.Application.Services;

namespace MyCabs.Api.Email;

// UPDATED: thêm ILogger + cấu hình SmtpClient rõ ràng + log lỗi
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IConfiguration cfg, ILogger<SmtpEmailSender> log)
    {
        _cfg = cfg;
        _log = log;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        var host = _cfg["Email:Smtp:Host"]!;
        var port = int.TryParse(_cfg["Email:Smtp:Port"], out var p) ? p : 587;
        var user = _cfg["Email:Smtp:User"];
        var pass = _cfg["Email:Smtp:Pass"];
        // UPDATED: FromAddress mặc định trùng user để tránh bị chặn bởi Gmail/DMARC
        var fromAddr = _cfg["Email:FromAddress"] ?? user ?? "noreply@mycabs.local";
        var fromName = _cfg["Email:FromName"] ?? "MyCabs";
        var enableSsl = bool.TryParse(_cfg["Email:Smtp:EnableSsl"], out var ssl) ? ssl : true;

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Timeout = 15000
        };
        if (!string.IsNullOrEmpty(user))
            client.Credentials = new NetworkCredential(user, pass);

        using var msg = new MailMessage
        {
            From = new MailAddress(fromAddr, fromName),
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
            Body = htmlBody
        };
        msg.To.Add(to);
        if (!string.IsNullOrWhiteSpace(textBody))
        {
            msg.AlternateViews.Add(
                AlternateView.CreateAlternateViewFromString(textBody, Encoding.UTF8, "text/plain"));
        }

        try
        {
            await client.SendMailAsync(msg);
            _log.LogInformation("[SMTP] Sent email to {To} subject={Subject}", to, subject);
        }
        catch (SmtpException ex)
        {
            _log.LogError(ex, "[SMTP] Send failed Host={Host}:{Port} User={User}", host, port, user);
            throw; // để controller/logic phía trên biết gửi thất bại
        }
    }
}
