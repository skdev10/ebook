using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;

namespace EBookDashboard.Services;

/// <summary>Sends transactional email using SMTP settings from configuration.</summary>
public class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, IServiceScopeFactory scopeFactory)
    {
        _config = config;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<bool> SendEmailAsync(string toEmail, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return false;

        if (!TryGetSettings(out var settings))
        {
            _logger.LogWarning("Email not sent to {Email}: SMTP is not configured.", toEmail);
            return false;
        }

        try
        {
            using var client = new SmtpClient(settings.SmtpServer, settings.Port)
            {
                Credentials = new NetworkCredential(settings.Username, settings.Password),
                EnableSsl = settings.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30_000
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
            _logger.LogInformation("Email sent to {Email} via {Host}:{Port}", toEmail, settings.SmtpServer, settings.Port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {Email} via {Host}:{Port}", toEmail, settings.SmtpServer, settings.Port);
            return false;
        }
    }

    public bool IsConfigured => TryGetSettings(out _);

    public EmailServiceStatus GetStatus()
    {
        if (!TryGetSettings(out var settings))
        {
            return new EmailServiceStatus
            {
                Configured = false,
                Source = "none"
            };
        }

        return new EmailServiceStatus
        {
            Configured = true,
            Source = settings.Source,
            SmtpServer = settings.SmtpServer,
            Port = settings.Port,
            FromEmail = MaskEmail(settings.SenderEmail),
            HasPassword = !string.IsNullOrEmpty(settings.Password)
        };
    }

    private bool TryGetSettings(out ResolvedEmailSettings settings)
    {
        settings = new ResolvedEmailSettings();
        if (TryGetFromConfiguration(out settings))
            return true;

        if (TryGetFromDatabase(out settings))
            return true;

        return false;
    }

    private bool TryGetFromConfiguration(out ResolvedEmailSettings settings)
    {
        settings = new ResolvedEmailSettings();
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

        return TryBuildSettings(
            smtpServer,
            portRaw,
            senderEmail,
            senderName,
            username,
            password,
            sslRaw,
            "configuration",
            out settings);
    }

    private bool TryGetFromDatabase(out ResolvedEmailSettings settings)
    {
        settings = new ResolvedEmailSettings();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var keys = new[]
            {
                "smtp_host", "smtp_port", "smtp_username", "smtp_password",
                "smtp_from_email", "smtp_sender_name", "smtp_enable_ssl", "site_email"
            };
            var rows = db.Settings.AsNoTracking()
                .Where(s => keys.Contains(s.Key))
                .ToDictionary(s => s.Key, s => s.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            var smtpServer = Row(rows, "smtp_host");
            var portRaw = Row(rows, "smtp_port");
            var senderEmail = FirstNonEmpty(Row(rows, "smtp_from_email"), Row(rows, "site_email"));
            var username = FirstNonEmpty(Row(rows, "smtp_username"), senderEmail);
            var password = Row(rows, "smtp_password");
            var senderName = FirstNonEmpty(Row(rows, "smtp_sender_name")) ?? "eBook Publisher";
            var sslRaw = FirstNonEmpty(Row(rows, "smtp_enable_ssl")) ?? "true";

            return TryBuildSettings(
                smtpServer,
                portRaw,
                senderEmail,
                senderName,
                username,
                password,
                sslRaw,
                "database",
                out settings);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load SMTP settings from database.");
            return false;
        }
    }

    private static bool TryBuildSettings(
        string? smtpServer,
        string? portRaw,
        string? senderEmail,
        string senderName,
        string? username,
        string? password,
        string? sslRaw,
        string source,
        out ResolvedEmailSettings settings)
    {
        settings = new ResolvedEmailSettings();
        if (string.IsNullOrWhiteSpace(smtpServer) || string.IsNullOrWhiteSpace(senderEmail))
            return false;

        if (!int.TryParse((portRaw ?? "").Trim(), out var port))
            port = 587;

        settings = new ResolvedEmailSettings
        {
            Source = source,
            SmtpServer = smtpServer.Trim(),
            Port = port,
            SenderEmail = senderEmail.Trim(),
            SenderName = senderName.Trim(),
            Username = (username ?? senderEmail).Trim(),
            Password = password ?? string.Empty,
            EnableSsl = !string.Equals(sslRaw, "false", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sslRaw, "0", StringComparison.OrdinalIgnoreCase)
        };
        return true;
    }

    private static string Row(IReadOnlyDictionary<string, string> rows, string key)
        => rows.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }
        return null;
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return email;
        var parts = email.Split('@');
        var local = parts[0];
        var domain = parts[1];
        if (local.Length <= 2)
            return $"**@{domain}";
        return $"{local[0]}***{local[^1]}@{domain}";
    }

    private sealed class ResolvedEmailSettings
    {
        public string Source { get; init; } = "";
        public string SmtpServer { get; init; } = "";
        public int Port { get; init; }
        public string SenderEmail { get; init; } = "";
        public string SenderName { get; init; } = "";
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public bool EnableSsl { get; init; }
    }
}
