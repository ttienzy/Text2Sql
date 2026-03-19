using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace TextToSqlAgent.API.Services;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string toName, string code);
}

/// <summary>
/// Email service using MailKit + Gmail SMTP (configured via .env)
/// </summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendPasswordResetEmailAsync(string toEmail, string toName, string code)
    {
        var senderName  = Environment.GetEnvironmentVariable("SMTP_SENDER_NAME")  ?? _config["Smtp:SenderName"]  ?? "TextToSQL Agent";
        var senderEmail = Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL") ?? _config["Smtp:SenderEmail"] ?? "";
        var smtpHost    = Environment.GetEnvironmentVariable("SMTP_SERVER")       ?? _config["Smtp:Server"]      ?? "smtp.gmail.com";
        var smtpPort    = int.TryParse(Environment.GetEnvironmentVariable("SMTP_PORT") ?? _config["Smtp:Port"], out var p) ? p : 587;
        var username    = Environment.GetEnvironmentVariable("SMTP_USERNAME")     ?? _config["Smtp:Username"]    ?? "";
        var password    = Environment.GetEnvironmentVariable("SMTP_PASSWORD")     ?? _config["Smtp:Password"]    ?? "";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, senderEmail));
        message.To.Add(new MailboxAddress(toName, toEmail));
        message.Subject = "Your Password Reset Code";

        message.Body = new TextPart("html")
        {
            Text = $@"
<!DOCTYPE html>
<html>
<body style=""font-family:Inter,Arial,sans-serif;background:#f5f5f5;margin:0;padding:0"">
  <div style=""max-width:480px;margin:40px auto;background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,.08)"">
    <div style=""background:linear-gradient(135deg,#1890ff,#096dd9);padding:32px;text-align:center"">
      <h1 style=""color:#fff;margin:0;font-size:22px"">TextToSQL Agent</h1>
      <p style=""color:rgba(255,255,255,.85);margin:8px 0 0"">Password Reset</p>
    </div>
    <div style=""padding:40px 32px"">
      <p style=""color:#333;font-size:15px"">Hi <strong>{toName}</strong>,</p>
      <p style=""color:#555;font-size:14px"">Use the code below to reset your password. This code expires in <strong>15 minutes</strong>.</p>
      <div style=""background:#f0f7ff;border:2px dashed #1890ff;border-radius:8px;padding:24px;text-align:center;margin:24px 0"">
        <span style=""font-size:40px;font-weight:700;letter-spacing:12px;color:#1890ff"">{code}</span>
      </div>
      <p style=""color:#999;font-size:12px"">If you did not request a password reset, you can safely ignore this email.</p>
    </div>
    <div style=""padding:16px 32px;background:#fafafa;text-align:center"">
      <p style=""color:#bbb;font-size:11px;margin:0"">© 2025 TextToSQL Agent. All rights reserved.</p>
    </div>
  </div>
</body>
</html>"
        };

        using var smtp = new SmtpClient();
        try
        {
            await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(username, password);
            await smtp.SendAsync(message);
            _logger.LogInformation("Password reset email sent to {Email}", toEmail);
        }
        finally
        {
            await smtp.DisconnectAsync(true);
        }
    }
}
