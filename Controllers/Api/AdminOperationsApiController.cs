using EBookDashboard.Filters;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Controllers.Api
{
    /// <summary>
    /// Programmatic admin API (integrations, scripts, internal tools). All calls are audited via existing admin flows where applicable.
    /// </summary>
    [ApiController]
    [Route("api/admin/v1")]
    [Authorize]
    [ServiceFilter(typeof(RequireAdminAuthorizationFilter))]
    public sealed class AdminOperationsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AdminOperationsApiController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet("health")]
        public IActionResult Health() => Ok(new { ok = true, module = "admin-api", at = DateTime.UtcNow });

        [HttpGet("dashboard/snapshot")]
        public async Task<IActionResult> DashboardSnapshot(CancellationToken cancellationToken)
        {
            var totalUsers = await _db.Users.CountAsync(cancellationToken);
            var totalBooks = await _db.Books.CountAsync(cancellationToken);
            var activeUsers = await _db.Users.CountAsync(u => u.Status == "Active", cancellationToken);
            var pendingTasks = await _db.Books.CountAsync(
                b => b.Status == "Draft" || b.Status == "Styled" || b.Status == "Previewed",
                cancellationToken);
            var revenue = await _db.BookPrice.SumAsync(bp => (decimal?)bp.bookPrice, cancellationToken) ?? 0m;
            var booksByStatus = await _db.Books.AsNoTracking()
                .GroupBy(b => b.Status ?? "")
                .Select(g => new { status = g.Key, count = g.Count() })
                .ToListAsync(cancellationToken);
            var booksPerUser = await _db.Books.AsNoTracking()
                .GroupBy(b => b.UserId)
                .Select(g => new { userId = g.Key, books = g.Count() })
                .OrderByDescending(x => x.books)
                .Take(50)
                .ToListAsync(cancellationToken);
            var activeSubscriptions = await _db.AuthorPlans.CountAsync(ap => ap.IsActive == 1, cancellationToken);

            return Ok(new
            {
                totalUsers,
                totalBooks,
                activeUsers,
                pendingTasks,
                totalRevenue = revenue,
                activeSubscriptions,
                booksByStatus = booksByStatus.ToDictionary(x => string.IsNullOrEmpty(x.status) ? "Unknown" : x.status, x => x.count),
                topUsersByBookCount = booksPerUser
            });
        }

        [HttpGet("books/recent")]
        public async Task<IActionResult> RecentBooks(int take = 50, CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 200);
            var rows = await (from b in _db.Books.AsNoTracking()
                              join u in _db.Users.AsNoTracking() on b.UserId equals u.UserId
                              orderby b.CreatedAt descending
                              select new
                              {
                                  b.BookId,
                                  b.Title,
                                  b.Status,
                                  b.UserId,
                                  userEmail = u.UserEmail,
                                  userName = u.FullName,
                                  b.CreatedAt,
                                  b.UpdatedAt
                              }).Take(take).ToListAsync(cancellationToken);
            return Ok(new { items = rows });
        }

        [HttpGet("books/transitions")]
        public async Task<IActionResult> BookTransitions(int take = 200, CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 500);
            var transitions = await _db.BookStateTransitions
                .AsNoTracking()
                .OrderByDescending(t => t.CreatedAt)
                .Take(take)
                .ToListAsync(cancellationToken);

            var userIds = transitions.Select(t => t.UserId).Distinct().ToList();
            var bookIds = transitions.Select(t => t.BookId).Distinct().ToList();
            var userMap = await _db.Users.AsNoTracking()
                .Where(u => userIds.Contains(u.UserId))
                .ToDictionaryAsync(u => u.UserId, u => new { u.UserEmail, u.FullName }, cancellationToken);
            var bookMap = await _db.Books.AsNoTracking()
                .Where(b => bookIds.Contains(b.BookId))
                .ToDictionaryAsync(b => b.BookId, b => b.Title, cancellationToken);

            var items = transitions.Select(t => new
            {
                t.BookStateTransitionId,
                t.BookId,
                bookTitle = bookMap.GetValueOrDefault(t.BookId),
                t.UserId,
                userEmail = userMap.TryGetValue(t.UserId, out var u) ? u.UserEmail : null,
                userName = userMap.TryGetValue(t.UserId, out var u2) ? u2.FullName : null,
                t.FromStatus,
                t.ToStatus,
                t.Kind,
                t.CreatedAt,
                t.MetadataJson
            });

            return Ok(new { items });
        }

        [HttpGet("audit/recent")]
        public async Task<IActionResult> RecentAudit(int take = 100, CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 500);
            var rows = await _db.AuditLogs
                .AsNoTracking()
                .OrderByDescending(a => a.CreatedAt)
                .Take(take)
                .Select(a => new
                {
                    a.AuditLogId,
                    a.UserId,
                    a.Action,
                    a.EntityType,
                    a.EntityId,
                    a.Description,
                    a.CreatedAt
                })
                .ToListAsync(cancellationToken);
            return Ok(new { items = rows });
        }
    }
}
