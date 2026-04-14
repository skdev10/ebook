using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Generators;
using System;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Dynamic;
using System.Linq;
using Microsoft.AspNetCore.Authentication;

namespace EBookDashboard.Controllers
{
    [Authorize]
    [Route("Dashboard")]
    public class DashboardController : Controller
    {
        private readonly IFeatureCartService _featureCartService;
        private readonly ApplicationDbContext _context;
        private readonly IDashboardService _dashboardService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DashboardController> _logger;
        private readonly IBookService _bookService;
        private readonly IBookPdfService _bookPdfService;

        public DashboardController(
            IFeatureCartService featureCartService,
            ApplicationDbContext context,
            IDashboardService dashboardService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<DashboardController> logger,
            IBookService bookService,
            IBookPdfService bookPdfService)
        {
            _featureCartService = featureCartService;
            _context = context;
            _dashboardService = dashboardService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _bookService = bookService;
            _bookPdfService = bookPdfService;
        }

        [Route("")]
        [Route("Index")]
        public async Task<IActionResult> Index()
        {
            // Temporarily bypass the feature check to allow login
            /*
            // Check if user has an active plan
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))  
            {
                var hasActivePlan = await _featureCartService.HasActivePlanAsync(userId);
                if (!hasActivePlan)
                {
                    // Redirect to features selection if no active plan
                    return RedirectToAction("Index", "Features");
                }
            }
            */

            // Get user information
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserEmail == userEmail);

            if (user == null)
            {
                ViewBag.UserName = User.Identity?.Name ?? "User";
                ViewBag.UserEmail = userEmail;
                ViewBag.AuthorName = User.FindFirst("AuthorName")?.Value ?? "";
                ViewBag.Genre = User.FindFirst("Genre")?.Value ?? "";
                return View(new DashboardIndexViewModel { UserName = ViewBag.UserName as string ?? "User" });
            }

            // Get author information
            int? userId = HttpContext.Session.GetInt32("UserId");
            ViewBag.UserId = userId; // ✅ send to Razor view
            var author = await _context.Authors
                .FirstOrDefaultAsync(a => a.AuthorCode == user.UserId.ToString());

            // Get user's books
            var books = await _context.Books
                .Where(b => b.UserId == user.UserId)
                .ToListAsync();

            // Get chapter counts from apirawresponse (generated chapters per UserId + BookId)
            var bookIds = books.Select(b => b.BookId).ToList();
            var rawResponseCounts = await _context.APIRawResponse
                .Where(r => r.UserId == user.UserId && r.BookId != null && bookIds.Contains(r.BookId.Value))
                .GroupBy(r => r.BookId)
                .Select(g => new { BookId = g.Key, Count = g.Count() })
                .ToListAsync();
            var chaptersGeneratedByBookId = rawResponseCounts.ToDictionary(x => x.BookId!.Value, x => x.Count);

            // Calculate real statistics from database
            var totalBooksPublished = books.Count(b => b.Status == "Published");
            var totalBooksGenerated = books.Count(b => b.Status == "Finalized");

            // Load user reading stats (UserStats table) - one row per user
            var userStats = await _context.UserStats
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == user.UserId);

            // Calculate monthly revenue from BookPrice table (if exists)
            var currentMonth = DateTime.UtcNow.Month;
            var currentYear = DateTime.UtcNow.Year;
            var monthlyRevenue = await _context.BookPrice
                .Where(bp => bp.CreatedAt.Month == currentMonth && bp.CreatedAt.Year == currentYear)
                .Join(_context.Books.Where(b => b.UserId == user.UserId),
                    bp => bp.BookId,
                    b => b.BookId,
                    (bp, b) => bp)
                .SumAsync(bp => (decimal?)bp.bookPrice) ?? 0m;
            
            // Calculate total downloads from database (if you have a Downloads table)
            // For now, using book count as a placeholder - replace with actual downloads table when available
            var totalDownloads = books.Count; // Replace with actual downloads count when Downloads table exists
            
            // Calculate average rating from database (if you have a Ratings table)
            // For now, return 0 if no ratings exist
            var averageRating = 0m; // Replace with actual rating calculation when Ratings table exists

            // Ensure demo books exist in DB for this user (same as attached image)
            books = await EnsureDemoBooksAsync(user, author, books);
            bookIds = books.Select(b => b.BookId).ToList();
            rawResponseCounts = await _context.APIRawResponse
                .Where(r => r.UserId == user.UserId && r.BookId != null && bookIds.Contains(r.BookId.Value))
                .GroupBy(r => r.BookId)
                .Select(g => new { BookId = g.Key, Count = g.Count() })
                .ToListAsync();
            chaptersGeneratedByBookId = rawResponseCounts.ToDictionary(x => x.BookId!.Value, x => x.Count);
            totalBooksPublished = books.Count(b => b.Status == "Published" || b.Status == "Finalized");
            totalBooksGenerated = books.Count(b => b.Status == "Finalized");

            // Demo data matching the attached image (exact books, covers, reader friends)
            var demoPublished = GetDemoPublishedBooks(books);
            var demoDrafts = GetDemoDrafts(books);
            var demoHero = GetDemoHeroBook(books);
            var demoCurrentRead = GetDemoCurrentRead(books);
            var demoReaderFriends = GetDemoReaderFriends();

            var viewModel = new DashboardIndexViewModel
            {
                UserName = user.FullName,
                UserEmail = user.UserEmail,
                TotalBooksPublished = totalBooksPublished,
                MonthlyRevenue = monthlyRevenue,
                TotalDownloads = totalDownloads,
                AverageRating = averageRating,
                CurrentProjects = books.Select(b =>
                {
                    var totalChapters = chaptersGeneratedByBookId.GetValueOrDefault(b.BookId, 0);
                    var progressPct = b.Status == "Published" || b.Status == "Finalized" ? 100 : totalChapters > 0 ? 100 : 0;
                    var progressText = b.Status == "Published" || b.Status == "Finalized" ? "Complete" : totalChapters > 0 ? $"Chapter {totalChapters} of {totalChapters}" : "No chapters yet";
                    return new ProjectViewModel
                    {
                        BookId = b.BookId,
                        Title = b.Title,
                        Status = b.Status,
                        ProgressPercentage = progressPct,
                        ProgressText = progressText,
                        CoverImagePath = b.CoverImagePath
                    };
                }).ToList(),
                RecentActivities = new List<ActivityViewModel>
                {
                    new ActivityViewModel { Title = "Chapter 5 of \"The Art of Digital Publishing\" was updated", Description = "Your latest changes have been saved successfully", TimeAgo = "2 hours ago", IconClass = "fas fa-book" },
                    new ActivityViewModel { Title = "New order for \"Modern Web Development\" received", Description = "Customer purchased 3 copies of your book", TimeAgo = "5 hours ago", IconClass = "fas fa-shopping-cart" },
                    new ActivityViewModel { Title = "New review for \"AI in Everyday Life\"", Description = "Received 5-star rating with positive feedback", TimeAgo = "1 day ago", IconClass = "fas fa-comment" },
                    new ActivityViewModel { Title = "New manuscript uploaded for \"Creative Writing Techniques\"", Description = "File processed and ready for editing", TimeAgo = "2 days ago", IconClass = "fas fa-file-alt" }
                },
                CurrentWorkingBook = demoHero.book != null ? new BookViewModel { BookId = demoHero.book.BookId, Title = demoHero.book.Title, BookIdText = demoHero.book.BookId.ToString(), ProgressPercentage = 100, ProgressText = "Complete", CoverImagePath = demoHero.coverUrl } : CreateCurrentWorkingBook(books, chaptersGeneratedByBookId),
                TotalBooksGenerated = totalBooksGenerated,
                BooksRead = 13,
                HoursRead = userStats?.HoursRead ?? 45,
                PagesRead = userStats?.PagesRead ?? 115,
                DayStreak = userStats?.DayStreak ?? 26,
                DemoHeroTitle = demoHero.title,
                DemoHeroDescription = demoHero.description,
                DemoHeroCoverUrl = demoHero.coverUrl,
                DemoHeroBookId = demoHero.book?.BookId,
                DemoCurrentReadTitle = demoCurrentRead.title,
                DemoCurrentReadProgressLabel = demoCurrentRead.progressLabel,
                DemoCurrentReadPercent = demoCurrentRead.percent,
                DemoCurrentReadCoverUrl = demoCurrentRead.coverUrl,
                DemoCurrentReadBookId = demoCurrentRead.bookId,
                DemoPublishedBooks = demoPublished,
                DemoDrafts = demoDrafts,
                DemoReaderFriends = demoReaderFriends
            };

