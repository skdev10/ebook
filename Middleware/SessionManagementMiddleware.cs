using System.Security.Claims;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Middleware
{
    public class SessionManagementMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SessionManagementMiddleware> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public SessionManagementMiddleware(
            RequestDelegate next,
            ILogger<SessionManagementMiddleware> logger,
            IServiceScopeFactory scopeFactory)
        {
            _next = next;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Sync Session from current request's identity (Admin vs User cookie) so each tab shows the correct user
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var nameClaim = context.User.FindFirst(ClaimTypes.Name)?.Value;
                if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var userId))
                    context.Session.SetInt32("UserId", userId);
                if (nameClaim != null)
                    context.Session.SetString("FullName", nameClaim);
            }

            await TryPersistBookWorkResumeUrlAsync(context);

            // Track user session activity
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var lastActivity = context.Session.GetString("LastActivity");
                var currentTime = DateTime.UtcNow;

                if (lastActivity != null)
                {
                    var lastActivityTime = DateTime.Parse(lastActivity);
                    var inactivityPeriod = currentTime - lastActivityTime;

                    // Auto-logout after 30 minutes of inactivity
                    if (inactivityPeriod.TotalMinutes > 30)
                    {
                        _logger.LogInformation("User session expired due to inactivity. User: {User}", 
                            context.User.Identity.Name);
                        
                        await context.SignOutAsync("AdminCookie");
                        await context.SignOutAsync("UserCookie");
                        context.Session.Clear();
                        
                        if (IsAjaxRequest(context))
                        {
                            context.Response.StatusCode = 401;
                            await context.Response.WriteAsync("Session expired");
                            return;
                        }
                        
                        context.Response.Redirect("/Account/UserLogin?sessionExpired=true");
                        return;
                    }
                }

                // Update last activity
                context.Session.SetString("LastActivity", currentTime.ToString());
                
                // Track page views for analytics
                var currentPage = context.Request.Path.Value;
                if (!string.IsNullOrEmpty(currentPage) && !currentPage.Contains("/api/"))
                {
                    IncrementPageView(context, currentPage);
                }
            }

            await _next(context);
        }

        /// <summary>
        /// Persists the last book-design URL per user (Settings) so login can return them to the same screen.
        /// </summary>
        private async Task TryPersistBookWorkResumeUrlAsync(HttpContext context)
        {
            if (context.User?.Identity?.IsAuthenticated != true || !HttpMethods.IsGet(context.Request.Method))
                return;
            if (IsAjaxRequest(context))
                return;
            var path = context.Request.Path.Value ?? "";
            if (!ShouldTrackBookWorkPath(path))
                return;
            var uid = context.Session.GetInt32("UserId");
            if (!uid.HasValue || uid.Value <= 0)
                return;
            var qs = context.Request.QueryString.HasValue ? context.Request.QueryString.Value ?? "" : "";
            var full = Models.Settings.ClampValueLength(path + qs, Models.Settings.DbCompatMaxValueLength) ?? "";
            if (!IsSafeResumePath(full))
                return;

            // Avoid 3–5 DB round-trips on every GET when the user is already on the same URL/book.
            var sessionUrlKey = $"ResumeUrl:{uid.Value}";
            var sessionBookKey = $"ResumeBookId:{uid.Value}";
            var urlUnchanged = string.Equals(context.Session.GetString(sessionUrlKey), full, StringComparison.Ordinal);
            TryExtractBookIdFromPathAndQuery(path, qs, out var bookId);
            var bookUnchanged = bookId <= 0
                || context.Session.GetInt32(sessionBookKey) == bookId;
            if (urlUnchanged && bookUnchanged)
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var key = $"user:{uid.Value}:lastBookWorkUrl";
                var row = await db.Settings.AsNoTracking()
                    .Where(s => s.Key == key)
                    .Select(s => new { s.SettingId, s.Value })
                    .FirstOrDefaultAsync(context.RequestAborted);

                if (row != null && string.Equals(row.Value, full, StringComparison.Ordinal) && bookUnchanged)
                {
                    context.Session.SetString(sessionUrlKey, full);
                    if (bookId > 0) context.Session.SetInt32(sessionBookKey, bookId);
                    return;
                }

                var tracked = row == null
                    ? null
                    : await db.Settings.FirstOrDefaultAsync(s => s.SettingId == row.SettingId, context.RequestAborted);

                if (tracked == null)
                {
                    var nextId = await db.NextSettingIdAsync(context.RequestAborted);
                    db.Settings.Add(new Models.Settings
                    {
                        SettingId = nextId,
                        Key = key,
                        Value = full,
                        Category = "Resume",
                        Description = "Last book formatter / writer URL",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    tracked.Value = full;
                    tracked.UpdatedAt = DateTime.UtcNow;
                }

                if (bookId > 0)
                    await UpsertPerBookResumeUrlAsync(db, bookId, full, context.RequestAborted);

                await db.SaveChangesAsync(context.RequestAborted);
                context.Session.SetString(sessionUrlKey, full);

                if (bookId > 0)
                {
                    await TryPersistLastWorkedBookAsync(db, uid.Value, bookId, context.RequestAborted);
                    context.Session.SetInt32(sessionBookKey, bookId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist resume URL for user {UserId}", uid.Value);
            }
        }

        private static async Task UpsertPerBookResumeUrlAsync(
            ApplicationDbContext db,
            int bookId,
            string fullUrl,
            CancellationToken cancellationToken)
        {
            var perBookKey = BookResumeUrlHelper.PerBookSettingsKey(bookId);
            var perBookRow = await db.Settings.FirstOrDefaultAsync(s => s.Key == perBookKey, cancellationToken);
            if (perBookRow == null)
            {
                var nextId = await db.NextSettingIdAsync(cancellationToken);
                db.Settings.Add(new Settings
                {
                    SettingId = nextId,
                    Key = perBookKey,
                    Value = fullUrl,
                    Category = "Resume",
                    Description = "Last workflow URL for this book",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else if (!string.Equals(perBookRow.Value, fullUrl, StringComparison.Ordinal))
            {
                perBookRow.Value = fullUrl;
                perBookRow.UpdatedAt = DateTime.UtcNow;
            }
        }

        private static bool TryExtractBookIdFromPathAndQuery(string path, string queryString, out int bookId)
        {
            bookId = 0;
            if (!string.IsNullOrWhiteSpace(queryString))
            {
                var q = queryString.StartsWith('?') ? queryString[1..] : queryString;
                foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && kv[0].Equals("bookId", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(Uri.UnescapeDataString(kv[1]), out bookId) && bookId > 0)
                        return true;
                }
            }
            return false;
        }

        private async Task TryPersistLastWorkedBookAsync(ApplicationDbContext db, int userId, int bookId, CancellationToken cancellationToken)
        {
            var owns = await db.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
            if (!owns) return;

            var key = $"user:{userId}:lastBookId";
            var existingId = await db.Settings.AsNoTracking()
                .Where(s => s.Key == key)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
            var idText = bookId.ToString();
            if (string.Equals(existingId, idText, StringComparison.Ordinal))
            {
                var alreadyActive = await db.Books.AsNoTracking()
                    .AnyAsync(b => b.UserId == userId && b.BookId == bookId && b.isActive == 1, cancellationToken);
                if (alreadyActive) return;
            }

            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (row == null)
            {
                var nextId = await db.NextSettingIdAsync(cancellationToken);
                db.Settings.Add(new Settings
                {
                    SettingId = nextId,
                    Key = key,
                    Value = idText,
                    Category = "Resume",
                    Description = "Last book the author worked on",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                row.Value = idText;
                row.UpdatedAt = DateTime.UtcNow;
            }

            await db.Books
                .Where(b => b.UserId == userId && b.isActive == 1 && b.BookId != bookId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.isActive, 0), cancellationToken);
            await db.Books
                .Where(b => b.UserId == userId && b.BookId == bookId && b.isActive != 1)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.isActive, 1), cancellationToken);
            await db.Books
                .Where(b => b.UserId == userId && b.BookId == bookId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.UpdatedAt, DateTime.UtcNow), cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        private static bool ShouldTrackBookWorkPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var p = path.TrimEnd('/');
            return
                p.StartsWith("/Books/Writer", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("/Books/Formatting", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("/Books/Cover", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("/Books/AIGenerateBook", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("/BookDesign/CoverDesignCalculatorFixing", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("/Dashboard/CoverDesign", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p, "/Dashboard/MyBooks", StringComparison.OrdinalIgnoreCase);
            // Do not track /Dashboard/Publish — Edit / Continue Editing must return to
            // Writer, Formatting, or Cover, not overwrite last work with the Publish screen.
        }

        private static bool IsSafeResumePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            path = path.Trim();
            if (path.Length > 600) return false;
            if (!path.StartsWith('/')) return false;
            if (path.StartsWith("//", StringComparison.Ordinal)) return false;
            if (path.Contains("://", StringComparison.Ordinal) || path.Contains('\\')) return false;
            return true;
        }

        private static bool IsAjaxRequest(HttpContext context)
        {
            if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return true;

            var contentType = context.Request.Headers.ContentType.ToString();
            if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;
            if (contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
                return true;

            var path = context.Request.Path.Value ?? "";
            if (path.Equals("/Books/ImportChapterFile", StringComparison.OrdinalIgnoreCase))
                return true;

            var accept = context.Request.Headers.Accept.ToString();
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;

            // fetch() GET calls to JSON endpoints (no Content-Type) — avoid HTML login page on session expiry
            if (context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                if (path.StartsWith("/Books/", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals("/Books/AIGenerateBook", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals("/Books/AIGenerateBookFormat", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals("/Books/EBookHub", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals("/Books/CreateBook", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains("/Books/Create", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (path.StartsWith("/BookDesign/", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals("/BookDesign/CoverDesignCalculatorFixing", StringComparison.OrdinalIgnoreCase)
                    && !path.Equals("/BookDesign/Index", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void IncrementPageView(HttpContext context, string page)
        {
            var pageViewsKey = $"PageViews_{page}";
            var currentViews = context.Session.GetInt32(pageViewsKey) ?? 0;
            context.Session.SetInt32(pageViewsKey, currentViews + 1);
        }
    }

    // Extension method for easy registration
    public static class SessionManagementMiddlewareExtensions
    {
        public static IApplicationBuilder UseSessionManagement(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<SessionManagementMiddleware>();
        }
    }
}