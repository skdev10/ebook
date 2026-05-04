using System.Security.Claims;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EBookDashboard.Hubs
{
    [Authorize]
    public sealed class AdminActivityHub : Hub
    {
        public const string AdminGroupName = "AdminUsers";

        private readonly IServiceScopeFactory _scopeFactory;

        public AdminActivityHub(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public override async Task OnConnectedAsync()
        {
            var email = Context.User?.FindFirst(ClaimTypes.Email)?.Value
                ?? Context.User?.Identity?.Name;
            if (string.IsNullOrEmpty(email))
            {
                Context.Abort();
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var user = await db.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserEmail == email);

            var isAdmin = user != null
                && ((user.Role?.RoleName != null
                     && user.Role.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    || user.RoleId == 213);

            if (!isAdmin)
            {
                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, AdminGroupName);
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, AdminGroupName);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
