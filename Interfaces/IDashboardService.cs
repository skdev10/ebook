using EBookDashboard.Models.DTO;

namespace EBookDashboard.Interfaces
{
    public interface IDashboardService
    {
        Task<bool> ChangePasswordTextAsync(string email, string oldPassword, string newPassword);
        Task<bool> ChangePasswordAsync(string email, string oldPassword, string newPassword);
      

    }
}
