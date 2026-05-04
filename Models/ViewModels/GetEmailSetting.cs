namespace EBookDashboard.Models.ViewModels
{
    public class GetEmailSetting
    {
        // Email configuration properties according to appsettings.json
        public string? SmtpServer { get; set; } = default;
        public int Port { get; set; } = default;
        public string? Username { get; set; } = default;
        public string? Password { get; set; } = default;
        public string? FromEmail { get; set; } = default;
        public bool EnableSSL { get; set; } = true;

    }
}
