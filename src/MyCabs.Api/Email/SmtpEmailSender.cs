using System.Net;
using System.Net.Mail;
using MyCabs.Application.Services;

namespace MyCabs.Api.Email;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _cfg;
    public SmtpEmailSender(IConfiguration cfg) { _cfg = cfg; }

    public async Task SendAsync(string to, string subject, string htmlBody, string? textBody = null)
    {
        var host = _cfg["Email:Smtp:Host"]!;
        var port = int.Parse(_cfg["Email:Smtp:Port"] ?? "587");
        var user = _cfg["Email:Smtp:User"];
        var pass = _cfg["Email:Smtp:Pass"];
        var fromAddr = _cfg["Email:FromAddress"] ?? "noreply@mycabs.local";
        var fromName = _cfg["Email:FromName"] ?? "MyCabs";

        using var client = new SmtpClient(host, port) { EnableSsl = bool.Parse(_cfg["Email:Smtp:EnableSsl"] ?? "true") };
        if (!string.IsNullOrEmpty(user)) client.Credentials = new NetworkCredential(user, pass);
        using var msg = new MailMessage() { From = new MailAddress(fromAddr, fromName), Subject = subject, Body = htmlBody, IsBodyHtml = true };
        msg.To.Add(to);
        await client.SendMailAsync(msg);
    }
}