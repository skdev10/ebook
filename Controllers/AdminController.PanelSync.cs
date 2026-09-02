using EBookDashboard.Infrastructure;
using EBookDashboard.Models;
using EBookDashboard.Models.Options;
using EBookDashboard.Models.ViewModels;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EBookDashboard.Controllers;

public partial class AdminController
{
    /// <summary>GET /Admin/UserDetail — 360 view of a user matching every user-panel screen.</summary>
    [HttpGet]
    public async Task<IActionResult> UserDetail(int id)
    {
        if (!await IsCurrentUserAdminAsync())
            return RedirectToAction("AccessDenied", "Account");

        var workspace = await _panelSync.GetUserWorkspaceAsync(id);
        if (workspace == null)
            return NotFound();

        ViewData["Title"] = workspace.FullName;
        return View(workspace);
    }

    /// <summary>GET /Admin/BookDetail — 360 view of a book (Writer / Format / Cover / Publish).</summary>
    [HttpGet]
    public async Task<IActionResult> BookDetail(int id)
    {
        if (!await IsCurrentUserAdminAsync())
            return RedirectToAction("AccessDenied", "Account");

        var workspace = await _panelSync.GetBookWorkspaceAsync(id);
        if (workspace == null)
            return NotFound();

        ViewData["Title"] = workspace.Title;
        return View(workspace);
    }

    /// <summary>JSON book workspace for admin book-list modals.</summary>
    [HttpGet]
    public async Task<IActionResult> GetBookDetails(int bookId)
    {
        var workspace = await _panelSync.GetBookWorkspaceAsync(bookId);
        if (workspace == null)
            return Json(new { success = false, message = "Book not found" });
        return Json(new { success = true, book = workspace });
    }

    /// <summary>Active subscriptions as they appear on the user Subscriptions screen.</summary>
    [HttpGet]
    public async Task<IActionResult> GetSubscribers()
    {
        var now = DateTime.UtcNow;
        var rows = await (from ap in _context.AuthorPlans.AsNoTracking()
                          join u in _context.Users.AsNoTracking() on ap.UserId equals u.UserId
                          orderby ap.CreatedAt descending
                          select new
                          {
                              subscriptionId = ap.AuthorPlanId,
                              userId = ap.UserId,
                              userName = u.FullName,
                              userEmail = u.UserEmail,
                              planName = ap.PlanName,
                              startDate = ap.StartDate.ToString("yyyy-MM-dd"),
                              endDate = ap.EndDate.Year > 1981 ? ap.EndDate.ToString("yyyy-MM-dd") : "",
                              status = ap.IsActive == 1 && (ap.EndDate.Year < 1981 || ap.EndDate >= now)
                                  ? "Active"
                                  : ap.IsActive == 0 ? "Cancelled" : "Expired"
                          }).Take(400).ToListAsync();

        return Json(new { success = true, subscribers = rows });
    }

    /// <summary>User-panel billing records (AuthorBills) for the Subscriptions payments tab.</summary>
    [HttpGet]
    public async Task<IActionResult> GetPayments()
    {
        var rows = await (from b in _context.AuthorBills.AsNoTracking()
                          join u in _context.Users.AsNoTracking() on b.UserId equals u.UserId into ug
                          from u in ug.DefaultIfEmpty()
                          orderby b.CreatedAt descending
                          select new
                          {
                              transactionId = b.BillId,
                              userName = u != null ? u.FullName : (b.UserEmail ?? "Unknown"),
                              userEmail = u != null ? u.UserEmail : b.UserEmail,
                              amount = b.TotalAmount,
                              currency = b.Currency ?? "usd",
                              createdAt = b.CreatedAt,
                              status = string.IsNullOrWhiteSpace(b.Status) ? (b.IsActive == 1 ? "Paid" : "Pending") : b.Status,
                              paymentReference = b.PaymentReference,
                              description = b.Description
                          }).Take(400).ToListAsync();

        return Json(new { success = true, payments = rows });
    }

