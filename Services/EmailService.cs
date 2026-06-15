using EBookDashboard.Interfaces;
using System.Net;
using System.Net.Mail;

namespace EBookDashboard.Services;

/// <summary>Sends transactional email using SMTP settings from configuration.</summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return false;

        if (!TryGetSettings(out var settings))
        {
            _logger.LogWarning("Email not sent to {Email}: SMTP is not configured (Email__SmtpServer / Email__FromEmail).", toEmail);
            return false;
        }

        try
        {
            using var client = new SmtpClient(settings.SmtpServer, settings.Port)
            {
                Credentials = new NetworkCredential(settings.Username, settings.Password),
                EnableSsl = settings.EnableSsl
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(settings.SenderEmail, settings.SenderName),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail.Trim());

            await client.SendMailAsync(mail);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
            return false;
        }
    }

    public bool IsConfigured => TryGetSettings(out _);

    private bool TryGetSettings(out EmailSmtpSettings settings)
    {
        settings = new EmailSmtpSettings();
        var smtpServer = FirstNonEmpty(
            _config["EmailSettings:SmtpServer"],
            _config["Email:SmtpServer"]);
        var senderEmail = FirstNonEmpty(
            _config["EmailSettings:SenderEmail"],
            _config["Email:FromEmail"],
            _config["EmailSettings:FromEmail"]);
        var portRaw = FirstNonEmpty(
            _config["EmailSettings:Port"],
            _config["Email:Port"]) ?? "587";
        var username = FirstNonEmpty(
            _config["EmailSettings:Username"],
            _config["Email:Username"]) ?? senderEmail ?? "";
        var password = FirstNonEmpty(
            _config["EmailSettings:Password"],
            _config["Email:Password"]) ?? "";
        var senderName = FirstNonEmpty(
            _config["EmailSettings:SenderName"],
            _config["Email:SenderName"]) ?? "eBook Publisher";
        var sslRaw = FirstNonEmpty(
            _config["EmailSettings:EnableSsl"],
            _config["EmailSettings:EnableSSL"],
            _config["Email:EnableSsl"],
            _config["Email:EnableSSL"]) ?? "true";

        if (string.IsNullOrWhiteSpace(smtpServer) || string.IsNullOrWhiteSpace(senderEmail))
            return false;

        if (!int.TryParse(portRaw, out var port))
            port = 587;

        settings = new EmailSmtpSettings
        {
            SmtpServer = smtpServer.Trim(),
            Port = port,
            SenderEmail = senderEmail.Trim(),
            SenderName = senderName.Trim(),
            Username = username.Trim(),
            Password = password,
            EnableSsl = !string.Equals(sslRaw, "false", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sslRaw, "0", StringComparison.OrdinalIgnoreCase)
        };
        return true;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }
        return null;
    }

    private sealed class EmailSmtpSettings
    {
        public string SmtpServer { get; init; } = "";
        public int Port { get; init; }
        public string SenderEmail { get; init; } = "";
        public string SenderName { get; init; } = "";
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public bool EnableSsl { get; init; }
    }
}
