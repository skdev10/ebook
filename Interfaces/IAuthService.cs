using EBookDashboard.Models;

namespace EBookDashboard.Interfaces
{
    public interface IAuthService
    {
        Task<bool> RegisterAsync(UserRegistrationModel model);
        Task<bool> LoginAsync(LoginViewModel model);
        Task LogoutAsync();
    }
}
