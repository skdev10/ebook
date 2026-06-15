namespace EBookDashboard.Interfaces;

/// <summary>Non-secret SMTP diagnostics for deploy/health checks.</summary>
public sealed class EmailServiceStatus
{
    public bool Configured { get; init; }
    public string Source { get; init; } = "";
    public string? SmtpServer { get; init; }
    public int? Port { get; init; }
    public string? FromEmail { get; init; }
    public bool HasPassword { get; init; }
}

/// <summary>Sends HTML email via configured SMTP.</summary>
public interface IEmailService
{
    /// <summary>Returns true when the message was sent; false when SMTP is not configured or send failed.</summary>
    Task<bool> SendEmailAsync(string toEmail, string subject, string body);

    /// <summary>True when minimum SMTP settings are present.</summary>
    bool IsConfigured { get; }

    /// <summary>Non-secret SMTP diagnostics for deploy/health checks.</summary>
    EmailServiceStatus GetStatus();
}
