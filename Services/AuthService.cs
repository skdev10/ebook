using EBookDashboard.Interfaces;
using EBookDashboard.Models;

namespace EBookDashboard.Services
{
    public class AuthService : IAuthService
    {
        // inject any dependencies you need, e.g. DbContext, UserManager, etc.
        public AuthService(/* AppDbContext context, UserManager<AppUser> userManager */)
        {
            // assign dependencies here
        }

        public async Task<bool> RegisterAsync(UserRegistrationModel model)
        {
            // TODO: add your registration logic
            return await Task.FromResult(true);
        }

        public async Task<bool> LoginAsync(LoginViewModel model)
        {
            // TODO: add your login logic
            return await Task.FromResult(true);
        }

        public async Task LogoutAsync()
        {
            // TODO: add your logout logic
            await Task.CompletedTask;
        }
    }
}
