using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.ViewModels;
using EBookDashboard.Services;
using EBookDashboard.Filters;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Security.Claims;

namespace EBookDashboard.Controllers
{
    [Authorize]
    [ServiceFilter(typeof(RequireAdminAuthorizationFilter))]
    public partial class AdminController : Controller
    {
        private readonly IUserService _userService;
        private readonly IBookService _bookService;
        private readonly ApplicationDbContext _context;
        private readonly IDashboardService _dashboardService;
        private readonly IAdminPanelSyncService _panelSync;

        public AdminController(IUserService userService, IBookService bookService, ApplicationDbContext context, IDashboardService dashboardService, IAdminPanelSyncService panelSync)
        {
            _userService = userService;
            _bookService = bookService;
            _context = context;
            _dashboardService = dashboardService;
            _panelSync = panelSync;
        }

        // Helper method to check if current user is Admin
        private async Task<bool> IsCurrentUserAdminAsync()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return false;

            // Try multiple ways to get email
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                // Try User.Identity.Name (might be email in some cases)
                userEmail = User.Identity?.Name;
            }
            
            if (string.IsNullOrEmpty(userEmail))
                return false;

            var currentUser = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (currentUser == null)
                return false;

            // Check role - allow Admin role or RoleId 213 (Super Admin) - case insensitive
            var isAdmin = (currentUser.Role?.RoleName != null && 
                          currentUser.Role.RoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase)) 
                          || currentUser.RoleId == 213;
            
            return isAdmin;
        }

        public async Task<IActionResult> Dashboard()
        {
            // Check if user is Admin
            if (!await IsCurrentUserAdminAsync())
            {
                return RedirectToAction("AccessDenied", "Account");
            }

            // Get user email for data retrieval
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value 
                ?? User.Identity?.Name ?? "";

            var viewModel = new AdminDashboardViewModel
            {
                TotalUsers = await _context.Users.CountAsync(),
                TotalBooks = await _context.Books.CountAsync(),
                ActiveUsers = await _context.Users.CountAsync(u => u.Status == "Active"),
                RecentUsers = (await _context.Users
                    .Include(u => u.Role)
                    .OrderByDescending(u => u.CreatedAt)
                    .Take(5)
                    .ToListAsync())
                    .Select(u => new UserManagementViewModel
                    {
                        UserId = u.UserId,
                        FullName = u.FullName,
                        UserEmail = u.UserEmail,
                        RoleName = u.Role?.RoleName ?? "Unknown",
                        Status = u.Status,
                        CreatedAt = u.CreatedAt,
                        LastLoginAt = u.LastLoginAt,
                        SignupMethod = string.IsNullOrEmpty(u.Password) ? "OAuth" : "Manual",
                        ProfileCompletionPercentage = CalculateProfileCompletion(u),
                        BooksCreated = _context.Books.Count(b => b.UserId == u.UserId),
                        AICoverGenerationsUsed = u.AICoverGenerationsUsed,
                        AICoverGenerationLimit = u.AICoverGenerationLimit > 0 ? u.AICoverGenerationLimit : 5
                    })
                    .ToList(),
                RecentBooks = await (from b in _context.Books
                                     join a in _context.Authors on b.AuthorId equals a.AuthorId into authorGroup
                                     from author in authorGroup.DefaultIfEmpty()
                                     orderby b.CreatedAt descending
                                     select new BookManagementViewModel
                                     {
                                         BookId = b.BookId,
                                         Title = b.Title,
                                         AuthorName = author != null ? author.FullName : "Unknown",
                                         Status = b.Status,
                                         CreatedAt = b.CreatedAt,
                                         UpdatedAt = b.UpdatedAt,
                                         CoverImagePath = b.CoverImagePath,
                                         WordCount = b.WordCount,
                                         Genre = b.Genre,
                                         UserId = b.UserId,
                                         AuthorId = b.AuthorId
                                     })
                                     .Take(5)
                                     .ToListAsync()
            };

            // Calculate revenue (placeholder - adjust based on your payment structure)
            var revenueSum = await _context.BookPrice
                .SumAsync(bp => (decimal?)bp.bookPrice);
            viewModel.TotalRevenue = revenueSum ?? 0m;

            // Populate analytics
            viewModel.Analytics = await GetAnalyticsDataAsync();

            // Calculate pending tasks (books in draft/styled/previewed status)
            ViewBag.PendingTasks = await _context.Books
                .CountAsync(b => b.Status == "Draft" || b.Status == "Styled" || b.Status == "Previewed");

            return View(viewModel);
        }

        private int CalculateProfileCompletion(Users user)
        {
            int completed = 0;
            int total = 7;

            if (!string.IsNullOrEmpty(user.FullName)) completed++;
            if (!string.IsNullOrEmpty(user.UserEmail)) completed++;
            if (!string.IsNullOrEmpty(user.ProfilePicturePath)) completed++;
            if (!string.IsNullOrEmpty(user.SecretQuestion)) completed++;
            if (!string.IsNullOrEmpty(user.SecretQuestionAnswer)) completed++;
            if (user.CreatedAt != default) completed++;
            if (user.LastLoginAt != default) completed++;

            return (int)((double)completed / total * 100);
        }

        private async Task<AnalyticsViewModel> GetAnalyticsDataAsync()
        {
            var revenueSum = await _context.BookPrice.SumAsync(bp => (decimal?)bp.bookPrice);
            var analytics = new AnalyticsViewModel
            {
                TotalUsers = await _context.Users.CountAsync(),
                TotalBooks = await _context.Books.CountAsync(),
                TotalRevenue = revenueSum ?? 0m,
                TotalActivity = await _context.Books.CountAsync()
            };

            // Get signups over last 30 days
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            var signups = await _context.Users
                .Where(u => u.CreatedAt >= thirtyDaysAgo)
                .GroupBy(u => u.CreatedAt.Date)
                .Select(g => new TimeSeriesData
                {
                    Date = g.Key.ToString("yyyy-MM-dd"),
                    Value = g.Count()
                })
                .ToListAsync();

            analytics.SignupsOverTime = signups;

            // Get book creations over last 30 days
            var bookCreations = await _context.Books
                .Where(b => b.CreatedAt >= thirtyDaysAgo)
                .GroupBy(b => b.CreatedAt.Date)
                .Select(g => new TimeSeriesData
                {
                    Date = g.Key.ToString("yyyy-MM-dd"),
                    Value = g.Count()
                })
                .ToListAsync();

            analytics.BookCreationsOverTime = bookCreations;

            // Conversion funnel
            var totalSignups = await _context.Users.CountAsync();
            var drafts = await _context.Books.CountAsync(b => b.Status == "Draft");
            var styled = await _context.Books.CountAsync(b => b.Status == "Styled");
            var previewed = await _context.Books.CountAsync(b => b.Status == "Previewed");
            var generated = await _context.Books.CountAsync(b => b.Status == "Generated");
            var published = await _context.Books.CountAsync(b => b.Status == "Published");

            analytics.ConversionFunnel = new ConversionFunnelViewModel
            {
                Signups = totalSignups,
                Drafts = drafts,
                Styled = styled,
                Previewed = previewed,
                Generated = generated,
                Published = published,
                SignupToDraftRate = totalSignups > 0 ? (decimal)drafts / totalSignups * 100 : 0,
                DraftToPublishedRate = drafts > 0 ? (decimal)published / drafts * 100 : 0
            };

            return analytics;
        }
        
        public IActionResult DashboardNew()
        {
            return View(); // Views/Admin/DashboardNew.cshtml
        }

        // GET: /Admin/Users
        public async Task<IActionResult> Users(string searchTerm = "", string roleFilter = "", string statusFilter = "")
        {
            var query = _context.Users.Include(u => u.Role).AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(u => u.FullName.Contains(searchTerm) || u.UserEmail.Contains(searchTerm));
            }

            if (!string.IsNullOrEmpty(roleFilter))
            {
                query = query.Where(u => u.Role.RoleName == roleFilter);
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(u => u.Status == statusFilter);
            }

            var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

            var viewModel = users.Select(u => new UserManagementViewModel
            {
                UserId = u.UserId,
                FullName = u.FullName,
                UserEmail = u.UserEmail,
                RoleName = u.Role != null ? u.Role.RoleName : "Unknown",
                Status = u.Status,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                SignupMethod = string.IsNullOrEmpty(u.Password) ? "OAuth" : "Manual",
                ProfileCompletionPercentage = CalculateProfileCompletion(u),
                BooksCreated = _context.Books.Count(b => b.UserId == u.UserId),
                AICoverGenerationsUsed = u.AICoverGenerationsUsed,
                AICoverGenerationLimit = u.AICoverGenerationLimit > 0 ? u.AICoverGenerationLimit : 5
            }).ToList();

            await _panelSync.EnrichUserRowsAsync(viewModel);

            ViewBag.SearchTerm = searchTerm;
            ViewBag.RoleFilter = roleFilter;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.Roles = await _context.Roles.Select(r => r.RoleName).ToListAsync();

            return View(viewModel);
        }

        // GET: /Admin/Roles
        public async Task<IActionResult> Roles()
        {
            var allRoles = await _context.Roles
                .Include(r => r.Users)
                .ToListAsync();
            return View(allRoles);
        }

        // GET: /Admin/GetRoles
        [HttpGet]
        public async Task<IActionResult> GetRoles()
        {
            var roles = await _context.Roles
                .OrderBy(r => r.RoleName)
                .Select(r => new { roleId = r.RoleId, roleName = r.RoleName })
                .ToListAsync();
            return Json(new { success = true, roles });
        }

        // GET: /Admin/Analytics
        public async Task<IActionResult> Analytics()
        {
            var analytics = await GetAnalyticsDataAsync();
            return View(analytics);
        }

        // GET: /Admin/ProgressTracker
        public async Task<IActionResult> ProgressTracker()
        {
            var viewModel = new ProgressTrackerViewModel();
            var users = await _context.Users.Include(u => u.Role).ToListAsync();
            var books = await _context.Books.ToListAsync();
            var bookIds = books.Select(b => b.BookId).ToList();
            var flowKeys = bookIds.Select(id => $"book:{id}:flowStep").ToList();
            var flowRows = flowKeys.Count == 0
                ? new Dictionary<string, string>()
                : await _context.Settings.AsNoTracking()
                    .Where(s => flowKeys.Contains(s.Key))
                    .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");

            string FlowLabelFor(Books b)
            {
                if (BookFlowStateService.IsPublishedStatus(b.Status))
                    return BookFlowStateService.StepToLabel(BookFlowStateService.StepPublish);
                var step = flowRows.GetValueOrDefault($"book:{b.BookId}:flowStep", BookFlowStateService.StepGenerate);
                return BookFlowStateService.StepToLabel(step);
            }

            int FlowPct(Books b)
            {
                if (BookFlowStateService.IsPublishedStatus(b.Status)) return 100;
                var step = flowRows.GetValueOrDefault($"book:{b.BookId}:flowStep", BookFlowStateService.StepGenerate);
                return BookFlowStateService.StepToPercent(step);
            }

            var writer = 0; var format = 0; var cover = 0; var publish = 0;
            foreach (var b in books)
            {
                if (BookFlowStateService.IsPublishedStatus(b.Status)) { publish++; continue; }
                var step = flowRows.GetValueOrDefault($"book:{b.BookId}:flowStep", BookFlowStateService.StepGenerate).Trim().ToLowerInvariant();
                switch (step)
                {
                    case BookFlowStateService.StepFormat: format++; break;
                    case BookFlowStateService.StepCover: cover++; break;
                    case BookFlowStateService.StepPublish: publish++; break;
                    default: writer++; break;
                }
            }

            viewModel.UsersByPhase = new Dictionary<string, int>
            {
                { "Signup", users.Count },
                { "Has books", books.Select(b => b.UserId).Distinct().Count() },
                { "Published", books.Count(b => BookFlowStateService.IsPublishedStatus(b.Status)) }
            };

            viewModel.BooksByPhase = new Dictionary<string, int>
            {
                { "AI Writer", writer },
                { "Formatting", format },
                { "Cover", cover },
                { "Publish", publish }
            };

            viewModel.UserJourneys = users.Select(u =>
            {
                var latest = books.Where(b => b.UserId == u.UserId).OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt).FirstOrDefault();
                return new UserJourneyViewModel
                {
                    UserId = u.UserId,
                    UserName = u.FullName,
                    SignupDate = u.CreatedAt,
                    CurrentPhase = latest == null ? "Signup" : FlowLabelFor(latest),
                    DraftDate = books.Where(b => b.UserId == u.UserId).OrderBy(b => b.CreatedAt).Select(b => (DateTime?)b.CreatedAt).FirstOrDefault(),
                    GeneratedDate = books.Where(b => b.UserId == u.UserId).OrderBy(b => b.CreatedAt).Select(b => (DateTime?)b.CreatedAt).FirstOrDefault(),
                    PublishedDate = books.Where(b => b.UserId == u.UserId && BookFlowStateService.IsPublishedStatus(b.Status)).OrderBy(b => b.CreatedAt).Select(b => (DateTime?)b.CreatedAt).FirstOrDefault(),
                    DaysInCurrentPhase = latest == null ? 0 : (int)(DateTime.UtcNow - (latest.UpdatedAt ?? latest.CreatedAt)).TotalDays
                };
            }).ToList();

            var authors = await _context.Authors.ToListAsync();
            viewModel.BookProgresses = books.Select(b =>
            {
                var author = authors.FirstOrDefault(a => a.AuthorId == b.AuthorId);
                return new BookProgressViewModel
                {
                    BookId = b.BookId,
                    UserId = b.UserId,
                    Title = b.Title,
                    AuthorName = author != null ? author.FullName : "Unknown",
                    CurrentPhase = FlowLabelFor(b),
                    ProgressPercentage = FlowPct(b),
                    CreatedAt = b.CreatedAt,
                    LastUpdated = b.UpdatedAt
                };
            }).ToList();

            return View(viewModel);
        }

        // GET: /Admin/Settings
        public async Task<IActionResult> Settings()
        {
            var id = await GetCurrentAdminUserIdAsync();
            Users? user = null;
            if (id.HasValue)
                user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == id.Value);
            ViewBag.AdminUser = user;
            var prefKeys = await _context.Settings.AsNoTracking()
                .Where(s => s.Key.StartsWith("admin:settings:"))
                .ToListAsync();
            ViewBag.AdminSettings = prefKeys.ToDictionary(s => s.Key, s => s.Value ?? "");
            return View();
        }

        // GET: /Admin/Subscriptions
        public IActionResult Subscriptions()
        {
            return View();
        }

        // GET: /Admin/ContentManagement (Books Management)
        public async Task<IActionResult> ContentManagement(string searchTerm = "", string statusFilter = "")
        {
            var query = _context.Books.AsQueryable();

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(b => b.Title.Contains(searchTerm));
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(b => b.Status == statusFilter);
            }

            var books = await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
            var authors = await _context.Authors.ToListAsync();

            var viewModel = books.Select(b =>
            {
                var author = authors.FirstOrDefault(a => a.AuthorId == b.AuthorId);
                return new BookManagementViewModel
                {
                    BookId = b.BookId,
                    Title = b.Title,
                    AuthorName = author != null ? author.FullName : "Unknown",
                    Status = b.Status,
                    CreatedAt = b.CreatedAt,
                    UpdatedAt = b.UpdatedAt,
                    CoverImagePath = b.CoverImagePath,
                    WordCount = b.WordCount,
                    Genre = b.Genre,
                    UserId = b.UserId,
                    AuthorId = b.AuthorId
                };
            }).ToList();

            await _panelSync.EnrichBookRowsAsync(viewModel);

            ViewBag.SearchTerm = searchTerm;
            ViewBag.StatusFilter = statusFilter;
            ViewBag.Statuses = new[] { "Draft", "Styled", "Previewed", "Generated", "Finalized", "Published" };

            return View(viewModel);
        }

        // GET: /Admin/AuthorManagement
        public async Task<IActionResult> AuthorManagement()
        {
            var authors = await _context.Authors
                .Include(a => a.Books)
                .ToListAsync();
            return View(authors);
        }

        // GET: /Admin/UserPlanInfo
        public async Task<IActionResult> UserPlanInfo()
        {
            var users = await _context.Users.AsNoTracking().ToListAsync();
            var plans = await _context.AuthorPlans.AsNoTracking()
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
            var latestByUser = new Dictionary<int, AuthorPlans>();
            foreach (var p in plans)
            {
                if (!latestByUser.ContainsKey(p.UserId))
                    latestByUser[p.UserId] = p;
            }

            var now = DateTime.UtcNow;
            var rows = users.Select(u =>
            {
                latestByUser.TryGetValue(u.UserId, out var plan);
                var active = plan != null && plan.IsActive == 1 && (plan.EndDate.Year < 1981 || plan.EndDate >= now);
                return new AdminUserPlanRow
                {
                    UserId = u.UserId,
                    FullName = u.FullName ?? "",
                    UserEmail = u.UserEmail ?? "",
                    UserStatus = u.Status ?? "",
                    PlanName = plan?.PlanName ?? "None",
                    PlanStatus = plan == null ? "None" : active ? "Active" : plan.IsActive == 0 ? "Cancelled" : "Expired",
                    StartDate = plan?.StartDate,
                    EndDate = plan != null && plan.EndDate.Year > 1981 ? plan.EndDate : null,
                    PlanRate = plan?.PlanRate ?? 0
                };
            }).OrderByDescending(r => r.PlanStatus == "Active").ThenBy(r => r.FullName).ToList();

            return View(rows);
        }

        // GET: /Admin/BookOwnership
        public async Task<IActionResult> BookOwnership()
        {
            var books = await _context.Books
                .Include(b => b.Chapters)
                .ToListAsync();
            
            var authors = await _context.Authors.ToListAsync();
            ViewBag.Authors = authors;
            
            return View(books);
        }

        // POST: /Admin/CreateUser
        [HttpPost]
        public async Task<IActionResult> CreateUser(string fullName, string userEmail, string password, int roleId, string status = "Active")
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                // Validate inputs
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    return Json(new { success = false, message = "Full name is required" });
                }
                
                if (string.IsNullOrWhiteSpace(userEmail))
                {
                    return Json(new { success = false, message = "Email is required" });
                }
                
                // Check if email already exists
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
                if (existingUser != null)
                {
                    return Json(new { success = false, message = "Email already exists" });
                }
                
                // Validate role exists
                var roleExists = await _context.Roles.AnyAsync(r => r.RoleId == roleId);
                if (!roleExists)
                {
                    return Json(new { success = false, message = "Invalid role selected" });
                }
                
                // Create user with all required fields
                var user = new Users
                {
                    FullName = fullName.Trim(),
                    UserEmail = userEmail.Trim().ToLower(),
                    Password = password ?? string.Empty, // In production, hash this password
                    ConfirmPassword = password ?? string.Empty, // Required field
                    RoleId = roleId,
                    Status = string.IsNullOrWhiteSpace(status) ? "Active" : status,
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.MinValue, // Required field - set to default, will update on first login
                    SecretQuestion = string.Empty, // Required field
                    SecretQuestionAnswer = string.Empty // Required field
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Create", "User", user.UserId, $"Created user: {user.FullName}");

                return Json(new { success = true, message = "User created successfully", userId = user.UserId });
            }
            catch (DbUpdateException dbEx)
            {
                // Get inner exception for more details
                var innerException = dbEx.InnerException?.Message ?? dbEx.Message;
                return Json(new { success = false, message = $"Database error: {innerException}" });
            }
            catch (Exception ex)
            {
                // Log the full exception for debugging
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $" | Inner: {ex.InnerException.Message}";
                }
                return Json(new { success = false, message = $"Error: {errorMessage}" });
            }
        }

        // POST: /Admin/UpdateUser
        [HttpPost]
        public async Task<IActionResult> UpdateUser([FromForm] UpdateUserRequest model)
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            if (model == null || model.UserId <= 0)
            {
                return Json(new { success = false, message = "Invalid user or missing user id" });
            }

            try
            {
                var user = await _context.Users.FindAsync(model.UserId);
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                // Validate email if changed
                var userEmail = model.UserEmail?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(userEmail))
                {
                    var normalizedEmail = userEmail.ToLower();
                    if (!string.Equals(user.UserEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
                    {
                        var emailInUse = await _context.Users.AnyAsync(u => u.UserEmail == normalizedEmail && u.UserId != model.UserId);
                        if (emailInUse)
                        {
                            return Json(new { success = false, message = "Email is already in use by another user" });
                        }
                        user.UserEmail = normalizedEmail;
                    }
                }

                var fullName = model.FullName?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(fullName))
                    user.FullName = fullName;
                if (model.RoleId.HasValue && model.RoleId.Value > 0)
                {
                    var roleExists = await _context.Roles.AnyAsync(r => r.RoleId == model.RoleId.Value);
                    if (!roleExists)
                    {
                        return Json(new { success = false, message = "Selected role does not exist. Please choose a valid role." });
                    }
                    user.RoleId = model.RoleId.Value;
                }
                var status = model.Status?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(status))
                    user.Status = status;

                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Update", "User", model.UserId, $"Updated user: {user.FullName}");

                return Json(new { success = true, message = "User updated successfully" });
            }
            catch (DbUpdateException dbEx)
            {
                var innerMsg = dbEx.InnerException?.Message ?? dbEx.Message;
                return Json(new { success = false, message = "Database error: " + innerMsg });
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (ex.InnerException != null)
                    msg += " | " + ex.InnerException.Message;
                return Json(new { success = false, message = msg });
            }
        }

        // POST: /Admin/DeleteUser
        [HttpPost]
        public async Task<IActionResult> DeleteUser(int userId)
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                user.Status = "Deleted";
                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Delete", "User", userId, $"Deleted user: {user.FullName}");

                return Json(new { success = true, message = "User deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Admin/SuspendUser
        [HttpPost]
        public async Task<IActionResult> SuspendUser(int userId)
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                user.Status = "Suspended";
                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Suspend", "User", userId, $"Suspended user: {user.FullName}");

                return Json(new { success = true, message = "User suspended successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Admin/CreateBook
        [HttpPost]
        public async Task<IActionResult> CreateBook(string title, int authorId, int userId, string genre, string status = "Draft")
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                var book = new Books
                {
                    Title = title,
                    AuthorId = authorId,
                    UserId = userId,
                    Genre = genre,
                    Status = status,
                    CreatedAt = DateTime.UtcNow,
                    CategoryId = 1, // Default category
                    LanguageId = 1 // Default language
                };

                _context.Books.Add(book);
                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Create", "Book", book.BookId, $"Created book: {book.Title}");

                return Json(new { success = true, message = "Book created successfully", bookId = book.BookId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Admin/UpdateBook
        [HttpPost]
        public async Task<IActionResult> UpdateBook(int bookId, string title, string genre, string status)
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                var book = await _context.Books.FindAsync(bookId);
                if (book == null)
                {
                    return Json(new { success = false, message = "Book not found" });
                }

                book.Title = title;
                book.Genre = genre;
                book.Status = status;
                book.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Update", "Book", bookId, $"Updated book: {book.Title}");

                return Json(new { success = true, message = "Book updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Admin/DeleteBook
        [HttpPost]
        public async Task<IActionResult> DeleteBook(int bookId)
        {
            if (!await IsCurrentUserAdminAsync())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                var book = await _context.Books.FindAsync(bookId);
                if (book == null)
                {
                    return Json(new { success = false, message = "Book not found" });
                }

                var bookTitle = book.Title;
                _context.Books.Remove(book);
                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Delete", "Book", bookId, $"Deleted book: {bookTitle}");

                return Json(new { success = true, message = "Book deleted successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Admin/ContentEditor
        public IActionResult ContentEditor()
        {
            return View();
        }

        // GET: /Admin/Notifications
        public IActionResult Notifications()
        {
            return View();
        }

        // GET: /Admin/HelpFeedback
        public IActionResult HelpFeedback()
        {
            return View();
        }

        // GET: /Admin/AuditLogs
        public IActionResult AuditLogs()
        {
            return View();
        }

        // GET: /Admin/APIManagement
        public IActionResult APIManagement()
        {
            return View();
        }

        /// <summary>GET /Admin/Logout — ends admin (and user) auth cookies and returns to admin login.</summary>
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            try
            {
                HttpContext.Session.Clear();
            }
            catch
            {
                // Session store may be unavailable; still clear auth cookies
            }

            await HttpContext.SignOutAsync("AdminCookie");
            await HttpContext.SignOutAsync("UserCookie");

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            Response.Headers.Pragma = "no-cache";

            return RedirectToAction("AdminLogin", "Account");
        }

        // GET: /Admin/ProductManagement — catalog + pricing snapshot for ops
        public async Task<IActionResult> ProductManagement()
        {
            if (!await IsCurrentUserAdminAsync())
                return RedirectToAction("AccessDenied", "Account");

            var books = await _context.Books
                .AsNoTracking()
                .Include(b => b.BookPrice)
                .OrderByDescending(b => b.UpdatedAt)
                .Take(400)
                .ToListAsync();

            var list = books.Select(b =>
            {
                var p = b.BookPrice?.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
                return new AdminProductRowViewModel
                {
                    BookId = b.BookId,
                    Title = b.Title ?? "",
                    Genre = b.Genre ?? "",
                    Status = b.Status ?? "",
                    WordCount = b.WordCount,
                    UpdatedAt = b.UpdatedAt,
                    ListPrice = p?.bookPrice,
                    Currency = string.IsNullOrWhiteSpace(p?.Currency) ? "USD" : p!.Currency
                };
            }).ToList();

            return View(list);
        }

        // GET: /Admin/Search
        [HttpGet]
        public async Task<IActionResult> Search(string q)
        {
            if (string.IsNullOrEmpty(q) || q.Length < 2)
            {
                return Json(new { results = new List<object>() });
            }

            var results = new List<object>();

            // Search users
            var users = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.FullName.Contains(q) || u.UserEmail.Contains(q))
                .Take(5)
                .Select(u => new { type = "user", id = u.UserId, title = u.FullName, subtitle = u.UserEmail, url = Url.Action("UserDetail", "Admin", new { id = u.UserId }) })
                .ToListAsync();
            results.AddRange(users);

            // Search books
            var books = await _context.Books
                .Where(b => b.Title.Contains(q))
                .Take(5)
                .Select(b => new { type = "book", id = b.BookId, title = b.Title, subtitle = b.Genre, url = Url.Action("BookDetail", "Admin", new { id = b.BookId }) })
                .ToListAsync();
            results.AddRange(books);

            // Search plans
            var plans = await _context.Plans
                .Where(p => p.PlanName.Contains(q))
                .Take(5)
                .Select(p => new { type = "plan", id = p.PlanId, title = p.PlanName, subtitle = $"${p.PlanRate}", url = Url.Action("Subscriptions", "Admin") })
                .ToListAsync();
            results.AddRange(plans);

            return Json(new { results });
        }
        // GET: /Admin/SearchUsers
        [HttpGet]
        public async Task<IActionResult> SearchUsers(string q)
        {
            if (string.IsNullOrEmpty(q) || q.Length < 2)
            {
                var allUsers = await _context.Users
                    .Include(u => u.Role)
                    .OrderByDescending(u => u.CreatedAt)
                    .Take(20)
                    .Select(u => new UserManagementViewModel
                    {
                        UserId = u.UserId,
                        FullName = u.FullName,
                        UserEmail = u.UserEmail,
                        RoleName = u.Role != null ? u.Role.RoleName : "Unknown", // Fixed: replaced ?. with != null check
                        Status = u.Status,
                        CreatedAt = u.CreatedAt,
                        LastLoginAt = u.LastLoginAt,
                        SignupMethod = string.IsNullOrEmpty(u.Password) ? "OAuth" : "Manual",
                        ProfileCompletionPercentage = CalculateProfileCompletion(u),
                        BooksCreated = _context.Books.Count(b => b.UserId == u.UserId),
                        AICoverGenerationsUsed = u.AICoverGenerationsUsed,
                        AICoverGenerationLimit = u.AICoverGenerationLimit > 0 ? u.AICoverGenerationLimit : 5
                    })
                    .ToListAsync();
                return Json(new { success = true, users = allUsers });
            }

            var users = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.FullName.Contains(q) || u.UserEmail.Contains(q))
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new UserManagementViewModel
                {
                    UserId = u.UserId,
                    FullName = u.FullName,
                    UserEmail = u.UserEmail,
                    RoleName = u.Role != null ? u.Role.RoleName : "Unknown", // Fixed: replaced ?. with != null check
                    Status = u.Status,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt,
                    SignupMethod = string.IsNullOrEmpty(u.Password) ? "OAuth" : "Manual",
                    ProfileCompletionPercentage = CalculateProfileCompletion(u),
                    BooksCreated = _context.Books.Count(b => b.UserId == u.UserId),
                    AICoverGenerationsUsed = u.AICoverGenerationsUsed,
                    AICoverGenerationLimit = u.AICoverGenerationLimit > 0 ? u.AICoverGenerationLimit : 5
                })
                .ToListAsync();

            return Json(new { success = true, users });
        }

        // Helper method to get current admin user ID
        private async Task<int?> GetCurrentAdminUserIdAsync()
        {
            if (!User.Identity?.IsAuthenticated ?? true)
                return null;

            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.Identity?.Name;
            if (string.IsNullOrEmpty(userEmail))
                return null;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            return user?.UserId;
        }

        // Helper method to log audit action
        private async Task LogAuditActionAsync(string action, string entityType, int? entityId = null, string? description = null)
        {
            var adminId = await GetCurrentAdminUserIdAsync();
            var auditLog = new AuditLog
            {
                UserId = adminId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Description = description,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers["User-Agent"].ToString(),
                CreatedAt = DateTime.UtcNow
            };
            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();
        }

        // ========== NOTIFICATION ENDPOINTS ==========
        
        // GET: /Admin/GetNotifications
        [HttpGet]
        public async Task<IActionResult> GetNotifications()
        {
            var adminId = await GetCurrentAdminUserIdAsync();
            var notifications = await _context.Notifications
                .Where(n => n.UserId == null || n.UserId == adminId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(20)
                .Select(n => new
                {
                    id = n.NotificationId,
                    title = n.Title,
                    message = n.Message,
                    type = n.Type,
                    isRead = n.IsRead,
                    link = n.Link,
                    createdAt = n.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync();

            var unreadCount = await _context.Notifications
                .CountAsync(n => (n.UserId == null || n.UserId == adminId) && !n.IsRead);

            return Json(new { notifications, unreadCount });
        }

        // POST: /Admin/MarkNotificationAsRead
        [HttpPost]
        public async Task<IActionResult> MarkNotificationAsRead(int notificationId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification == null)
                return Json(new { success = false, message = "Notification not found" });

            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }

        // ========== USER ENDPOINTS ==========

        // GET: /Admin/GetUserDetails
        [HttpGet]
        public async Task<IActionResult> GetUserDetails(int userId)
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
                return Json(new { success = false, message = "User not found" });

            var books = await _context.Books
                .Where(b => b.UserId == userId)
                .CountAsync();

            var plan = await _context.AuthorPlans.AsNoTracking()
                .Where(p => p.UserId == userId && p.IsActive == 1)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => p.PlanName)
                .FirstOrDefaultAsync();

            var userDetails = new
            {
                userId = user.UserId,
                fullName = user.FullName,
                userEmail = user.UserEmail,
                roleId = user.RoleId,
                roleName = user.Role != null ? user.Role.RoleName : "Unknown",
                status = user.Status,
                createdAt = user.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                lastLoginAt = user.LastLoginAt != default && user.LastLoginAt.Year > 1981 ? user.LastLoginAt.ToString("yyyy-MM-dd HH:mm:ss") : "Never",
                profilePicturePath = user.ProfilePicturePath,
                booksCreated = books,
                profileCompletion = CalculateProfileCompletion(user),
                planName = plan ?? "",
                workspaceUrl = Url.Action("UserDetail", "Admin", new { id = user.UserId }),
                aiCoverGenerationsUsed = user.AICoverGenerationsUsed,
                aiCoverGenerationLimit = user.AICoverGenerationLimit > 0 ? user.AICoverGenerationLimit : 5
            };

            return Json(new { success = true, user = userDetails });
        }

        /// <summary>Sets a user's lifetime AI cover uses back to zero.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ResetAiCoverQuota(int userId)
        {
            if (!await IsCurrentUserAdminAsync())
                return Json(new { success = false, message = "Unauthorized" });
            var quota = HttpContext.RequestServices.GetRequiredService<IAiCoverQuotaService>();
            await quota.ResetAsync(userId);
            var snap = await quota.GetAsync(userId);
            return Json(new { success = true, used = snap.Used, limit = snap.Limit, remaining = snap.Remaining });
        }

        /// <summary>Sets a custom lifetime AI cover generation cap for one user.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SetAiCoverLimit(int userId, int limit)
        {
            if (!await IsCurrentUserAdminAsync())
                return Json(new { success = false, message = "Unauthorized" });
            if (limit < 1 || limit > 500)
                return Json(new { success = false, message = "Limit must be between 1 and 500." });
            var quota = HttpContext.RequestServices.GetRequiredService<IAiCoverQuotaService>();
            await quota.SetLimitAsync(userId, limit);
            var snap = await quota.GetAsync(userId);
            return Json(new { success = true, used = snap.Used, limit = snap.Limit, remaining = snap.Remaining });
        }

        // POST: /Admin/DeactivateUser
        [HttpPost]
        public async Task<IActionResult> DeactivateUser(int userId)
        {
            if (!await IsCurrentUserAdminAsync())
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                    return Json(new { success = false, message = "User not found" });

                user.Status = "Deactivated";
                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Deactivate", "User", userId, $"Deactivated user: {user.FullName}");

                return Json(new { success = true, message = "User deactivated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // GET: /Admin/ExportUsers
        [HttpGet]
        public async Task<IActionResult> ExportUsers(string format = "csv", string roleFilter = "", string statusFilter = "")
        {
            if (!await IsCurrentUserAdminAsync())
                return Unauthorized();

            var query = _context.Users.Include(u => u.Role).AsQueryable();

            if (!string.IsNullOrEmpty(roleFilter))
                query = query.Where(u => u.Role.RoleName == roleFilter);

            if (!string.IsNullOrEmpty(statusFilter))
                query = query.Where(u => u.Status == statusFilter);

            var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

            if (format.ToLower() == "excel" || format.ToLower() == "xlsx")
            {
                // Excel export
                var csv = "Full Name,Email,Role,Status,Created At,Last Login,Books Created\n";
                foreach (var u in users)
                {
                    var booksCount = _context.Books.Count(b => b.UserId == u.UserId);
                    csv += $"\"{u.FullName}\",\"{u.UserEmail}\",\"{(u.Role != null ? u.Role.RoleName : "Unknown")}\",\"{u.Status}\",\"{u.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{(u.LastLoginAt != default ? u.LastLoginAt.ToString("yyyy-MM-dd HH:mm:ss") : "Never")}\",{booksCount}\n";
                }
                var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
                return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"users_export_{DateTime.UtcNow:yyyyMMdd}.csv");
            }
            else
            {
                // CSV export
                var csv = "Full Name,Email,Role,Status,Created At,Last Login,Books Created\n";
                foreach (var u in users)
                {
                    var booksCount = _context.Books.Count(b => b.UserId == u.UserId);
                    csv += $"\"{u.FullName}\",\"{u.UserEmail}\",\"{(u.Role != null ? u.Role.RoleName : "Unknown")}\",\"{u.Status}\",\"{u.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{(u.LastLoginAt != default ? u.LastLoginAt.ToString("yyyy-MM-dd HH:mm:ss") : "Never")}\",{booksCount}\n";
                }
                var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
                return File(bytes, "text/csv", $"users_export_{DateTime.UtcNow:yyyyMMdd}.csv");
            }
        }

        // ========== BOOK ENDPOINTS ==========

        // GET: /Admin/ExportBooks
        [HttpGet]
        public async Task<IActionResult> ExportBooks(string format = "csv", string statusFilter = "", string genreFilter = "")
        {
            if (!await IsCurrentUserAdminAsync())
                return Unauthorized();

            var query = _context.Books.Include(b => b.Chapters).AsQueryable();

            if (!string.IsNullOrEmpty(statusFilter))
                query = query.Where(b => b.Status == statusFilter);

            if (!string.IsNullOrEmpty(genreFilter))
                query = query.Where(b => b.Genre == genreFilter);

            var books = await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
            var authors = await _context.Authors.ToListAsync();

            var csv = "Title,Author,Genre,Status,Word Count,Chapters,Created At,Updated At\n";
            foreach (var b in books)
            {
                var author = authors.FirstOrDefault(a => a.AuthorId == b.AuthorId);
                var chaptersCount = b.Chapters != null ? b.Chapters.Count : 0;
                csv += $"\"{b.Title}\",\"{(author != null ? author.FullName : "Unknown")}\",\"{b.Genre}\",\"{b.Status}\",{b.WordCount},{chaptersCount},\"{b.CreatedAt:yyyy-MM-dd HH:mm:ss}\",\"{(b.UpdatedAt.HasValue ? b.UpdatedAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "N/A")}\"{Environment.NewLine}";
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            var contentType = format.ToLower() == "excel" || format.ToLower() == "xlsx" 
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" 
                : "text/csv";
            var fileName = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? $"books_export_{DateTime.UtcNow:yyyyMMdd}.xlsx"
                : $"books_export_{DateTime.UtcNow:yyyyMMdd}.csv";

            return File(bytes, contentType, fileName);
        }

        // ========== SUBSCRIPTIONS EXPORT ==========

        // GET: /Admin/ExportSubscriptions
        [HttpGet]
        public async Task<IActionResult> ExportSubscriptions(string format = "csv")
        {
            if (!await IsCurrentUserAdminAsync())
                return Unauthorized();

            var query = from ap in _context.AuthorPlans
                        join u in _context.Users on ap.UserId equals u.UserId
                        join p in _context.Plans on ap.PlanId equals p.PlanId
                        orderby ap.CreatedAt descending
                        select new
                        {
                            ap.AuthorPlanId,
                            UserName = u.FullName,
                            u.UserEmail,
                            PlanName = p.PlanName,
                            p.PlanRate,
                            p.Currency,
                            ap.StartDate,
                            ap.EndDate,
                            ap.IsActive,
                            ap.PaymentReference
                        };

            var subs = await query.ToListAsync();

            var csv = "SubscriptionId,User Name,Email,Plan,Price,Currency,Start Date,End Date,Active,Payment Ref\n";
            foreach (var s in subs)
            {
                csv += $"{s.AuthorPlanId},\"{s.UserName}\",\"{s.UserEmail}\",\"{s.PlanName}\",{s.PlanRate},{s.Currency},\"{s.StartDate:yyyy-MM-dd}\",\"{s.EndDate:yyyy-MM-dd}\",{(s.IsActive == 1 ? "Yes" : "No")},\"{s.PaymentReference ?? ""}\"\n";
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            var contentType = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv";
            var fileName = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? $"subscriptions_{DateTime.UtcNow:yyyyMMdd}.xlsx"
                : $"subscriptions_{DateTime.UtcNow:yyyyMMdd}.csv";

            return File(bytes, contentType, fileName);
        }

        // ========== SUBSCRIPTIONS ENDPOINTS ==========

        // GET: /Admin/GetPlans
        [HttpGet]
        public async Task<IActionResult> GetPlans()
        {
            var plans = await _context.Plans
                .OrderBy(p => p.PlanRate)
                .Select(p => new
                {
                    planId = p.PlanId,
                    planName = p.PlanName,
                    planDescription = p.PlanDescription,
                    planRate = p.PlanRate,
                    planDays = p.PlanDays,
                    currency = p.Currency,
                    maxEBooks = p.MaxEBooks,
                    maxPages = p.MaxPages,
                    maxChapters = p.MaxChapters,
                    allowDownloads = p.AllowDownloads,
                    allowFullDashboard = p.AllowFullDashboard,
                    allowAnalytics = p.AllowAnalytics,
                    allowPublishing = p.AllowPublishing,
                    isActive = p.IsActive,
                    createdAt = p.CreateddAt.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync();

            return Json(new { success = true, plans });
        }

        // GET: /Admin/GetPlanDetails
        [HttpGet]
        public async Task<IActionResult> GetPlanDetails(int planId)
        {
            var plan = await _context.Plans.FindAsync(planId);
            if (plan == null)
                return Json(new { success = false, message = "Plan not found" });

            var planDetails = new
            {
                planId = plan.PlanId,
                planName = plan.PlanName,
                planDescription = plan.PlanDescription,
                planRate = plan.PlanRate,
                planDays = plan.PlanDays,
                planHours = plan.PlanHours,
                currency = plan.Currency,
                maxEBooks = plan.MaxEBooks,
                maxPages = plan.MaxPages,
                maxChapters = plan.MaxChapters,
                allowDownloads = plan.AllowDownloads,
                allowFullDashboard = plan.AllowFullDashboard,
                allowAnalytics = plan.AllowAnalytics,
                allowPublishing = plan.AllowPublishing,
                isActive = plan.IsActive,
                createdAt = plan.CreateddAt.ToString("yyyy-MM-dd HH:mm:ss")
            };

            return Json(new { success = true, plan = planDetails });
        }

        // POST: /Admin/CreatePlan
        [HttpPost]
        public async Task<IActionResult> CreatePlan([FromBody] Plans planData)
        {
            if (!await IsCurrentUserAdminAsync())
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var plan = new Plans
                {
                    PlanName = planData.PlanName,
                    PlanDescription = planData.PlanDescription,
                    PlanRate = planData.PlanRate,
                    PlanDays = planData.PlanDays,
                    PlanHours = planData.PlanHours,
                    Currency = planData.Currency ?? "usd",
                    MaxEBooks = planData.MaxEBooks,
                    MaxPages = planData.MaxPages,
                    MaxChapters = planData.MaxChapters,
                    AllowDownloads = planData.AllowDownloads,
                    AllowFullDashboard = planData.AllowFullDashboard,
                    AllowAnalytics = planData.AllowAnalytics,
                    AllowPublishing = planData.AllowPublishing,
                    IsActive = planData.IsActive,
                    CreateddAt = DateTime.UtcNow
                };

                _context.Plans.Add(plan);
                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Create", "Plan", plan.PlanId, $"Created plan: {plan.PlanName}");

                return Json(new { success = true, message = "Plan created successfully", planId = plan.PlanId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // POST: /Admin/UpdatePlan
        [HttpPost]
        public async Task<IActionResult> UpdatePlan(int planId, [FromBody] Plans planData)
        {
            if (!await IsCurrentUserAdminAsync())
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var plan = await _context.Plans.FindAsync(planId);
                if (plan == null)
                    return Json(new { success = false, message = "Plan not found" });

                plan.PlanName = planData.PlanName;
                plan.PlanDescription = planData.PlanDescription;
                plan.PlanRate = planData.PlanRate;
                plan.PlanDays = planData.PlanDays;
                plan.PlanHours = planData.PlanHours;
                plan.Currency = planData.Currency ?? plan.Currency;
                plan.MaxEBooks = planData.MaxEBooks;
                plan.MaxPages = planData.MaxPages;
                plan.MaxChapters = planData.MaxChapters;
                plan.AllowDownloads = planData.AllowDownloads;
                plan.AllowFullDashboard = planData.AllowFullDashboard;
                plan.AllowAnalytics = planData.AllowAnalytics;
                plan.AllowPublishing = planData.AllowPublishing;
                plan.IsActive = planData.IsActive;

                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Update", "Plan", planId, $"Updated plan: {plan.PlanName}");

                return Json(new { success = true, message = "Plan updated successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ========== SETTINGS ENDPOINTS ==========

        // GET: /Admin/GetSettings
        [HttpGet]
        public async Task<IActionResult> GetSettings(string category = "")
        {
            var query = _context.Settings.AsQueryable();
            if (!string.IsNullOrEmpty(category))
                query = query.Where(s => s.Category == category);

            var settings = await query
                .OrderBy(s => s.Category)
                .ThenBy(s => s.Key)
                .Select(s => new
                {
                    settingId = s.SettingId,
                    key = s.Key,
                    value = s.Value,
                    category = s.Category,
                    description = s.Description
                })
                .ToListAsync();

            return Json(new { success = true, settings });
        }

        // POST: /Admin/SaveSettings
        [HttpPost]
        public async Task<IActionResult> SaveSettings([FromBody] List<Settings> settings)
        {
            if (!await IsCurrentUserAdminAsync())
                return Json(new { success = false, message = "Unauthorized" });

            try
            {
                var batchNextSettingId = 0;
                foreach (var setting in settings)
                {
                    var existing = await _context.Settings.FirstOrDefaultAsync(s => s.Key == setting.Key);
                    if (existing != null)
                    {
                        existing.Value = setting.Value;
                        existing.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        if (batchNextSettingId == 0)
                            batchNextSettingId = await _context.NextSettingIdAsync(HttpContext.RequestAborted);
                        else
                            batchNextSettingId++;
                        _context.Settings.Add(new Settings
                        {
                            SettingId = batchNextSettingId,
                            Key = setting.Key,
                            Value = setting.Value,
                            Category = setting.Category,
                            Description = setting.Description,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                }

                await _context.SaveChangesAsync();
                await LogAuditActionAsync("Update", "Settings", null, "Updated settings");

                return Json(new { success = true, message = "Settings saved successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ========== AUDIT LOGS ENDPOINTS ==========

        // GET: /Admin/GetAuditLogs
        [HttpGet]
        public async Task<IActionResult> GetAuditLogs(string actionFilter = "", string entityTypeFilter = "", DateTime? startDate = null, DateTime? endDate = null, int? userId = null)
        {
            if (!await IsCurrentUserAdminAsync())
                return Json(new { success = false, message = "Unauthorized" });

            var query = _context.AuditLogs.Include(al => al.User).AsQueryable();

            if (!string.IsNullOrEmpty(actionFilter))
                query = query.Where(al => al.Action == actionFilter);

            if (!string.IsNullOrEmpty(entityTypeFilter))
                query = query.Where(al => al.EntityType == entityTypeFilter);

            if (userId.HasValue)
                query = query.Where(al => al.UserId == userId);

            if (startDate.HasValue)
                query = query.Where(al => al.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(al => al.CreatedAt <= endDate.Value);

            var logs = await query
                .OrderByDescending(al => al.CreatedAt)
                .Take(1000)
                .Select(al => new
                {
                    auditLogId = al.AuditLogId,
                    userId = al.UserId,
                    userName = al.User != null ? al.User.FullName : "System",
                    action = al.Action,
                    entityType = al.EntityType,
                    entityId = al.EntityId,
                    description = al.Description,
                    ipAddress = al.IpAddress,
                    createdAt = al.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync();

            return Json(new { success = true, logs });
        }

        // GET: /Admin/ExportAuditLogs
        [HttpGet]
        public async Task<IActionResult> ExportAuditLogs(string format = "csv", string actionFilter = "", string entityTypeFilter = "")
        {
            if (!await IsCurrentUserAdminAsync())
                return Unauthorized();

            var query = _context.AuditLogs.Include(al => al.User).AsQueryable();

            if (!string.IsNullOrEmpty(actionFilter))
                query = query.Where(al => al.Action == actionFilter);

            if (!string.IsNullOrEmpty(entityTypeFilter))
                query = query.Where(al => al.EntityType == entityTypeFilter);

            var logs = await query.OrderByDescending(al => al.CreatedAt).Take(10000).ToListAsync();

            var csv = "User,Action,Entity Type,Entity ID,Description,IP Address,Created At\n";
            foreach (var log in logs)
            {
                csv += $"\"{log.User?.FullName ?? "System"}\",\"{log.Action}\",\"{log.EntityType}\",{log.EntityId?.ToString() ?? "N/A"},\"{log.Description ?? ""}\",\"{log.IpAddress ?? ""}\",\"{log.CreatedAt:yyyy-MM-dd HH:mm:ss}\"\n";
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            var contentType = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv";
            var fileName = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? $"audit_logs_{DateTime.UtcNow:yyyyMMdd}.xlsx"
                : $"audit_logs_{DateTime.UtcNow:yyyyMMdd}.csv";

            return File(bytes, contentType, fileName);
        }

        // ========== PROGRESS TRACKER EXPORT ==========

        // GET: /Admin/ExportProgressTracker
        [HttpGet]
        public async Task<IActionResult> ExportProgressTracker(string format = "csv", string dateFrom = "", string dateTo = "", string genreFilter = "")
        {
            if (!await IsCurrentUserAdminAsync())
                return Unauthorized();

            var query = _context.Books.Include(b => b.Chapters).AsQueryable();

            if (!string.IsNullOrEmpty(genreFilter))
                query = query.Where(b => b.Genre == genreFilter);

            if (!string.IsNullOrEmpty(dateFrom) && DateTime.TryParse(dateFrom, out var fromDate))
                query = query.Where(b => b.CreatedAt >= fromDate);

            if (!string.IsNullOrEmpty(dateTo) && DateTime.TryParse(dateTo, out var toDate))
                query = query.Where(b => b.CreatedAt <= toDate);

            var books = await query.OrderByDescending(b => b.CreatedAt).ToListAsync();
            var authors = await _context.Authors.ToListAsync();

            var csv = "Title,Author,Genre,Status,Progress %,Word Count,Chapters,Created At\n";
            foreach (var b in books)
            {
                var author = authors.FirstOrDefault(a => a.AuthorId == b.AuthorId);
                var progress = b.Status switch
                {
                    "Draft" => 20,
                    "Styled" => 40,
                    "Previewed" => 60,
                    "Generated" => 80,
                    "Published" => 100,
                    _ => 0
                };
                csv += $"\"{b.Title}\",\"{author?.FullName ?? "Unknown"}\",\"{b.Genre}\",\"{b.Status}\",{progress},{b.WordCount},{b.Chapters?.Count ?? 0},\"{b.CreatedAt:yyyy-MM-dd HH:mm:ss}\"\n";
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            var contentType = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv";
            var fileName = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? $"progress_tracker_{DateTime.UtcNow:yyyyMMdd}.xlsx"
                : $"progress_tracker_{DateTime.UtcNow:yyyyMMdd}.csv";

            return File(bytes, contentType, fileName);
        }

        // ========== ANALYTICS EXPORT ==========

        // GET: /Admin/ExportAnalytics
        [HttpGet]
        public async Task<IActionResult> ExportAnalytics(string format = "csv")
        {
            if (!await IsCurrentUserAdminAsync())
                return Unauthorized();

            var analytics = await GetAnalyticsDataAsync();

            var csv = "Metric,Value\n";
            csv += $"Total Users,{analytics.TotalUsers}\n";
            csv += $"Total Books,{analytics.TotalBooks}\n";
            csv += $"Total Revenue,${analytics.TotalRevenue:F2}\n";
            csv += $"Total Activity,{analytics.TotalActivity}\n";
            csv += $"Conversion Funnel - Signups,{analytics.ConversionFunnel.Signups}\n";
            csv += $"Conversion Funnel - Drafts,{analytics.ConversionFunnel.Drafts}\n";
            csv += $"Conversion Funnel - Styled,{analytics.ConversionFunnel.Styled}\n";
            csv += $"Conversion Funnel - Previewed,{analytics.ConversionFunnel.Previewed}\n";
            csv += $"Conversion Funnel - Generated,{analytics.ConversionFunnel.Generated}\n";
            csv += $"Conversion Funnel - Published,{analytics.ConversionFunnel.Published}\n";
            csv += $"Signup to Draft Rate,{analytics.ConversionFunnel.SignupToDraftRate:F2}%\n";
            csv += $"Draft to Published Rate,{analytics.ConversionFunnel.DraftToPublishedRate:F2}%\n";

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            var contentType = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv";
            var fileName = format.ToLower() == "excel" || format.ToLower() == "xlsx"
                ? $"analytics_{DateTime.UtcNow:yyyyMMdd}.xlsx"
                : $"analytics_{DateTime.UtcNow:yyyyMMdd}.csv";

            return File(bytes, contentType, fileName);
        }

        /// <summary>JSON snapshot for admin dashboard live refresh (SignalR + polling).</summary>
        [HttpGet]
        public async Task<IActionResult> LiveDashboardStats()
        {
            var totalUsers = await _context.Users.CountAsync();
            var totalBooks = await _context.Books.CountAsync();
            var activeUsers = await _context.Users.CountAsync(u => u.Status == "Active");
            var pendingTasks = await _context.Books
                .CountAsync(b => b.Status == "Draft" || b.Status == "Styled" || b.Status == "Previewed");
            var revenueSum = await _context.BookPrice.SumAsync(bp => (decimal?)bp.bookPrice);

            var booksByStatus = await _context.Books
                .AsNoTracking()
                .GroupBy(b => b.Status ?? "")
                .Select(g => new { status = g.Key, count = g.Count() })
                .ToListAsync();

            var activeSubscriptions = await _context.AuthorPlans.CountAsync(ap => ap.IsActive == 1);
            var usersWithBooks = await _context.Books.AsNoTracking().Select(b => b.UserId).Distinct().CountAsync();

            return Json(new
            {
                totalUsers,
                totalBooks,
                activeUsers,
                pendingTasks,
                totalRevenue = revenueSum ?? 0m,
                activeSubscriptions,
                usersWithBooks,
                booksByStatus = booksByStatus.ToDictionary(x => string.IsNullOrEmpty(x.status) ? "Unknown" : x.status, x => x.count)
            });
        }

        /// <summary>Granular book lifecycle rows for Progress Tracker / audit drill-down.</summary>
        [HttpGet]
        public async Task<IActionResult> RecentBookTransitions(int take = 150)
        {
            try
            {
                var transitions = await _context.BookStateTransitions
                    .AsNoTracking()
                    .OrderByDescending(t => t.CreatedAt)
                    .Take(Math.Clamp(take, 1, 500))
                    .ToListAsync();

                var userIds = transitions.Select(t => t.UserId).Distinct().ToList();
                var bookIds = transitions.Select(t => t.BookId).Distinct().ToList();
                var userMap = await _context.Users.AsNoTracking()
                    .Where(u => userIds.Contains(u.UserId))
                    .ToDictionaryAsync(u => u.UserId, u => new { u.UserEmail, u.FullName });
                var bookMap = await _context.Books.AsNoTracking()
                    .Where(b => bookIds.Contains(b.BookId))
                    .ToDictionaryAsync(b => b.BookId, b => b.Title);

                var list = transitions.Select(t => new
                {
                    t.BookStateTransitionId,
                    t.BookId,
                    bookTitle = bookMap.TryGetValue(t.BookId, out var title) ? title : "",
                    t.UserId,
                    userEmail = userMap.TryGetValue(t.UserId, out var u) ? u.UserEmail : "",
                    userName = userMap.TryGetValue(t.UserId, out var u2) ? u2.FullName : "",
                    t.FromStatus,
                    t.ToStatus,
                    t.Kind,
                    t.CreatedAt,
                    t.MetadataJson
                }).ToList();

                return Json(new { success = true, transitions = list });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, transitions = Array.Empty<object>() });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("ChangePassword")]
        public async Task<IActionResult> ChangePassword(string oldPassword, string newPassword)
        {
            Console.WriteLine($"=== ChangePassword Called ===");
            Console.WriteLine($"Time: {DateTime.Now}");
            Console.WriteLine($"User.Identity.Name: {User.Identity?.Name}");
            Console.WriteLine($"User.Identity.IsAuthenticated: {User.Identity?.IsAuthenticated}");
            Console.WriteLine($"Received oldPassword: {(string.IsNullOrEmpty(oldPassword) ? "EMPTY" : "HAS VALUE")}");
            Console.WriteLine($"Received newPassword: {(string.IsNullOrEmpty(newPassword) ? "EMPTY" : "HAS VALUE")}");

            // Log all form values for debugging
            if (Request.HasFormContentType)
            {
                Console.WriteLine("Form values:");
                foreach (var key in Request.Form.Keys)
                {
                    Console.WriteLine($"  {key}: {Request.Form[key]}");
                }
            }

            if (string.IsNullOrEmpty(oldPassword) || string.IsNullOrEmpty(newPassword))
            {
                Console.WriteLine("ERROR: One or both passwords are empty");
                return BadRequest(new
                {
                    message = "Invalid data",
                    oldPasswordReceived = !string.IsNullOrEmpty(oldPassword),
                    newPasswordReceived = !string.IsNullOrEmpty(newPassword)
                });
            }

            try
            {
                // Get the actual email from claims
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
                var userName = User.Identity?.Name;

                Console.WriteLine($"Using email for lookup: {userEmail}");
                Console.WriteLine($"Using username for lookup: {userName}");

                // Try with email first, then username
                var lookupValue = userEmail ?? userName;

                if (string.IsNullOrEmpty(lookupValue))
                {
                    Console.WriteLine("ERROR: Cannot find user email or username in claims");
                    return BadRequest(new { message = "User not found in session" });
                }

                Console.WriteLine($"Looking up user with: {lookupValue}");

                var result = await _dashboardService.ChangePasswordTextAsync(
                    lookupValue,
                    oldPassword,
                    newPassword
                );

                if (!result)
                {
                    Console.WriteLine("ERROR: Old password is incorrect or user not found");
                    return BadRequest(new { message = "Old password is incorrect" });
                }

                Console.WriteLine("SUCCESS: Password changed successfully");
                return Ok(new { message = "Password changed successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPTION: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                Console.WriteLine($"Exception Type: {ex.GetType().FullName}");

                // Check for inner exception
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"INNER EXCEPTION: {ex.InnerException.Message}");
                    Console.WriteLine($"INNER Stack Trace: {ex.InnerException.StackTrace}");
                }

                return StatusCode(500, new
                {
                    message = "Internal server error",
                    error = ex.Message,
                    errorType = ex.GetType().Name
                });
            }
        }
        // ========== USER PREFERENCES ==========

        // POST: /Admin/SaveUserPreference
        [HttpPost]
        public async Task<IActionResult> SaveUserPreference(string key, string value)
        {
            var userId = await GetCurrentAdminUserIdAsync();
            if (!userId.HasValue)
                return Json(new { success = false, message = "User not authenticated" });

            try
            {
                var existing = await _context.UserPreferences
                    .FirstOrDefaultAsync(up => up.UserId == userId.Value && up.Key == key);

                if (existing != null)
                {
                    existing.Value = value;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _context.UserPreferences.Add(new UserPreference
                    {
                        UserId = userId.Value,
                        Key = key,
                        Value = value,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception)
            {
                // Attempt to create the table if it doesn't exist, then retry once
                await EnsureUserPreferencesTableAsync();

                var existingRetry = await _context.UserPreferences
                    .FirstOrDefaultAsync(up => up.UserId == userId.Value && up.Key == key);

                if (existingRetry != null)
                {
                    existingRetry.Value = value;
                    existingRetry.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _context.UserPreferences.Add(new UserPreference
                    {
                        UserId = userId.Value,
                        Key = key,
                        Value = value,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true, created = true });
            }
        }

        // GET: /Admin/GetUserPreference
        [HttpGet]
        public async Task<IActionResult> GetUserPreference(string key)
        {
            var userId = await GetCurrentAdminUserIdAsync();
            if (!userId.HasValue)
                return Json(new { success = false, value = "" });

            try
            {
                var preference = await _context.UserPreferences
                    .FirstOrDefaultAsync(up => up.UserId == userId.Value && up.Key == key);

                return Json(new { success = true, value = preference?.Value ?? "" });
            }
            catch (Exception)
            {
                await EnsureUserPreferencesTableAsync();
                var preference = await _context.UserPreferences
                    .FirstOrDefaultAsync(up => up.UserId == userId.Value && up.Key == key);
                return Json(new { success = true, value = preference?.Value ?? "" });
            }
        }

        private async Task EnsureUserPreferencesTableAsync()
        {
            try
            {
                // Check if table exists first
                var tableExists = await _context.Database.SqlQueryRaw<int>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'UserPreferences'").FirstOrDefaultAsync();
                if (tableExists == 0)
                {
                    var sql = @"
CREATE TABLE `UserPreferences` (
    `PreferenceId` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `Key` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Value` varchar(500) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    PRIMARY KEY (`PreferenceId`),
    KEY `IX_UserPreferences_UserId_Key` (`UserId`, `Key`),
    CONSTRAINT `FK_UserPreferences_Users_UserId` FOREIGN KEY (`UserId`) REFERENCES `Users` (`UserId`) ON DELETE CASCADE
) CHARACTER SET=utf8mb4;";

                    await _context.Database.ExecuteSqlRawAsync(sql);
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't throw to avoid breaking the application
                System.Diagnostics.Debug.WriteLine($"Error ensuring UserPreferences table: {ex.Message}");
            }
        }
    }
}