            ViewBag.ProfilePicturePath = user.ProfilePicturePath;
            return View(viewModel);
        }

        private static readonly string[] DemoCoverUrls = new[]
        {
            "/images/books/the-bird.png",
            "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcRwYdSnCRFL0Hy0etDwEvor8vgwX0qhLdKlDQ&s",
            "/images/books/good-things-are-up-ahead.png",
            "/images/books/fairy-tale.png",
            "https://images.unsplash.com/photo-1519681393784-d120267933ba?q=80&w=2070&auto=format&fit=crop",
            "/images/books/the-wizarding-chronicles.png",
            "/images/books/the-cambers-of-secrets.png"
        };

        private async Task<List<Books>> EnsureDemoBooksAsync(Users user, Authors? author, List<Books> existingBooks)
        {
            var demoTitles = new[] { "The Wizarding Chronicles", "The Bird", "SOUL", "Good Things Are Up Ahead", "Fairy Tale", "Conquest of Flames", "The Cambers of Secrets" };
            var existingTitles = existingBooks.Select(b => b.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var toAdd = demoTitles.Where(t => !existingTitles.Contains(t)).ToList();
            if (toAdd.Count == 0) return existingBooks;

            int authorId = author?.AuthorId ?? 0;
            if (authorId == 0)
            {
                var newAuthor = new Authors { AuthorCode = user.UserId.ToString(), FullName = user.FullName ?? "Author", AuthorEmail = user.UserEmail ?? "", CategoryId = 1, Country = "", City = "", Region = "", PostalCode = "", CountryCode = "", Phone = "", Address = "", Status = "Active", IsActive = true };
                _context.Authors.Add(newAuthor);
                await _context.SaveChangesAsync();
                authorId = newAuthor.AuthorId;
            }

            int catId = 1;
            int langId = 1;
            try
            {
                var cat = await _context.Categories.AsNoTracking().FirstOrDefaultAsync();
                if (cat != null) catId = cat.CategoryId;
                var lang = await _context.Languages.AsNoTracking().FirstOrDefaultAsync();
                if (lang != null) langId = lang.LanguageId;
            }
            catch { }

            var statuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["The Wizarding Chronicles"] = "Draft",
                ["The Bird"] = "Published",
                ["SOUL"] = "Published",
                ["Good Things Are Up Ahead"] = "Published",
                ["Fairy Tale"] = "Published",
                ["Conquest of Flames"] = "Draft",
                ["The Cambers of Secrets"] = "Draft"
            };
            var coverUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["The Wizarding Chronicles"] = DemoCoverUrls[5],
                ["The Bird"] = DemoCoverUrls[0],
                ["SOUL"] = DemoCoverUrls[1],
                ["Good Things Are Up Ahead"] = DemoCoverUrls[2],
                ["Fairy Tale"] = DemoCoverUrls[3],
                ["Conquest of Flames"] = DemoCoverUrls[4],
                ["The Cambers of Secrets"] = DemoCoverUrls[6]
            };

            foreach (var title in toAdd)
            {
                var book = new Books
                {
                    UserId = user.UserId,
                    AuthorId = authorId,
                    CategoryId = catId,
                    LanguageId = langId,
                    Title = title,
                    AuthorCode = user.UserId.ToString(),
                    BookCode = Guid.NewGuid().ToString("N")[..12],
                    Status = statuses.GetValueOrDefault(title, "Draft"),
                    CoverImagePath = coverUrls.GetValueOrDefault(title),
                    isActive = title == "The Wizarding Chronicles" ? 1 : 0,
                    Genre = "Fiction",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Books.Add(book);
            }
            await _context.SaveChangesAsync();
            return await _context.Books.Where(b => b.UserId == user.UserId).ToListAsync();
        }

        private static List<DemoPublishedBookViewModel> GetDemoPublishedBooks(List<Books> books)
        {
            var published = books.Where(b => b.Status == "Published" || b.Status == "Finalized").ToList();
            var titles = new[] { ("The Bird", "Emily Duvert", null), ("SOUL", "Olivia Wilson", "Volume II"), ("Good Things Are Up Ahead", "Saad Rehman", null), ("Fairy Tale", "", null) };
            var urls = new[] { DemoCoverUrls[0], DemoCoverUrls[1], DemoCoverUrls[2], DemoCoverUrls[3] };
            var result = new List<DemoPublishedBookViewModel>();
            for (int i = 0; i < titles.Length; i++)
            {
                var (title, author, sub) = titles[i];
                var book = published.FirstOrDefault(b => b.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
                result.Add(new DemoPublishedBookViewModel
                {
                    Title = title,
                    Author = author,
                    Subtitle = sub,
                    CoverUrl = urls[i],
                    BookId = book?.BookId ?? 0
                });
            }
            return result;
        }

        private static List<DemoDraftViewModel> GetDemoDrafts(List<Books> books)
        {
            var drafts = books.Where(b => b.Status != "Published" && b.Status != "Finalized").ToList();
            var title = "Conquest of Flames";
            var book = drafts.FirstOrDefault(b => b.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            return new List<DemoDraftViewModel>
            {
                new DemoDraftViewModel
                {
                    Title = title,
                    Subtitle = "8 chapters each vol",
                    Volumes = "2 vol",
                    CoverUrl = book?.CoverImagePath ?? DemoCoverUrls[4],
                    BookId = book?.BookId ?? 0
                }
            };
        }

        private static (string title, string description, string coverUrl, Books? book) GetDemoHeroBook(List<Books> books)
        {
            const string title = "The Wizarding Chronicles";
            const string description = "You've delved deep into the wizarding world's secrets. Have Harry's parents died yet? Oops, looks like you're not there yet. Get reading now!";
            var book = books.FirstOrDefault(b => b.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            return (title, description, DemoCoverUrls[5], book);
        }

        private static (string title, string progressLabel, int percent, string coverUrl, int? bookId) GetDemoCurrentRead(List<Books> books)
        {
            const string title = "The Cambers of Secrets";
            var book = books.FirstOrDefault(b => b.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
            return (title, "154 / 300 pages", 51, DemoCoverUrls[6], book?.BookId);
        }

        private static List<DemoReaderFriendViewModel> GetDemoReaderFriends()
        {
            return new List<DemoReaderFriendViewModel>
            {
                new DemoReaderFriendViewModel
                {
                    Name = "Roberto Jordan",
                    Initial = "R",
                    AvatarColor = "bg-orange-400",
                    TimeAgo = "2 min ago",
                    Comment = "What a delightful and magical chapter it is! It indeed transports readers...",
                    Tag = "Chapter Five: Diagon Alley"
                },
                new DemoReaderFriendViewModel
                {
                    Name = "Anna Henry",
                    Initial = "A",
                    AvatarColor = "bg-blue-500",
                    TimeAgo = "1 hr ago",
                    Comment = "I finished reading the chapter last night and couldn't stop thinking about it..."
                }
            };
        }

        private static BookViewModel CreateCurrentWorkingBook(List<Books> books, Dictionary<int, int> chaptersGeneratedByBookId)
        {
            if (books == null || !books.Any())
                return new BookViewModel();
            // "Currently Working On" = book with isActive == 1
            var workingBook = books.FirstOrDefault(b => b.isActive == 1);
            if (workingBook == null)
                return new BookViewModel();
            var totalChapters = chaptersGeneratedByBookId?.GetValueOrDefault(workingBook.BookId, 0) ?? 0;
            var progressPct = workingBook.Status == "Published" ? 100 : totalChapters > 0 ? 100 : 0;
            var progressText = workingBook.Status == "Published" ? "Complete" : totalChapters > 0 ? $"Chapter {totalChapters} of {totalChapters}" : "No chapters yet";
            return new BookViewModel
            {
                BookId = workingBook.BookId,
                Title = workingBook.Title,
                BookIdText = workingBook.BookId.ToString(),
                ProgressPercentage = progressPct,
                ProgressText = progressText,
                CoverImagePath = workingBook.CoverImagePath
            };
        }

        /// <summary>
        /// Save or update reading stats (HoursRead, PagesRead, DayStreak) for the current user.
        /// </summary>
        [HttpPost]
        [Route("Dashboard/UpdateReadingStats")]
        public async Task<IActionResult> UpdateReadingStats([FromBody] UpdateReadingStatsRequest request)
        {
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            if (user == null)
                return Json(new { success = false, message = "User not found." });

            var stats = await _context.UserStats.FirstOrDefaultAsync(s => s.UserId == user.UserId);
            if (stats == null)
            {
                stats = new UserStats
                {
                    UserId = user.UserId,
                    HoursRead = request.HoursRead,
                    PagesRead = request.PagesRead,
                    DayStreak = request.DayStreak,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.UserStats.Add(stats);
            }
            else
            {
                stats.HoursRead = request.HoursRead;
                stats.PagesRead = request.PagesRead;
                stats.DayStreak = request.DayStreak;
                stats.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Reading stats saved." });
        }

        [Route("EBook")]
        public IActionResult EBook()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("CoverDesign")]
        public async Task<IActionResult> CoverDesign(int? bookId = null)
        {
            var formattingDone = HttpContext.Session.GetString("FormattingDone") == "1";
            var hasGeneratedBook = HttpContext.Session.GetString("HasGeneratedBook") == "1";
            if (!hasGeneratedBook || !formattingDone)
            {
                ViewBag.LockMessage = !hasGeneratedBook ? "Please generate your AI book first." : "Complete Book Formatting first, then AI Cover Design will unlock.";
                ViewBag.LockGoto = !hasGeneratedBook ? "/Books/AIGenerateBook" : "/BookDesign/CoverDesignCalculatorFixing";
                ViewBag.LockButtonText = !hasGeneratedBook ? "Go to AI Writer" : "Go to Formatting";
            }
            ViewBag.UserName = User.Identity?.Name ?? "User";
            ViewBag.BookId = bookId ?? 0;
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            var roleId = user?.RoleId ?? 0;
            // RoleId 1 or 2: page stays open, no Upgrade Required popup
            ViewBag.ShowUpgradePrompt = (roleId != 1 && roleId != 2);
            return View();
        }

        /// <summary>
        /// AI cover generation for Cover Design page. Calls the same external service as Books/GenerateAICoverPreview.
        /// Returns { success, status, image_base64?, imageDataUrl?, options[] } for the front-end preview.
        /// </summary>
        [HttpPost]
        [Route("GenerateCover")]
        public async Task<IActionResult> GenerateCover([FromBody] DashboardGenerateCoverRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0)
                return Json(new { success = false, status = "error", message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, status = "error", message = "Please sign in." });

            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (book == null)
                return Json(new { success = false, status = "error", message = "Book not found." });

            var description = (req.Description ?? "").Trim();
            // Stored prompt must fit legacy VARCHAR(1000); full description still drives MapCoverStyleForExternalApi below.
            await UpsertDashboardSettingAsync($"book:{req.BookId}:aiCoverPrompt",
                Models.Settings.ClampValueLength(description, Models.Settings.DbCompatMaxValueLength) ?? "", "Book", cancellationToken);

            var title = string.IsNullOrWhiteSpace(req.Title) ? (book.Title ?? "").Trim() : req.Title!.Trim();
            if (string.IsNullOrEmpty(title)) title = "My Book";

            var authorName = (req.Author ?? "").Trim();
            if (string.IsNullOrEmpty(authorName))
            {
                var u = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == book.UserId, cancellationToken);
                authorName = u?.FullName ?? u?.UserEmail ?? book.UserId.ToString();
            }

            var category = string.IsNullOrWhiteSpace(req.Genre) ? (book.Genre ?? "General").Trim() : req.Genre!.Trim();
            var styleKey = string.IsNullOrWhiteSpace(req.Style) ? "modern" : req.Style.Trim();
            var coverStyleLabel = CoverExternalApiHelper.MapCoverStyleForExternalApi(styleKey, description);

            var size = (_configuration["ExternalApi:CoverGenerateSize"] ?? "1024x1536").Trim();
            var quality = (_configuration["ExternalApi:CoverGenerateQuality"] ?? "medium").Trim();
            var apiUrl = (_configuration["ExternalApi:GenerateCoverUrl"] ?? "http://162.229.248.26:8001/api/generate-cover").Trim();
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();

            var payloadObj = new JObject
            {
                ["title"] = title,
                ["author_name"] = authorName,
                ["category"] = category,
                ["cover_style"] = coverStyleLabel,
                ["size"] = size,
                ["quality"] = quality
            };

            var json = payloadObj.ToString(Newtonsoft.Json.Formatting.None);
            _logger.LogInformation("[Dashboard/GenerateCover] bookId={BookId} url={Url} promptChars={Chars}", req.BookId, apiUrl, (req.Prompt ?? "").Length);

            try
            {
                var client = _httpClientFactory.CreateClient();
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(apiKey))
                    httpRequest.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMinutes(3));
                var response = await client.SendAsync(httpRequest, cts.Token);
                var responseData = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GenerateCover HTTP {Code}: {Body}", (int)response.StatusCode,
                        responseData?.Length > 500 ? responseData.Substring(0, 500) + "…" : responseData);
                    return Json(new { success = false, status = "error", message = $"Cover service returned {(int)response.StatusCode}." });
                }

                var urls = CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse(responseData);
                if (urls.Count == 0)
                    return Json(new { success = false, status = "error", message = "No image in API response." });

                var first = urls[0];
                string? imageBase64 = null;
                if (first.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = first.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                        imageBase64 = first[(idx + "base64,".Length)..];
                }

                HttpContext.Session.SetString("CoverDesignHasGenerated", "1");
                HttpContext.Session.SetString("CoverDesignLastBookId", req.BookId.ToString());

                // Persist preview to disk when possible so Settings / Book rows are not forced to hold huge base64.
                string? coverUrlForClient = first;
                try
                {
                    var persisted = await TryPersistCoverReferenceAsync(sessionUserId.Value, req.BookId, first, cancellationToken);
                    if (!string.IsNullOrEmpty(persisted))
                    {
                        if (persisted.Length <= Models.Settings.DbCompatMaxValueLength)
                        {
                            await UpsertDashboardSettingAsync($"book:{req.BookId}:aiCoverLastPreview", persisted, "Book", cancellationToken);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Cover preview URL/path length {Len} exceeds DbCompatMaxValueLength; skipping Settings save. Run: ALTER TABLE `Settings` MODIFY COLUMN `Value` LONGTEXT NULL;",
                                persisted.Length);
                        }

                        if (persisted.StartsWith("/", StringComparison.Ordinal) || persisted.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            coverUrlForClient = persisted;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not persist AI cover preview for book {BookId}", req.BookId);
                }

                return Json(new
                {
                    success = true,
                    status = "success",
                    coverUrl = coverUrlForClient,
                    image_base64 = imageBase64,
                    imageDataUrl = first.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? first : (string?)null,
                    options = urls.ToArray()
                });
            }
            catch (OperationCanceledException)
            {
                return Json(new { success = false, status = "error", message = "Request timed out." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateCover failed for book {BookId}", req.BookId);
                return Json(new { success = false, status = "error", message = ex.Message });
            }
        }

        /// <summary>Same body as <see cref="GenerateCover"/> — alias for clients that call a dedicated regenerate action.</summary>
        [HttpPost]
        [Route("RegenerateCover")]
        public Task<IActionResult> RegenerateCover([FromBody] DashboardGenerateCoverRequest req, CancellationToken cancellationToken)
            => GenerateCover(req, cancellationToken);

        /// <summary>Edit / refine cover via external POST /api/edit-cover (base64 image + direction).</summary>
        [HttpPost]
        [Route("EditCover")]
        public async Task<IActionResult> EditCover([FromBody] DashboardEditCoverRequest req, CancellationToken cancellationToken)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.EncodedImage))
                return Json(new { success = false, status = "error", message = "encoded_image is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, status = "error", message = "Please sign in." });

            var size = !string.IsNullOrWhiteSpace(req.Size) ? req.Size!.Trim() : (_configuration["ExternalApi:CoverGenerateSize"] ?? "1024x1536").Trim();
            var apiUrl = (_configuration["ExternalApi:EditCoverUrl"] ?? "http://162.229.248.26:8001/api/edit-cover").Trim();
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();

            var encoded = req.EncodedImage.Trim();
            if (encoded.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                var c = encoded.IndexOf(',');
                if (c >= 0) encoded = encoded[(c + 1)..];
            }

            var payload = new JObject
            {
                ["encoded_image"] = encoded,
                ["image_direction"] = (req.ImageDirection ?? "").Trim(),
                ["size"] = size
            };

            try
            {
                var client = _httpClientFactory.CreateClient();
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                httpRequest.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(apiKey))
                    httpRequest.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromMinutes(3));
                var response = await client.SendAsync(httpRequest, cts.Token);
                var responseData = await response.Content.ReadAsStringAsync(cts.Token);
                if (!response.IsSuccessStatusCode)
                    return Json(new { success = false, status = "error", message = $"Edit service returned {(int)response.StatusCode}." });

                var urls = CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse(responseData);
                if (urls.Count == 0)
                {
                    try
                    {
                        var jo = JObject.Parse(responseData ?? "{}");
                        var b64 = jo["encoded_image"]?.ToString() ?? jo["image"]?.ToString();
                        if (!string.IsNullOrEmpty(b64))
                        {
                            var dataUrl = b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? b64 : "data:image/png;base64," + b64;
                            urls.Add(dataUrl);
                        }
                    }
                    catch { /* ignore */ }
                }

                if (urls.Count == 0)
                    return Json(new { success = false, status = "error", message = "Edit response had no usable image." });

                var first = urls[0];
                string? imageBase64 = null;
                if (first.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = first.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                        imageBase64 = first[(idx + "base64,".Length)..];
                }

                var bookIdEdit = req.BookId > 0 ? req.BookId : 0;
                string? coverUrlForClient = first;
                if (bookIdEdit > 0)
                {
                    var ownsBook = await _context.Books.AsNoTracking()
                        .AnyAsync(b => b.BookId == bookIdEdit && b.UserId == sessionUserId.Value, cancellationToken);
                    if (ownsBook)
                    {
                        try
                        {
                            var persisted = await TryPersistCoverReferenceAsync(sessionUserId.Value, bookIdEdit, first, cancellationToken);
                            if (!string.IsNullOrEmpty(persisted))
                            {
                                if (persisted.Length <= Models.Settings.DbCompatMaxValueLength)
                                    await UpsertDashboardSettingAsync($"book:{bookIdEdit}:aiCoverLastPreview", persisted, "Book", cancellationToken);
                                if (persisted.StartsWith("/", StringComparison.Ordinal) || persisted.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                                    coverUrlForClient = persisted;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not persist edited cover for book {BookId}", bookIdEdit);
                        }
                    }
                }

                return Json(new
                {
                    success = true,
                    status = "success",
                    coverUrl = coverUrlForClient,
                    image_base64 = imageBase64,
                    imageDataUrl = first.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? first : (string?)null,
                    options = urls.ToArray()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EditCover failed");
                return Json(new { success = false, status = "error", message = ex.Message });
            }
        }

        /// <summary>Formatted chapter snippets for the Cover Design PDF preview modal (session-scoped book).</summary>
        [HttpGet]
        [Route("BookPdfPreviewData")]
        public async Task<IActionResult> BookPdfPreviewData(int bookId, bool full = false, CancellationToken cancellationToken = default)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, message = "Please sign in." });
            if (bookId <= 0)
                return Json(new { success = false, message = "Invalid book." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns)
                return Json(new { success = false, message = "Book not found." });

            var details = await _bookService.GetBookDetailsForPreviewAsync(sessionUserId.Value, bookId);
            if (details == null || !details.Success)
                return Json(new { success = false, message = details?.Message ?? "Could not load book." });

            var draftKey = $"book:{bookId}:formattingDraft";
            var draftRow = await _context.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == draftKey, cancellationToken);
            var exportOpt = BookPdfExportOptions.FromDraftJson(draftRow?.Value);
            var fmtRow = await _context.BookFormatting.AsNoTracking()
                .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == sessionUserId.Value, cancellationToken);
            exportOpt.MergeFromBookFormatting(fmtRow);

            var title = (details.BookTitle ?? "").Trim();
            var author = (details.AuthorName ?? "").Trim();
            var genre = (details.Genre ?? "").Trim();
            var phBase = BookManuscriptHtmlFormatter.CreateBaseContext(
                title, details.Subtitle, details.Description, genre, author);

            var chapters = details.Chapters.OrderBy(c => c.ChapterNumber).ToList();
            var list = new List<object>();
            const int previewMax = 900;
            for (var i = 0; i < chapters.Count; i++)
            {
                var ch = chapters[i];
                var displayNum = i + 1;
                var ph = phBase.WithChapter(ch.Title ?? "", displayNum, ch.ChapterNumber);
                var tRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
                var fullHtml = BookManuscriptHtmlFormatter.FormatBodyToHtml(
                    BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Content ?? "", ph));
                var html = fullHtml;
                if (html.Length > previewMax)
                    html = html.Substring(0, previewMax) + "…";
                var displayHeading = BookChapterHeadingFormatter.GetDisplayTitle(tRaw, displayNum);
                list.Add(new
                {
                    displayChapterNumber = displayNum,
                    chapterTitle = tRaw,
                    chapterDisplayHeading = displayHeading,
                    previewHtml = html,
                    chapterHtml = full ? fullHtml : null
                });
            }

            string? coverUrl = details.CoverImagePath;
            if (!string.IsNullOrWhiteSpace(coverUrl) && !coverUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !coverUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                coverUrl = coverUrl.StartsWith("/") ? coverUrl : "/" + coverUrl;

            return Json(new
            {
                success = true,
                bookId,
                bookTitle = details.BookTitle,
                authorName = details.AuthorName,
                genre = details.Genre,
                subtitle = details.Subtitle,
                coverImagePath = coverUrl,
                totalChapters = chapters.Count,
                chapters = list,
                formatting = new
                {
                    interiorStyle = exportOpt.InteriorStyle,
                    textSize = exportOpt.TextSize,
                    lineSpacing = exportOpt.LineSpacing,
                    format = exportOpt.Format,
                    publishingPlatform = exportOpt.PublishingPlatform,
                    publishingPlatforms = exportOpt.PublishingPlatforms
                }
            });
        }

        /// <summary>Full book PDF: cover (client data URL or server path) + formatted chapters.</summary>
        [HttpPost]
        [Route("DownloadBookPdf")]
        public async Task<IActionResult> DownloadBookPdf([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0)
                return Json(new { success = false, message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, message = "Please sign in." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns)
                return Json(new { success = false, message = "Book not found." });

            var details = await _bookService.GetBookDetailsForPreviewAsync(sessionUserId.Value, req.BookId);
            if (details == null || !details.Success)
                return Json(new { success = false, message = details?.Message ?? "Could not load book." });

            var orderedChapters = details.Chapters.OrderBy(c => c.ChapterNumber).ToList();
            if (orderedChapters.Count == 0)
                return Json(new { success = false, message = "Add at least one chapter in AI Writer before exporting the PDF." });
            if (!orderedChapters.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
                return Json(new { success = false, message = "Your chapters need body text. Add content in AI Writer, save, then download again." });

            try
            {
                var draftRow = await _context.Settings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == $"book:{req.BookId}:formattingDraft", cancellationToken);
                var exportOpt = BookPdfExportOptions.FromDraftJson(draftRow?.Value);
                var fmtRow = await _context.BookFormatting.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.BookId == req.BookId && f.UserId == sessionUserId.Value, cancellationToken);
                exportOpt.MergeFromBookFormatting(fmtRow);
                if (!string.IsNullOrWhiteSpace(req.InteriorStyle)) exportOpt.InteriorStyle = req.InteriorStyle!;
                if (!string.IsNullOrWhiteSpace(req.TextSize)) exportOpt.TextSize = req.TextSize!;
                if (!string.IsNullOrWhiteSpace(req.LineSpacing)) exportOpt.LineSpacing = req.LineSpacing!;
                if (!string.IsNullOrWhiteSpace(req.BookFormat)) exportOpt.Format = req.BookFormat!;
                if (!string.IsNullOrWhiteSpace(req.PublishingPlatform)) exportOpt.PublishingPlatform = req.PublishingPlatform!;

                var userRow = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value, cancellationToken);
                var publisherLabel = userRow?.FullName;
                if (string.IsNullOrWhiteSpace(publisherLabel)) publisherLabel = userRow?.UserEmail;

                var pdfBytes = await _bookPdfService.RenderFullBookPdfAsync(
                    details,
                    req.CoverImageDataUrl,
                    (req.DisplayTitle ?? "").Trim(),
                    (req.DisplayAuthor ?? "").Trim(),
                    (req.DisplayGenre ?? "").Trim(),
                    exportOpt,
                    publisherLabel,
                    cancellationToken);

                var rawName = (req.DisplayTitle ?? details.BookTitle ?? "book").Trim();
                if (string.IsNullOrEmpty(rawName)) rawName = "book";
                var safe = Regex.Replace(rawName, @"[^\w\-\s]", "");
                safe = Regex.Replace(safe, @"\s+", "-").Trim('-');
                if (string.IsNullOrEmpty(safe)) safe = "book";
                var fileName = $"{safe}-{req.BookId}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DownloadBookPdf failed for book {BookId}", req.BookId);
                return Json(new { success = false, message = "PDF generation failed. If this persists, verify Chromium (Puppeteer) can run on this machine." });
            }
        }

        /// <summary>Writes <see cref="Settings"/> rows. Values are clamped to <see cref="Settings.DbCompatMaxValueLength"/> until MySQL column is LONGTEXT.</summary>
        private async Task UpsertDashboardSettingAsync(string key, string value, string category, CancellationToken cancellationToken = default)
        {
            value ??= "";
            if (key.Contains("aiCoverLastPreview", StringComparison.OrdinalIgnoreCase) &&
                value.Length > Models.Settings.DbCompatMaxValueLength)
            {
                _logger.LogWarning(
                    "Skipping Settings save for {Key} (length {Len} > {Max}). Run: ALTER TABLE `Settings` MODIFY COLUMN `Value` LONGTEXT NULL;",
                    key, value.Length, Models.Settings.DbCompatMaxValueLength);
                return;
            }

            if (key.Contains("aiCoverPrompt", StringComparison.OrdinalIgnoreCase))
                value = Models.Settings.ClampValueLength(value, Models.Settings.DbCompatMaxValueLength) ?? "";
            else
                value = Models.Settings.ClampValueLength(value, Models.Settings.MaxShortValueLength) ?? "";

            var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            if (setting == null)
            {
                var nextId = await _context.NextSettingIdAsync(cancellationToken);
                _context.Settings.Add(new Models.Settings
                {
                    SettingId = nextId,
                    Key = key,
                    Value = value,
                    Category = category,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                setting.Value = value;
                setting.Category = category;
                setting.UpdatedAt = DateTime.UtcNow;
                _context.Settings.Update(setting);
            }
            await _context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>Data URLs are written under wwwroot/uploads; http(s) URLs are returned as-is for storage.</summary>
        private async Task<string?> TryPersistCoverReferenceAsync(int userId, int bookId, string imageRef, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imageRef)) return null;
            var t = imageRef.Trim();
            if (t.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                return await SaveDataUrlCoverToUploadsAsync(userId, bookId, t, cancellationToken);
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return t;
            if (t.StartsWith("/", StringComparison.Ordinal))
                return t;
            return null;
        }

        private static async Task<string> SaveDataUrlCoverToUploadsAsync(int userId, int bookId, string dataUrl, CancellationToken cancellationToken)
        {
            var comma = dataUrl.IndexOf(',', StringComparison.Ordinal);
            if (comma <= 0) throw new InvalidOperationException("Invalid data URL.");
            var header = dataUrl.Substring(0, comma);
            var b64 = dataUrl[(comma + 1)..].Trim();
            var ext = ".png";
            if (header.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || header.Contains("jpg", StringComparison.OrdinalIgnoreCase))
                ext = ".jpg";
            else if (header.Contains("webp", StringComparison.OrdinalIgnoreCase))
                ext = ".webp";

            var bytes = Convert.FromBase64String(b64);
            var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", userId.ToString(), "books", bookId.ToString());
            Directory.CreateDirectory(uploadsRoot);
            var fileName = $"cover_ai_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            var fullPath = Path.Combine(uploadsRoot, fileName);
            await System.IO.File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
            return $"/uploads/{userId}/books/{bookId}/{fileName}";
        }

        [Route("AudioBook")]
        public async Task<IActionResult> AudioBook()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            var roleId = user?.RoleId ?? 0;
            // RoleId 1 or 2: page stays open, no Upgrade Required popup
            ViewBag.ShowUpgradePrompt = (roleId != 1 && roleId != 2);
            return View();
        }

        /// <summary>Publishing hub: external platform guides + full-service option (demo gating by role/plan).</summary>
        [Route("/publish")]
        [Route("Publish")]
        public async Task<IActionResult> Publish(int? bookId = null)
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            var roleId = user?.RoleId ?? 0;
            ViewBag.PublishViaPlatformPaid = roleId == 1 || roleId == 2;
            ViewBag.PublishableKey = _configuration["Stripe:PublishableKey"] ?? "";
            ViewBag.SelectedBookId = bookId;
            ViewBag.PublishBookTitle = (string?)null;
            ViewBag.PublishBookDescription = (string?)null;
            ViewBag.PublishBookGenre = (string?)null;
            ViewBag.PublishBookCover = (string?)null;
            ViewBag.PublishBookStatus = (string?)null;
            ViewBag.PublishBookAlreadyListed = false;
            ViewBag.PublishBookChapterCount = 0;

            if (user != null && bookId.HasValue && bookId.Value > 0)
            {
                var pb = await _context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.BookId == bookId.Value && b.UserId == user.UserId);
                if (pb != null)
                {
                    ViewBag.PublishBookTitle = pb.Title;
                    ViewBag.PublishBookDescription = pb.Description;
                    ViewBag.PublishBookGenre = pb.Genre;
                    ViewBag.PublishBookCover = pb.CoverImagePath;
                    ViewBag.PublishBookStatus = pb.Status;
                    var ps = pb.Status ?? "";
                    ViewBag.PublishBookAlreadyListed = ps.Equals("Published", StringComparison.OrdinalIgnoreCase)
                        || ps.Equals("Paid", StringComparison.OrdinalIgnoreCase);

                    var bid = bookId.Value;
                    var uid = user.UserId;
                    var maxCh = 0;
                    if (await _context.Chapters.AsNoTracking().AnyAsync(c => c.BookId == bid))
                    {
                        maxCh = await _context.Chapters.AsNoTracking()
                            .Where(c => c.BookId == bid)
                            .MaxAsync(c => c.ChapterNumber > 0 ? c.ChapterNumber : (c.OrderIndex > 0 ? c.OrderIndex : c.SrNo));
                    }
                    var maxRaw = 0;
                    if (await _context.APIRawResponse.AsNoTracking().AnyAsync(r => r.UserId == uid && r.BookId == bid))
                    {
                        maxRaw = await _context.APIRawResponse.AsNoTracking()
                            .Where(r => r.UserId == uid && r.BookId == bid)
                            .MaxAsync(r => r.Chapter);
                    }
                    ViewBag.PublishBookChapterCount = Math.Max(maxCh, maxRaw);
                }
            }

            return View();
        }

        [Route("EditingFormatting")]
        public async Task<IActionResult> EditingFormatting()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var hasActivePlan = await _featureCartService.HasActivePlanAsync(userId);
                if (!hasActivePlan)
                {
                    return RedirectToAction("Index", "Features");
                }
            }
            return View();
        }

        [Route("Proofreading")]
        public IActionResult Proofreading()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("Publishing")]
        public IActionResult Publishing()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("Marketing")]
        public IActionResult Marketing()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("Royalties")]
        public IActionResult Royalties()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("OrderAuthorCopy")]
        public IActionResult OrderAuthorCopy()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("Copyright")]
        public IActionResult Copyright()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("Profile")]
        public async Task<IActionResult> Profile(PlanFeatures f)
        {
            // Get user information
            var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserEmail == userEmail);

            if (user == null)
            {
                ViewBag.UserName = User.Identity?.Name ?? "User";
                ViewBag.PremiumContentUnlocked = false;
                return View();
            }

            // Get author information
            var author = await _context.Authors
                .FirstOrDefaultAsync(a => a.AuthorCode == user.UserId.ToString());

            // Get user's active plan
            var activePlan = await _context.AuthorPlans
                .Include(ap => ap.Plan)
                .Where(ap => ap.AuthorId == user.UserId && ap.IsActive == 1 && ap.EndDate > DateTime.UtcNow)
                .OrderByDescending(ap => ap.EndDate)
                .FirstOrDefaultAsync();

            // Get plan features
            var planFeatures = new List<PlanFeatureViewModel>();
            if (activePlan != null)
            {
                // Get features for this plan
                var features = await _context.PlanFeatures
                    .Where(pf => pf.PlanId == activePlan.PlanId)
                    .ToListAsync();

                planFeatures = features.Select(f => new PlanFeatureViewModel
                {
                    Name = f.FeatureName ?? "",
                    IsIncluded = f.IsActive == 1
                }).ToList();
            }

            // Compute profile metrics from DB where possible
            var userBooks = await _context.Books.Where(b => b.UserId == user.UserId).ToListAsync();
            var totalBooks = userBooks.Count;
            var booksRead = userBooks.Count(b => b.Status == "Published");
            var booksReading = userBooks.Count(b => b.Status != "Published");

            // Create view model
            var viewModel = new DashboardProfileViewModel
            {
                UserName = user.FullName,
                UserEmail = user.UserEmail,
                UserRole = user.Role?.RoleName ?? "Reader",
                MemberSince = user.CreatedAt,
                Country = "United States",
                TotalBooks = totalBooks,
                BooksReading = booksReading,
                Reviews = 0,
                BooksRead = booksRead,
                ReadingHours = 0,
                PagesRead = 0,
                ReadingStreak = 0,
                PlanName = activePlan?.Plan?.PlanName ?? "Basic Plan",
                PlanPrice = activePlan?.Plan?.PlanRate ?? 0m,
                NextBillingDate = activePlan?.EndDate ?? DateTime.UtcNow.AddDays(30),
                BillingCycle = "Monthly",
                PaymentMethod = "**** 4242",
                PlanFeatures = planFeatures
            };

            // Optional username/account type (kept backward compatible without schema changes)
            var usernameSetting = await _context.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == $"profile:username:{user.UserId}");
            ViewBag.ProfileUsername = usernameSetting?.Value ?? "";
            ViewBag.AccountType = activePlan?.Plan?.PlanName ?? "Free";

            // Pass profile picture path to view
            ViewBag.ProfilePicturePath = user.ProfilePicturePath;
            ViewBag.PremiumContentUnlocked = user.RoleId == 1 || user.RoleId == 2;

            return View(viewModel);
        }

        // New actions for the dashboard overhaul
        [Route("FeaturesSelection")]
        public IActionResult FeaturesSelection()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("PlanSuggestion")]
        public IActionResult PlanSuggestion()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("UpdateProfile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                if (request == null)
                {
                    return Json(new { success = false, message = "Invalid request." });
                }

                if (!string.IsNullOrWhiteSpace(request.FullName))
                {
                    user.FullName = request.FullName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    var normalizedEmail = request.Email.Trim();
                    if (!Regex.IsMatch(normalizedEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                    {
                        return Json(new { success = false, message = "Please provide a valid email address." });
                    }

                    var emailInUse = await _context.Users.AnyAsync(u => u.UserId != user.UserId && u.UserEmail == normalizedEmail);
                    if (emailInUse)
                    {
                        return Json(new { success = false, message = "Email address is already in use." });
                    }
                    user.UserEmail = normalizedEmail;
                }

                // Store username in Settings table for backward compatibility
                if (!string.IsNullOrWhiteSpace(request.Username))
                {
                    var username = request.Username.Trim();
                    var existingUsername = await _context.Settings.FirstOrDefaultAsync(s => s.Key == $"profile:username:{user.UserId}");
                    if (existingUsername == null)
                    {
                        var nextId = await _context.NextSettingIdAsync(CancellationToken.None);
                        _context.Settings.Add(new Settings
                        {
                            SettingId = nextId,
                            Key = $"profile:username:{user.UserId}",
                            Value = username,
                            Category = "Profile",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        existingUsername.Value = username;
                        existingUsername.UpdatedAt = DateTime.UtcNow;
                    }
                }

                user.UpdatedAt = DateTime.UtcNow;

                // Keep authors row in sync when present (AuthorCode = UserId string)
                var authorRow = await _context.Authors.FirstOrDefaultAsync(a => a.AuthorCode == user.UserId.ToString());
                if (authorRow != null)
                {
                    authorRow.FullName = user.FullName ?? authorRow.FullName;
                    authorRow.AuthorEmail = user.UserEmail ?? authorRow.AuthorEmail;
                }

                await _context.SaveChangesAsync();

                // Refresh auth cookie + session so email/name match DB (lookup uses ClaimTypes.Email)
                HttpContext.Session.SetString("FullName", user.FullName ?? "");
                var auth = await HttpContext.AuthenticateAsync("UserCookie");
                if (auth.Succeeded)
                {
                    var roleName = await _context.Roles.AsNoTracking()
                        .Where(r => r.RoleId == user.RoleId)
                        .Select(r => r.RoleName)
                        .FirstOrDefaultAsync();
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                        new Claim(ClaimTypes.Name, user.FullName ?? ""),
                        new Claim(ClaimTypes.Email, user.UserEmail),
                        new Claim(ClaimTypes.Role, roleName ?? "Reader")
                    };
                    var identity = new ClaimsIdentity(claims, "UserCookie");
                    await HttpContext.SignInAsync("UserCookie", new ClaimsPrincipal(identity), auth.Properties);
                }

                return Json(new
                {
                    success = true,
                    fullName = user.FullName,
                    email = user.UserEmail
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
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

            if (newPassword.Length < 6)
            {
                return BadRequest(new { message = "New password must be at least 6 characters." });
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



        [Route("PlanConfirmation")]
        public IActionResult PlanConfirmation()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("Summary")]
        public IActionResult Summary()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("Billing")]
        public IActionResult Billing()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("LivePreview")]
        public IActionResult LivePreview()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("AdvancedEditing")]
        public IActionResult AdvancedEditing()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("UploadProfilePicture")]
        public async Task<IActionResult> UploadProfilePicture(IFormFile profilePicture)
        {
            try
            {
                // Log for debugging
                System.Diagnostics.Debug.WriteLine("UploadProfilePicture called");
                
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                System.Diagnostics.Debug.WriteLine($"User email: {userEmail}");
                
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
                
                if (user == null)
                {
                    System.Diagnostics.Debug.WriteLine("User not found");
                    return Json(new { success = false, message = "User not found" });
                }

                if (profilePicture == null || profilePicture.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("No file received");
                    return Json(new { success = false, message = "Please select an image file" });
                }

                System.Diagnostics.Debug.WriteLine($"File received: {profilePicture.FileName}, Size: {profilePicture.Length}");

                // Validate file type
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var fileExtension = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    return Json(new { success = false, message = "Only JPG, JPEG, and PNG files are allowed" });
                }

                // Validate file size (2 MB = 2 * 1024 * 1024 bytes)
                const long maxFileSize = 2 * 1024 * 1024; // 2 MB
                if (profilePicture.Length > maxFileSize)
                {
                    return Json(new { success = false, message = "File size must be less than 2 MB" });
                }

                // Delete old profile picture if exists
                if (!string.IsNullOrEmpty(user.ProfilePicturePath))
                {
                    var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.ProfilePicturePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                    }
                }

                // Generate unique filename
                var fileName = $"profile_{user.UserId}_{DateTime.UtcNow.Ticks}{fileExtension}";
                var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "UserImages", fileName);
                var relativePath = $"/UserImages/{fileName}";

                // Ensure directory exists
                var directory = Path.GetDirectoryName(uploadPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Save file
                using (var stream = new FileStream(uploadPath, FileMode.Create))
                {
                    await profilePicture.CopyToAsync(stream);
                }

                // Update user record
                user.ProfilePicturePath = relativePath;
                System.Diagnostics.Debug.WriteLine($"Updating user ProfilePicturePath to: {relativePath}");
                
                var changes = await _context.SaveChangesAsync();
                System.Diagnostics.Debug.WriteLine($"Database changes saved: {changes}");

                System.Diagnostics.Debug.WriteLine("Upload successful");
                return Json(new { success = true, message = "Profile picture uploaded successfully", imagePath = relativePath });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return Json(new { success = false, message = $"Error uploading image: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("RemoveProfilePicture")]
        public async Task<IActionResult> RemoveProfilePicture()
        {
            try
            {
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
                
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                // Delete file if exists
                if (!string.IsNullOrEmpty(user.ProfilePicturePath))
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", user.ProfilePicturePath.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                // Update user record
                user.ProfilePicturePath = null;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Profile picture removed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error removing image: {ex.Message}" });
            }
        }

        [HttpGet]
        [Route("GetProfilePicture")]
        public async Task<IActionResult> GetProfilePicture()
        {
            try
            {
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
                
                if (user == null || string.IsNullOrEmpty(user.ProfilePicturePath))
                {
                    return Json(new { success = false, imagePath = "" });
                }

                return Json(new { success = true, imagePath = user.ProfilePicturePath });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Returns recent activities for the currently logged-in user (books, chapters, AI generation).
        /// </summary>
        [HttpGet]
        [Route("GetRecentActivities")]
        public async Task<IActionResult> GetRecentActivities(int limit = 15)
        {
            try
            {
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
                if (user == null)
                    return Json(new List<object>());

                var activities = new List<(DateTime At, string Title, string Description, string Icon)>();

                // From Books: created/updated/finalized
                var books = await _context.Books
                    .Where(b => b.UserId == user.UserId)
                    .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                    .Take(50)
                    .Select(b => new { b.BookId, b.Title, b.CreatedAt, b.UpdatedAt, b.Status })
                    .ToListAsync();

                foreach (var b in books)
                {
                    var created = b.CreatedAt;
                    activities.Add((created, "Created book", "\"" + (b.Title ?? "Untitled") + "\"", "fas fa-book"));
                    var updated = b.UpdatedAt ?? created;
                    if (updated > created)
                    {
                        var action = (b.Status == "Published" || b.Status == "Final") ? "Finalized book" : "Updated book";
                        activities.Add((updated, action, "\"" + (b.Title ?? "Untitled") + "\"", "fas fa-check-circle"));
                    }
                }

                // From Chapters: edited (user's books only)
                var chapterUpdates = await _context.Chapters
                    .Where(c => _context.Books.Any(b => b.BookId == c.BookId && b.UserId == user.UserId))
                    .OrderByDescending(c => c.UpdatedAt)
                    .Take(30)
                    .Select(c => new { c.ChapterNumber, c.UpdatedAt, c.BookId })
                    .ToListAsync();

                var bookTitles = await _context.Books
                    .Where(b => b.UserId == user.UserId)
                    .Select(b => new { b.BookId, b.Title })
                    .ToDictionaryAsync(b => b.BookId, b => b.Title ?? "Untitled");

                foreach (var c in chapterUpdates)
                {
                    var title = bookTitles.GetValueOrDefault(c.BookId, "Untitled");
                    activities.Add((c.UpdatedAt, "Edited chapter", $"Chapter {c.ChapterNumber} of \"{title}\"", "fas fa-edit"));
                }

                // From APIRawResponse: AI-generated content
                var rawResponses = await _context.APIRawResponse
                    .Where(r => r.UserId == user.UserId)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(20)
                    .Select(r => new { r.BookId, r.Chapter, r.CreatedAt })
                    .ToListAsync();

                foreach (var r in rawResponses)
                {
                    var bookTitle = r.BookId.HasValue && bookTitles.ContainsKey(r.BookId.Value) ? bookTitles[r.BookId.Value] : "a book";
                    activities.Add((r.CreatedAt, "AI-generated content", $"Chapter {r.Chapter} for \"{bookTitle}\"", "fas fa-magic"));
                }

                // Sort by date desc and take limit
                var sorted = activities
                    .OrderByDescending(x => x.At)
                    .Take(limit)
                    .Select(a => new
                    {
                        createdAt = a.At,
                        title = a.Title,
                        description = a.Description,
                        timeAgo = TimeAgo(a.At),
                        iconClass = a.Icon
                    })
                    .ToList();

                return Json(sorted);
            }
            catch (Exception ex)
            {
                return Json(new List<object>());
            }
        }

        private static string TimeAgo(DateTime dateTime)
        {
            var span = DateTime.UtcNow - dateTime;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays} days ago";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)} weeks ago";
            return dateTime.ToString("MMM d, yyyy");
        }

        // ===================== BOOK CREATION SCREEN =====================
        [Route("BookCreation")]
        public async Task<IActionResult> BookCreation(int? bookId = null)
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            ViewBag.UserId = user.UserId;
            ViewBag.SelectedBookId = bookId;
            
            // Load user's books for dropdown
            var userBooks = await _context.Books
                .Where(b => b.UserId == user.UserId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.BookId, b.Title, b.CreatedAt })
                .ToListAsync();
            
            ViewBag.UserBooks = userBooks;
            
            // If bookId provided, load book details
            if (bookId.HasValue)
            {
                var book = await _context.Books
                    .Include(b => b.Chapters)
                    .FirstOrDefaultAsync(b => b.BookId == bookId.Value && b.UserId == user.UserId);
                
                if (book != null)
                {
                    ViewBag.Book = book;
                    ViewBag.Chapters = book.Chapters.OrderBy(c => c.ChapterNumber).ToList();
                }
            }
            
            return View();
        }

        // ===================== STYLING SCREEN =====================
        [Route("Styling")]
        public async Task<IActionResult> Styling(int? bookId = null)
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            ViewBag.UserId = user.UserId;
            
            // Check if user has styling feature (premium) - Database connected
            var userIdString = user.UserId.ToString();
            //var hasStylingFeature = await _context.userfeatures
            //    .Include(uf => uf.Feature)
            //    .Where(uf => uf.UserId == userIdString)
            //    .AnyAsync(uf => uf.Feature != null &&
            //                    (uf.Feature.Name.ToLower().Contains("style") ||
            //                     uf.Feature.Key.ToLower().Contains("style")));

            //ViewBag.HasStylingFeature = hasStylingFeature;

            // Load user's books
            var userBooks = await _context.Books
                .Where(b => b.UserId == user.UserId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.BookId, b.Title })
                .ToListAsync();
            
            ViewBag.UserBooks = userBooks;
            ViewBag.SelectedBookId = bookId;
            
            // If bookId provided, load style preferences from Settings
            if (bookId.HasValue)
            {
                var styleSetting = await _context.Settings
                    .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:style");
                
                if (styleSetting != null)
                {
                    ViewBag.StylePreferences = styleSetting.Value;
                }
            }
            
            return View();
        }

        // ===================== PREVIEW SCREEN =====================
        [Route("Preview")]
        public async Task<IActionResult> Preview(int? bookId = null)
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            ViewBag.UserId = user.UserId;
            if (bookId>0)
            {
                ViewBag.BookId = bookId;
            }
                // Load user's books
                var userBooks = await _context.Books
                .Where(b => b.UserId == user.UserId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.BookId, b.Title })
                .ToListAsync();
            
            ViewBag.UserBooks = userBooks;
            ViewBag.SelectedBookId = bookId;
            
            // If bookId provided, load book with chapters
            if (bookId.HasValue)
            {
                var book = await _context.Books
                    .Include(b => b.Chapters)
                    .FirstOrDefaultAsync(b => b.BookId == bookId.Value && b.UserId == user.UserId);
                
                if (book != null)
                {
                    ViewBag.Book = book;
                    ViewBag.Chapters = book.Chapters.OrderBy(c => c.ChapterNumber).ToList();
                }
            }
            
            return View();
        }

        // ===================== GENERATE FORMAT SCREEN =====================
        [Route("GenerateFormat")]
        public async Task<IActionResult> GenerateFormat(int? bookId = null)
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            ViewBag.UserId = user.UserId;
            
            // Load user's books
            var userBooks = await _context.Books
                .Where(b => b.UserId == user.UserId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.BookId, b.Title, b.Status })
                .ToListAsync();
            
            ViewBag.UserBooks = userBooks;
            ViewBag.SelectedBookId = bookId;
            
            // If bookId provided, check for generated formats in Settings
            if (bookId.HasValue)
            {
                var epubSetting = await _context.Settings
                    .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:output:epub");
                var pdfSetting = await _context.Settings
                    .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:output:pdf");
                
                ViewBag.EpubPath = epubSetting?.Value;
                ViewBag.PdfPath = pdfSetting?.Value;
            }
            
            return View();
        }

        // ===================== MY BOOKS SCREEN =====================
        [Route("MyBooks")]
        public async Task<IActionResult> MyBooks()
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Load all user's books; assign rotating theme covers when missing so nothing stays blank
            var bookEntities = await _context.Books
                .Where(b => b.UserId == user.UserId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var fallbackCovers = new[]
            {
                "/images/books/the-bird.png",
                "/images/books/good-things-are-up-ahead.png",
                "/images/books/fairy-tale.png",
                "/images/books/the-wizarding-chronicles.png",
                "/images/books/the-cambers-of-secrets.png"
            };
            var coverDirty = false;
            foreach (var b in bookEntities)
            {
                if (string.IsNullOrWhiteSpace(b.CoverImagePath))
                {
                    b.CoverImagePath = fallbackCovers[(b.BookId % fallbackCovers.Length + fallbackCovers.Length) % fallbackCovers.Length];
                    b.UpdatedAt = DateTime.UtcNow;
                    coverDirty = true;
                }
            }
            if (coverDirty) await _context.SaveChangesAsync();

            var userBooks = bookEntities.Select(b => new { b.BookId, b.Title, b.Status, b.CreatedAt, b.UpdatedAt, b.CoverImagePath, b.Description, b.Genre, b.WordCount }).ToList();

            // Get chapter counts from apirawresponse (generated chapters per UserId + BookId)
            var bookIds = userBooks.Select(b => b.BookId).ToList();
            var rawResponseCounts = await _context.APIRawResponse
                .Where(r => r.UserId == user.UserId && r.BookId != null && bookIds.Contains(r.BookId.Value))
                .GroupBy(r => r.BookId)
                .Select(g => new { BookId = g.Key, Count = g.Count() })
                .ToListAsync();
            var chaptersByBookId = rawResponseCounts.ToDictionary(x => x.BookId!.Value, x => x.Count);

            var books = userBooks.Select(b =>
            {
                dynamic item = new ExpandoObject();
                item.BookId = b.BookId;
                item.Title = b.Title;
                item.Status = b.Status;
                item.CreatedAt = b.CreatedAt;
                item.UpdatedAt = b.UpdatedAt;
                item.ChapterCount = chaptersByBookId.GetValueOrDefault(b.BookId, 0);
                item.CoverImagePath = b.CoverImagePath;
                item.Description = b.Description;
                item.Genre = b.Genre;
                item.WordCount = b.WordCount;
                return (dynamic)item;
            }).ToList();

            ViewBag.Books = books;
            ViewBag.UserId = user.UserId;
            ViewBag.AuthorName = user.FullName ?? user.UserEmail ?? "Author";
            
            return View();
        }

        // ===================== SUBSCRIPTIONS SCREEN =====================
        [Route("Subscriptions")]
        public async Task<IActionResult> Subscriptions()
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Load user's active subscriptions from database
            var activeSubscriptions = await _context.AuthorPlans
                .Include(ap => ap.Plan)
                .Where(ap => ap.UserId == user.UserId && ap.IsActive == 1 && ap.EndDate > DateTime.UtcNow)
                .OrderByDescending(ap => ap.EndDate)
                .Select(ap => new
                {
                    ap.AuthorPlanId,
                    PlanName = ap.Plan != null ? ap.Plan.PlanName : ap.PlanName,
                    ap.PlanRate,
                    ap.StartDate,
                    ap.EndDate,
                    ap.MaxEBooks,
                    ap.PlanDescription
                })
                .ToListAsync();
            
            // Load all available plans from database
            var availablePlans = await _context.Plans
                .Where(p => p.PlanId > 0) // Active plans
                .OrderBy(p => p.PlanRate)
                .ToListAsync();
            
            // Load user's purchased features - Database connected
            var userIdString = user.UserId.ToString();
            var userFeatures = await _context.UserFeatures
                .Include(uf => uf.Feature)
                .Where(uf => uf.UserId == userIdString)
                .Select(uf => new
                {
                    uf.FeatureId,
                    FeatureName = uf.Feature != null ? uf.Feature.Name : "Unknown",
                    uf.AddedAt
                })
                .ToListAsync();
            
            ViewBag.ActiveSubscriptions = activeSubscriptions;
            ViewBag.AvailablePlans = availablePlans;
            ViewBag.UserFeatures = userFeatures;
            ViewBag.UserId = user.UserId;
            
            return View();
        }

        // ===================== SETTINGS SCREEN =====================
        [Route("Settings")]
        public async Task<IActionResult> Settings()
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Load user preferences from database
            var userPreferences = await _context.UserPreferences
                .Where(up => up.UserId == user.UserId)
                .ToListAsync();
            
            var notificationsEnabled = userPreferences
                .FirstOrDefault(up => up.Key == "notifications_enabled");
            
            ViewBag.NotificationsEnabled = notificationsEnabled?.Value == "true";
            ViewBag.User = user;
            
            return View();
        }

        // Save notification settings to database
        [HttpPost]
        [Route("SaveNotificationSettings")]
        public async Task<IActionResult> SaveNotificationSettings([FromBody] bool enabled)
        {
            try
            {
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
                
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found" });
                }

                var preference = await _context.UserPreferences
                    .FirstOrDefaultAsync(up => up.UserId == user.UserId && up.Key == "notifications_enabled");
                
                if (preference == null)
                {
                    _context.UserPreferences.Add(new UserPreference
                    {
                        UserId = user.UserId,
                        Key = "notifications_enabled",
                        Value = enabled.ToString().ToLower(),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    preference.Value = enabled.ToString().ToLower();
                    preference.UpdatedAt = DateTime.UtcNow;
                }
                
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        // ===================== SUPPORT SCREEN =====================
        [Route("Support")]
        public async Task<IActionResult> Support()
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            // Load FAQs from Settings table (if stored there) or use default
            var faqSettings = await _context.Settings
                .Where(s => s.Category == "FAQ" || s.Key.StartsWith("faq:"))
                .OrderBy(s => s.Key)
                .ToListAsync();
            
            ViewBag.FAQs = faqSettings;
            ViewBag.UserId = user.UserId;
            ViewBag.UserEmail = user.UserEmail;
            
            return View();
        }
    }
}

public class UpdateProfileRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}

public class UpdatePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class UpdateReadingStatsRequest
{
    public int HoursRead { get; set; }
    public int PagesRead { get; set; }
    public int DayStreak { get; set; }
}