    /// <summary>Subscription KPIs used on the admin Subscriptions screen.</summary>
    [HttpGet]
    public async Task<IActionResult> GetSubscriptionStats()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var plans = await _context.AuthorPlans.AsNoTracking().ToListAsync();
        var totalSubscribers = plans.Count(p => p.IsActive == 1 && (p.EndDate.Year < 1981 || p.EndDate >= now));
        var total = Math.Max(plans.Count, 1);
        var monthlyRevenue = await _context.AuthorBills.AsNoTracking()
            .Where(b => b.CreatedAt >= monthStart)
            .SumAsync(b => (decimal?)b.TotalAmount) ?? 0m;
        var expiringSoon = plans.Count(p =>
            p.IsActive == 1 && p.EndDate.Year > 1981 && p.EndDate >= now && p.EndDate <= now.AddDays(14));
        var retentionRate = Math.Round((decimal)totalSubscribers / total * 100, 1);

        return Json(new
        {
            success = true,
            stats = new
            {
                totalSubscribers,
                monthlyRevenue,
                retentionRate,
                expiringSoon
            }
        });
    }

    /// <summary>Creates in-app notifications for the same inbox the user panel reads.</summary>
    [HttpPost]
    public async Task<IActionResult> CreateNotification([FromBody] AdminCreateNotificationRequest? req)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        if (req == null || string.IsNullOrWhiteSpace(req.Title) || string.IsNullOrWhiteSpace(req.Message))
            return Json(new { success = false, message = "Title and message are required" });

        var type = string.IsNullOrWhiteSpace(req.Type) ? "Info" : req.Type.Trim();
        var audience = (req.Audience ?? "all").Trim().ToLowerInvariant();
        var created = 0;

        if (audience == "all")
        {
            _context.Notifications.Add(new Notification
            {
                UserId = null,
                Title = req.Title.Trim(),
                Message = req.Message.Trim(),
                Type = type,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
            created = 1;
        }
        else
        {
            var query = _context.Users.AsNoTracking().AsQueryable();
            if (audience == "active")
                query = query.Where(u => u.Status == "Active");
            else if (audience == "premium")
            {
                var premiumIds = await _context.AuthorPlans.AsNoTracking()
                    .Where(p => p.IsActive == 1)
                    .Select(p => p.UserId)
                    .Distinct()
                    .ToListAsync();
                query = query.Where(u => premiumIds.Contains(u.UserId));
            }

            var userIds = await query.Select(u => u.UserId).Take(2000).ToListAsync();
            foreach (var uid in userIds)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = uid,
                    Title = req.Title.Trim(),
                    Message = req.Message.Trim(),
                    Type = type,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                created++;
            }
        }

        await _context.SaveChangesAsync();
        await LogAuditActionAsync("Create", "Notification", null, $"Sent '{req.Title}' to {audience} ({created})");
        return Json(new { success = true, created, message = "Notification sent" });
    }

    /// <summary>Lists stored notifications for the admin Notifications screen.</summary>
    [HttpGet]
    public async Task<IActionResult> ListManagedNotifications(int take = 50)
    {
        take = Math.Clamp(take, 1, 200);
        var rows = await _context.Notifications.AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .Select(n => new
            {
                id = n.NotificationId,
                title = n.Title,
                message = n.Message,
                type = n.Type,
                isRead = n.IsRead,
                userId = n.UserId,
                createdAt = n.CreatedAt.ToString("yyyy-MM-dd HH:mm")
            })
            .ToListAsync();
        return Json(new { success = true, notifications = rows });
    }

    /// <summary>Support tickets logged from the user Support screen (human-agent mode).</summary>
    [HttpGet]
    public async Task<IActionResult> GetSupportTickets()
    {
        var rows = await (from n in _context.Notifications.AsNoTracking()
                          join u in _context.Users.AsNoTracking() on n.UserId equals u.UserId into ug
                          from u in ug.DefaultIfEmpty()
                          where n.Type == "Support" || n.Type == "Feedback"
                          orderby n.CreatedAt descending
                          select new
                          {
                              id = n.NotificationId,
                              title = n.Title,
                              message = n.Message,
                              type = n.Type,
                              status = n.IsRead ? "resolved" : "open",
                              userName = u != null ? u.FullName : "Guest",
                              userEmail = u != null ? u.UserEmail : "",
                              createdAt = n.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                          }).Take(200).ToListAsync();
        return Json(new { success = true, tickets = rows });
    }

    /// <summary>Marks a support ticket resolved (notification read).</summary>
    [HttpPost]
    public async Task<IActionResult> ResolveSupportTicket(int notificationId)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        var n = await _context.Notifications.FindAsync(notificationId);
        if (n == null)
            return Json(new { success = false, message = "Ticket not found" });
        n.IsRead = true;
        n.ReadAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Json(new { success = true });
    }

    /// <summary>CMS copy used by landing / terms / help — persisted in Settings.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCmsContent()
    {
        var keys = new[]
        {
            "cms:heroTitle", "cms:heroDescription", "cms:features",
            "cms:terms", "cms:privacy", "cms:faq", "cms:videoLinks", "cms:articleLinks"
        };
        var rows = await _context.Settings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");
        return Json(new
        {
            success = true,
            content = new
            {
                heroTitle = rows.GetValueOrDefault("cms:heroTitle", ""),
                heroDescription = rows.GetValueOrDefault("cms:heroDescription", ""),
                features = rows.GetValueOrDefault("cms:features", ""),
                terms = rows.GetValueOrDefault("cms:terms", ""),
                privacy = rows.GetValueOrDefault("cms:privacy", ""),
                faq = rows.GetValueOrDefault("cms:faq", ""),
                videoLinks = rows.GetValueOrDefault("cms:videoLinks", ""),
                articleLinks = rows.GetValueOrDefault("cms:articleLinks", "")
            }
        });
    }

    /// <summary>Saves CMS copy so user-facing help / legal pages can read the same keys.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveCmsContent([FromBody] AdminCmsContentViewModel? model)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        if (model == null)
            return Json(new { success = false, message = "No content" });

        var map = new Dictionary<string, string?>
        {
            ["cms:heroTitle"] = model.HeroTitle,
            ["cms:heroDescription"] = model.HeroDescription,
            ["cms:features"] = model.Features,
            ["cms:terms"] = model.Terms,
            ["cms:privacy"] = model.Privacy,
            ["cms:faq"] = model.Faq,
            ["cms:videoLinks"] = model.VideoLinks,
            ["cms:articleLinks"] = model.ArticleLinks
        };
        await UpsertSettingsAsync(map, "Cms");
        await LogAuditActionAsync("Update", "Cms", null, "Updated CMS content");
        return Json(new { success = true, message = "Content saved" });
    }

    /// <summary>GET /Admin/AICoverManagement — live cover rows from the user Cover Design studio.</summary>
    [HttpGet]
    public async Task<IActionResult> AICoverManagement()
    {
        if (!await IsCurrentUserAdminAsync())
            return RedirectToAction("AccessDenied", "Account");

        var today = DateTime.UtcNow.Date;
        var covers = await _context.BookCoverDesigns.AsNoTracking()
            .Where(c => !c.IsDeleted)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(80)
            .ToListAsync();
        var bookIds = covers.Select(c => c.BookId).Distinct().ToList();
        var userIds = covers.Select(c => c.UserId).Distinct().ToList();
        var bookMap = await _context.Books.AsNoTracking()
            .Where(b => bookIds.Contains(b.BookId))
            .ToDictionaryAsync(b => b.BookId, b => b.Title);
        var userMap = await _context.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.FullName);

        var settings = await _context.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith("admin:cover"))
            .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");

        var vm = new AdminCoverManagementViewModel
        {
            TotalCovers = await _context.BookCoverDesigns.CountAsync(c => !c.IsDeleted),
            CoversToday = await _context.BookCoverDesigns.CountAsync(c => !c.IsDeleted && c.CreatedAt >= today),
            ActiveCovers = await _context.BookCoverDesigns.CountAsync(c => !c.IsDeleted && c.IsActive),
            FailedCovers = await _context.BookCoverDesigns.CountAsync(c => !c.IsDeleted && c.Status == "Failed"),
            CoverPrice = settings.GetValueOrDefault("admin:coverPrice", "2.99"),
            DailyLimit = settings.GetValueOrDefault("admin:coverDailyLimit", "100"),
            MonthlyLimit = settings.GetValueOrDefault("admin:coverMonthlyLimit", "2000"),
            DefaultPrompt = settings.GetValueOrDefault("admin:coverDefaultPrompt", ""),
            Recent = covers.Select(c => new AdminCoverManagementRow
            {
                CoverId = c.CoverId,
                BookId = c.BookId,
                BookTitle = bookMap.GetValueOrDefault(c.BookId, "Untitled"),
                UserName = userMap.GetValueOrDefault(c.UserId, "Unknown"),
                CoverType = c.CoverType,
                Status = c.Status,
                FrontImagePath = c.FrontImagePath,
                UpdatedAt = c.UpdatedAt
            }).ToList()
        };
        ViewData["Title"] = "AI Cover Management";
        return View(vm);
    }

    /// <summary>Persists cover pricing / prompt used by the user Cover Design studio.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveCoverSettings([FromBody] AdminCoverSettingsRequest? req)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        if (req == null)
            return Json(new { success = false, message = "No settings" });

        await UpsertSettingsAsync(new Dictionary<string, string?>
        {
            ["admin:coverPrice"] = req.CoverPrice,
            ["admin:coverDailyLimit"] = req.DailyLimit,
            ["admin:coverMonthlyLimit"] = req.MonthlyLimit,
            ["admin:coverDefaultPrompt"] = req.DefaultPrompt
        }, "Cover");
        await LogAuditActionAsync("Update", "CoverSettings", null, "Updated cover pricing / prompt");
        return Json(new { success = true, message = "Cover settings saved" });
    }

    /// <summary>Author row matching the Authors table used when a user creates books.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAuthorDetails(int authorId)
    {
        var author = await _context.Authors.AsNoTracking()
            .Include(a => a.Books)
            .FirstOrDefaultAsync(a => a.AuthorId == authorId);
        if (author == null)
            return Json(new { success = false, message = "Author not found" });

        var linkedUser = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserEmail == author.AuthorEmail || u.AuthorCode == author.AuthorCode);

        return Json(new
        {
            success = true,
            author = new
            {
                author.AuthorId,
                author.FullName,
                author.AuthorEmail,
                author.Specialization,
                author.Country,
                author.Phone,
                author.Address,
                author.AuthorCode,
                author.IsActive,
                bookCount = author.Books?.Count ?? 0,
                linkedUserId = linkedUser?.UserId,
                workspaceUrl = linkedUser == null ? null : Url.Action("UserDetail", "Admin", new { id = linkedUser.UserId })
            }
        });
    }

    /// <summary>Creates or updates an author record (same table the user panel writes on signup/book create).</summary>
    [HttpPost]
    public async Task<IActionResult> SaveAuthor([FromBody] AdminSaveAuthorRequest? req)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        if (req == null || string.IsNullOrWhiteSpace(req.FullName))
            return Json(new { success = false, message = "Name is required" });

        Authors author;
        if (req.AuthorId > 0)
        {
            author = await _context.Authors.FindAsync(req.AuthorId);
            if (author == null)
                return Json(new { success = false, message = "Author not found" });
        }
        else
        {
            author = new Authors
            {
                AuthorCode = string.IsNullOrWhiteSpace(req.AuthorCode) ? Guid.NewGuid().ToString("N")[..12] : req.AuthorCode.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _context.Authors.Add(author);
        }

        author.FullName = req.FullName.Trim();
        author.AuthorEmail = req.AuthorEmail?.Trim() ?? "";
        author.Specialization = req.Specialization?.Trim() ?? "";
        author.Country = req.Country?.Trim() ?? "";
        author.Phone = req.Phone?.Trim() ?? "";
        author.Address = req.Address?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(req.AuthorCode))
            author.AuthorCode = req.AuthorCode.Trim();
        author.IsActive = req.IsActive;
        author.Status = req.IsActive ? "Active" : "Inactive";

        await _context.SaveChangesAsync();
        await LogAuditActionAsync(req.AuthorId > 0 ? "Update" : "Create", "Author", author.AuthorId, author.FullName);
        return Json(new { success = true, authorId = author.AuthorId });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAuthor(int authorId)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        var author = await _context.Authors.FindAsync(authorId);
        if (author == null)
            return Json(new { success = false, message = "Author not found" });
        var hasBooks = await _context.Books.AnyAsync(b => b.AuthorId == authorId);
        if (hasBooks)
            return Json(new { success = false, message = "Cannot delete: this author still has books. Reassign or delete books first." });
        _context.Authors.Remove(author);
        await _context.SaveChangesAsync();
        await LogAuditActionAsync("Delete", "Author", authorId, author.FullName);
        return Json(new { success = true });
    }

    /// <summary>Live dependency status for APIs the user panel actually calls (no secrets returned).</summary>
    [HttpGet]
    public async Task<IActionResult> GetApiHealth()
    {
        var config = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var ext = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<ExternalApiOptions>>().Value;
        var key = ExternalApiKeyResolver.Resolve(config);
        var stripePub = StripeKeys.Publishable(config);
        var stripeSec = StripeKeys.Secret(config);
        var dbOk = false;
        try { dbOk = await _context.Database.CanConnectAsync(); } catch { }

        string ping = "not_tested";
        if (!string.IsNullOrWhiteSpace(ext.BaseUrl) && !string.IsNullOrEmpty(key))
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                var url = ext.BaseUrl.TrimEnd('/') + "/";
                using var resp = await http.GetAsync(url);
                ping = ((int)resp.StatusCode) + " " + resp.ReasonPhrase;
            }
            catch (Exception ex)
            {
                ping = "error: " + ex.Message;
            }
        }
        else if (string.IsNullOrEmpty(key))
            ping = "skipped_no_key";

        return Json(new
        {
            success = true,
            environment = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().EnvironmentName,
            items = new object[]
            {
                new { name = "Database", configured = dbOk, detail = dbOk ? "Connected" : "Cannot connect", kind = "db" },
                new { name = "Book / Cover AI (ExternalApi)", configured = !string.IsNullOrEmpty(key), detail = string.IsNullOrWhiteSpace(ext.BaseUrl) ? "BaseUrl empty" : ext.BaseUrl + " · " + ping, kind = "external" },
                new { name = "Stripe", configured = !string.IsNullOrEmpty(stripePub) && !string.IsNullOrEmpty(stripeSec), detail = string.IsNullOrEmpty(stripeSec) ? "Secret key not set" : "Keys present (env)", kind = "stripe" },
                new { name = "SMTP", configured = !string.IsNullOrWhiteSpace(config["Email:SmtpServer"]) && !string.IsNullOrWhiteSpace(config["Email:Username"]), detail = config["Email:SmtpServer"] ?? "not set", kind = "email" }
            }
        });
    }

    /// <summary>Saves the signed-in admin's display name (Admin Settings profile tab).</summary>
    [HttpPost]
    public async Task<IActionResult> SaveAdminProfile(string? fullName, string? firstName, string? lastName)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        var id = await GetCurrentAdminUserIdAsync();
        if (!id.HasValue)
            return Json(new { success = false, message = "Admin not found" });
        var user = await _context.Users.FindAsync(id.Value);
        if (user == null)
            return Json(new { success = false, message = "Admin not found" });
        var combined = string.IsNullOrWhiteSpace(fullName)
            ? string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)))
            : fullName.Trim();
        if (!string.IsNullOrWhiteSpace(combined))
            user.FullName = combined.Trim();
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Json(new { success = true, message = "Profile saved" });
    }

    [HttpPost]
    public async Task<IActionResult> CreateRole([FromBody] AdminSaveRoleRequest? req)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        if (req == null || string.IsNullOrWhiteSpace(req.RoleName))
            return Json(new { success = false, message = "Role name is required" });

        var name = req.RoleName.Trim();
        var nameTaken = await _context.Roles.AnyAsync(r => r.RoleName == name && r.RoleId != req.RoleId);
        if (nameTaken)
            return Json(new { success = false, message = "A role with that name already exists" });

        Roles role;
        var isUpdate = req.RoleId > 0;
        if (isUpdate)
        {
            role = await _context.Roles.FindAsync(req.RoleId);
            if (role == null)
                return Json(new { success = false, message = "Role not found" });
        }
        else
        {
            role = new Roles();
            _context.Roles.Add(role);
        }

        role.RoleName = name;
        role.Description = req.Description?.Trim() ?? "";
        role.AllowDownloads = req.AllowDownloads;
        role.AllowFullDashboard = req.AllowFullDashboard;
        role.AllowAnalytics = req.AllowAnalytics;
        role.AllowPublishing = req.AllowPublishing;
        role.AllowEdit = req.AllowEdit;
        role.AllowDelete = req.AllowDelete;
        await _context.SaveChangesAsync();
        await LogAuditActionAsync(isUpdate ? "Update" : "Create", "Role", role.RoleId, role.RoleName);
        return Json(new { success = true, roleId = role.RoleId });
    }

    /// <summary>Persists optional admin Settings-tab fields that are not columns on Users.</summary>
    [HttpPost]
    public async Task<IActionResult> SaveAdminSettings([FromBody] Dictionary<string, string?>? map)
    {
        if (!await IsCurrentUserAdminAsync())
            return Json(new { success = false, message = "Unauthorized" });
        if (map == null || map.Count == 0)
            return Json(new { success = false, message = "Nothing to save" });

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "country", "city", "timezone", "language",
            "notifyEmail", "notifyPush", "notifySms",
            "bio", "website", "payoutEmail", "taxId",
            "notifyBlob", "prefsBlob", "payoutBlob"
        };
        var toSave = new Dictionary<string, string?>();
        foreach (var kv in map)
        {
            if (!allowed.Contains(kv.Key)) continue;
            toSave["admin:settings:" + kv.Key] = kv.Value;
        }
        if (toSave.Count == 0)
            return Json(new { success = false, message = "No valid fields" });
        await UpsertSettingsAsync(toSave, "Admin");
        return Json(new { success = true, message = "Saved" });
    }

    private async Task UpsertSettingsAsync(Dictionary<string, string?> map, string category)
    {
        var keys = map.Keys.ToList();
        var existing = await _context.Settings.Where(s => keys.Contains(s.Key)).ToListAsync();
        var byKey = existing.ToDictionary(s => s.Key);
        var nextId = 0;
        foreach (var kv in map)
        {
            var value = EBookDashboard.Models.Settings.ClampValueLength(kv.Value, EBookDashboard.Models.Settings.DbCompatMaxValueLength);
            if (byKey.TryGetValue(kv.Key, out var row))
            {
                row.Value = value;
                row.UpdatedAt = DateTime.UtcNow;
                continue;
            }
            if (nextId == 0)
                nextId = await _context.NextSettingIdAsync();
            else
                nextId++;
            _context.Settings.Add(new EBookDashboard.Models.Settings
            {
                SettingId = nextId,
                Key = kv.Key,
                Value = value,
                Category = category,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
    }
}

public sealed class AdminCreateNotificationRequest
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Message { get; set; }
    public string? Audience { get; set; }
}

public sealed class AdminCoverSettingsRequest
{
    public string? CoverPrice { get; set; }
    public string? DailyLimit { get; set; }
    public string? MonthlyLimit { get; set; }
    public string? DefaultPrompt { get; set; }
}

public sealed class AdminSaveAuthorRequest
{
    public int AuthorId { get; set; }
    public string? FullName { get; set; }
    public string? AuthorEmail { get; set; }
    public string? Specialization { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? AuthorCode { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class AdminSaveRoleRequest
{
    public int RoleId { get; set; }
    public string? RoleName { get; set; }
    public string? Description { get; set; }
    public bool AllowDownloads { get; set; }
    public bool AllowFullDashboard { get; set; }
    public bool AllowAnalytics { get; set; }
    public bool AllowPublishing { get; set; }
    public bool AllowEdit { get; set; }
    public bool AllowDelete { get; set; }
}
