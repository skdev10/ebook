using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using EBookDashboard.Models;

namespace EBookDashboard.Filters
{
    /// <summary>
    /// Ensures only users with Admin role (or RoleId 213) can access AdminController actions.
    /// Matches AdminController role logic; same database as the user panel.
    /// </summary>
    public sealed class RequireAdminAuthorizationFilter : IAsyncAuthorizationFilter
    {
        private readonly ApplicationDbContext _context;

        public RequireAdminAuthorizationFilter(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            if (context.ActionDescriptor.EndpointMetadata.Any(m => m is AllowAnonymousAttribute))
                return;

            var http = context.HttpContext;
            if (http.User?.Identity?.IsAuthenticated != true)
                return;

            var userEmail = http.User.FindFirst(ClaimTypes.Email)?.Value
                ?? http.User.Identity?.Name;
            if (string.IsNullOrEmpty(userEmail))
            {
                context.Result = CreateFailure(context);
                return;
            }

            var currentUser = await _context.Users
                .AsNoTracking()
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserEmail == userEmail);

            var isAdmin = currentUser != null
                && ((currentUser.Role?.RoleName != null
                     && currentUser.Role.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                    || currentUser.RoleId == 213);

            if (!isAdmin)
                context.Result = CreateFailure(context);
        }

        private static IActionResult CreateFailure(AuthorizationFilterContext context)
        {
            var req = context.HttpContext.Request;
            var wantsJson =
                (req.Headers.TryGetValue("Accept", out var accept)
                 && accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase))
                || string.Equals(req.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
                || (req.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false);

            if (wantsJson)
                return new JsonResult(new { success = false, message = "Unauthorized" })
                    { StatusCode = StatusCodes.Status403Forbidden };

            return new RedirectToActionResult("AccessDenied", "Account", null);
        }
    }
}
