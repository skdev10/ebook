using System.Net;
using System.Net.Mail;
namespace EBookDashboard.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body);
    }

}
