using EBookDashboard.Models;

namespace EBookDashboard.Interfaces
{
    public interface IUserService
    {
        // ----- Users -----
        Task<IEnumerable<Users>> GetAllUsersAsync();
        Task<Users?> GetUserByIdAsync(int userId);
        Task<Users?> GetUserByEmailAsync(string email);
        Task<Users> CreateUserAsync(Users user);
        Task<bool> UpdateUserAsync(Users user);
        Task<bool> DeleteUserAsync(int userId);

        // ----- Roles -----
        Task<IEnumerable<Roles>> GetAllRolesAsync();
        Task<Roles?> GetRoleByIdAsync(int roleId);
        Task<Roles> CreateRoleAsync(Roles role);
        Task<bool> UpdateRoleAsync(Roles role);
        Task<bool> DeleteRoleAsync(int roleId);
    }
}
