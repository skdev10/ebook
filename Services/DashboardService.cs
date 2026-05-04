using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using Microsoft.EntityFrameworkCore;
using System;

namespace EBookDashboard.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly ApplicationDbContext _context;

        public DashboardService(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<bool> ChangePasswordTextAsync(string email, string oldPassword, string newPassword)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.UserEmail == email);
            if (user == null) return false;

            // Verify old password (plain text)
            if (user.Password != oldPassword)
                return false;

            // Update password (plain text — matches Account login comparison)
            user.Password = newPassword;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return true;
        }
        public async Task<bool> ChangePasswordAsync(string email, string oldPassword, string newPassword)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.UserEmail == email);
            if (user == null) return false;

            //if (!BCrypt.Net.BCrypt.Verify(oldPassword, user.Password))
            //    return false;

            user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
            await _context.SaveChangesAsync();

            return true;
        }
    }

}
