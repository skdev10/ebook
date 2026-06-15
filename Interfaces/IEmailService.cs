namespace EBookDashboard.Interfaces;

/// <summary>Sends HTML email via configured SMTP.</summary>
public interface IEmailService
{
    /// <summary>Returns true when the message was sent; false when SMTP is not configured or send failed.</summary>
    Task<bool> SendEmailAsync(string toEmail, string subject, string body);

    /// <summary>True when minimum SMTP settings are present.</summary>
    bool IsConfigured { get; }
}
