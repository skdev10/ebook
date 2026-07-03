using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Interfaces;
using EBookDashboard.Models.Options;
using EBookDashboard.Services.BookApi;
using EBookDashboard.Interfaces;
using EBookDashboard.Infrastructure;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Crypto.Generators;
using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
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
        private readonly IBookApiClient _bookApiClient;
        private readonly IOptionsSnapshot<ExternalApiOptions> _externalApiOptions;
        private readonly IConfiguration _configuration;
        private readonly ILogger<DashboardController> _logger;
        private readonly IBookService _bookService;
        private readonly IBookPdfService _bookPdfService;
        private readonly IBookPageMetricsService _bookPageMetricsService;
        private readonly BookPublishReadinessService _publishReadiness;
        private readonly IKdpCoverDimensionService _kdpCoverDimensions;
        private readonly BookFlowStateService _bookFlow;
        private readonly IEditorDraftResetService _draftReset;
        private readonly ICurrentUserAccessor _currentUser;
        private readonly IPrintWrapPregenerationQueue _printWrapPregenerationQueue;
        private readonly IPrintWrapGenerationService _printWrapGenerationService;
        private readonly ICoverGenerationJobQueue _coverGenerationJobQueue;
        private readonly ICoverFrontGenerationService _coverFrontGenerationService;

        public DashboardController(
            IFeatureCartService featureCartService,
            ApplicationDbContext context,
            IDashboardService dashboardService,
            IHttpClientFactory httpClientFactory,
            IBookApiClient bookApiClient,
            IOptionsSnapshot<ExternalApiOptions> externalApiOptions,
            IConfiguration configuration,
            ILogger<DashboardController> logger,
            IBookService bookService,
            IBookPdfService bookPdfService,
            IBookPageMetricsService bookPageMetricsService,
            BookPublishReadinessService publishReadiness,
            IKdpCoverDimensionService kdpCoverDimensions,
            BookFlowStateService bookFlow,
            IEditorDraftResetService draftReset,
            ICurrentUserAccessor currentUser,
            IPrintWrapPregenerationQueue printWrapPregenerationQueue,
            IPrintWrapGenerationService printWrapGenerationService,
            ICoverGenerationJobQueue coverGenerationJobQueue,
            ICoverFrontGenerationService coverFrontGenerationService)
        {
            _featureCartService = featureCartService;
            _context = context;
            _dashboardService = dashboardService;
            _httpClientFactory = httpClientFactory;
            _bookApiClient = bookApiClient;
            _externalApiOptions = externalApiOptions;
            _configuration = configuration;
            _logger = logger;
            _bookService = bookService;
            _bookPdfService = bookPdfService;
            _bookPageMetricsService = bookPageMetricsService;
            _publishReadiness = publishReadiness;
            _kdpCoverDimensions = kdpCoverDimensions;
            _bookFlow = bookFlow;
            _draftReset = draftReset;
            _currentUser = currentUser;
            _printWrapPregenerationQueue = printWrapPregenerationQueue;
            _printWrapGenerationService = printWrapGenerationService;
            _coverGenerationJobQueue = coverGenerationJobQueue;
            _coverFrontGenerationService = coverFrontGenerationService;
        }

        [Route("")]
        [Route("Index")]
        public async Task<IActionResult> Index()
        {
            try
            {
                return await IndexCoreAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard Index failed for {User}", User.Identity?.Name ?? "unknown");
                var fallbackName = User.Identity?.Name ?? "User";
                return View(new DashboardIndexViewModel
                {
                    UserName = fallbackName,
                    UserEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "",
                    CurrentProjects = new List<ProjectViewModel>(),
                    RecentActivities = new List<ActivityViewModel>(),
                    DemoPublishedBooks = new List<DemoPublishedBookViewModel>(),
                    DemoDrafts = new List<DemoDraftViewModel>(),
                    DemoReaderFriends = new List<DemoReaderFriendViewModel>(),
                    CurrentWorkingBook = new BookViewModel()
                });
            }
        }

        /// <summary>Empty-dashboard / My Books entry: create a draft and open AI Writer (Step 1).</summary>
        [HttpGet]
        [Route("StartNewBook")]
        public async Task<IActionResult> StartNewBook(string? title, int create = 0, int writer = 0)
        {
            var userId = _currentUser.GetUserId();
            if (!userId.HasValue || userId.Value <= 0)
                return RedirectToAction("UserLogin", "Account");

            var user = await _currentUser.GetUserAsync();
            if (user == null)
                return RedirectToAction("UserLogin", "Account");

            // Dashboard "New Book" / Write Now — explicit create=1 (legacy writer=1). Reuses near-empty Untitled when possible.
            var explicitNewBook = create == 1 || writer == 1;

            // Default navigation: resume latest real draft — never auto-create on load.
            if (!explicitNewBook)
            {
                var inProgress = await FindLatestUserDraftAsync(user.UserId);
                if (inProgress != null)
                {
                    await SetActiveBookForUserAsync(user.UserId, inProgress.BookId);
                    HttpContext.Session.SetInt32("LastSelectedBookId", inProgress.BookId);
                    HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, inProgress.BookId);
                    var flow = await _bookFlow.GetResumeStepAsync(inProgress.BookId);
                    BookResumeUrlHelper.SyncFlowSessionFlags(HttpContext, flow.Step);
                    var resumeUrl = _bookFlow.BuildResumeUrl(inProgress.BookId, flow.Step, flow.Path);
                    if (!string.IsNullOrWhiteSpace(resumeUrl) && resumeUrl.StartsWith('/'))
                        return Redirect(resumeUrl);
                    return RedirectToAction("AIGenerateBook", "Books", new { bookId = inProgress.BookId });
                }

                return RedirectToAction(nameof(Index));
            }

            return await CreateUntitledBookAndOpenWriterAsync(user, title);
        }

        /// <summary>Opens AI Writer with an Untitled draft — reuses a near-empty placeholder when one exists.</summary>
        private async Task<IActionResult> CreateUntitledBookAndOpenWriterAsync(Users user, string? title)
        {
            var trimmedTitle = string.IsNullOrWhiteSpace(title) ? "Untitled Book" : title.Trim();
            try
            {
                var book = await CreateUntitledBookAsync(user, trimmedTitle);
                await SetActiveBookForUserAsync(user.UserId, book.BookId);
                try
                {
                    await _bookFlow.SaveStepAsync(book.BookId, BookFlowStateService.StepGenerate);
                }
                catch (Exception flowEx)
                {
                    _logger.LogWarning(flowEx, "StartNewBook: flow step save failed for book {BookId}; continuing to AI Writer.", book.BookId);
                }
                HttpContext.Session.SetInt32("LastSelectedBookId", book.BookId);
                HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, book.BookId);
                return RedirectToAction("AIGenerateBook", "Books", new { bookId = book.BookId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartNewBook failed for user {UserId}", user.UserId);
                var fallback = await BookFlowStateService.WhereNotPublished(_context.Books.AsNoTracking())
                    .Where(b => b.UserId == user.UserId && b.Title == trimmedTitle)
                    .OrderByDescending(b => b.CreatedAt)
                    .Select(b => b.BookId)
                    .FirstOrDefaultAsync();
                if (fallback > 0)
                {
                    await SetActiveBookForUserAsync(user.UserId, fallback);
                    HttpContext.Session.SetInt32("LastSelectedBookId", fallback);
                    HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, fallback);
                    return RedirectToAction("AIGenerateBook", "Books", new { bookId = fallback });
                }
                TempData["InfoMessage"] = "We couldn't create your book just now. Please try again.";
                return RedirectToAction(nameof(Index));
            }
        }

        private async Task<Books> CreateUntitledBookAsync(Users user, string title)
        {
            var (authorId, categoryId, languageId) = await EnsureAuthorAndDefaultsForUserAsync(user);
            var request = new CreateBookRequest
            {
                UserId = user.UserId,
                AuthorId = authorId,
                CategoryId = categoryId,
                LanguageId = languageId,
                Title = title,
                Description = "",
                Dedication = "",
                Ghostwriting = "",
                Epigraph = "",
                Genre = "General",
                WordCount = 0,
                CoverImagePath = "",
                ManuscriptPath = "",
                Subtitle = "",
                AuthorCode = user.UserId.ToString(),
                BookCode = Guid.NewGuid().ToString("N")[..12]
            };
            return await _bookService.CreateBookFromRequestAsync(request);
        }

        private async Task<Books?> FindLatestUserDraftAsync(int userId)
        {
            var meaningful = await BookDraftGuard.FindLatestMeaningfulDraftAsync(_context, userId);
            if (meaningful != null && !IsDemoSeedTitle(meaningful.Title))
                return meaningful;
            return null;
        }

        private async Task SetActiveBookForUserAsync(int userId, int bookId)
        {
            await _context.Books
                .Where(b => b.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.isActive, 0));
            await _context.Books
                .Where(b => b.UserId == userId && b.BookId == bookId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.isActive, 1));
        }

        private static bool IsDemoSeedTitle(string? title) =>
            !string.IsNullOrWhiteSpace(title) &&
            DemoSeedTitles.Contains(title.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>Dashboard book pick — marks flow entry and resumes at the saved step.</summary>
        [HttpGet]
        [Route("SelectBook")]
        public async Task<IActionResult> SelectBook(int bookId)
        {
            var userId = _currentUser.GetUserId();
            if (!userId.HasValue || userId.Value <= 0)
                return RedirectToAction("UserLogin", "Account");
            if (bookId <= 0)
            {
                TempData["InfoMessage"] = "Select a book from the Dashboard to continue.";
                return RedirectToAction(nameof(Index));
            }

            var ownsBook = await _context.Books.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId.Value);
            if (ownsBook == null)
            {
                TempData["InfoMessage"] = "That book was not found. Choose a project from the Dashboard.";
                return RedirectToAction(nameof(Index));
            }
            await SetActiveBookForUserAsync(userId.Value, bookId);
            HttpContext.Session.SetInt32("LastSelectedBookId", bookId);
            HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);

            var flow = await _bookFlow.GetResumeStepAsync(bookId);
            BookResumeUrlHelper.BootstrapOwnedBookSession(HttpContext, bookId, ownsBook.Status, flow.Step);

            string resumeUrl;
            var perBookKey = BookResumeUrlHelper.PerBookSettingsKey(bookId);
            var perBookUrl = await _context.Settings.AsNoTracking()
                .Where(s => s.Key == perBookKey)
                .Select(s => s.Value)
                .FirstOrDefaultAsync();
            // Bug #17: honour the last-work URL only when it is at least as far as the furthest step,
            // so backward navigation can't make resume land on an earlier step.
            if (BookResumeUrlHelper.IsSafeResumePath(perBookUrl)
                && BookResumeUrlHelper.TryParseBookIdFromWorkUrl(perBookUrl!) == bookId
                && BookFlowStateService.StepRank(BookFlowStateService.StepFromWorkUrl(perBookUrl)) >= BookFlowStateService.StepRank(flow.Step))
            {
                resumeUrl = perBookUrl!.Trim();
            }
            else
            {
                resumeUrl = _bookFlow.BuildResumeUrl(bookId, flow.Step, flow.Path);
            }

            if (!string.IsNullOrWhiteSpace(resumeUrl) && resumeUrl.StartsWith('/'))
                return Redirect(resumeUrl);
            return RedirectToAction("AIGenerateBook", "Books", new { bookId });
        }

        /// <summary>Lightweight active-book pick from My Books (no redirect) — syncs session and sidebar workflow.</summary>
        [HttpPost]
        [Route("SetActiveBook")]
        public async Task<IActionResult> SetActiveBook(int bookId)
        {
            var userId = _currentUser.GetUserId();
            if (!userId.HasValue || userId.Value <= 0)
                return Json(new { ok = false, message = "Please sign in again." });

            if (bookId <= 0)
                return Json(new { ok = false, message = "Invalid book." });

            var book = await _context.Books.AsNoTracking()
                .Where(b => b.BookId == bookId && b.UserId == userId.Value)
                .Select(b => new { b.BookId, b.Title, b.Status })
                .FirstOrDefaultAsync();
            if (book == null)
                return Json(new { ok = false, message = "Book not found." });

            await SetActiveBookForUserAsync(userId.Value, bookId);
            HttpContext.Session.SetInt32("LastSelectedBookId", bookId);
            HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);

            string? resumeUrl = null;
            var flow = await _bookFlow.GetResumeStepAsync(bookId);
            var flowStep = flow.Step;
            BookResumeUrlHelper.SyncFlowSessionFlags(HttpContext, flow.Step);

            var perBookKey = BookResumeUrlHelper.PerBookSettingsKey(bookId);
            var perBookUrl = await _context.Settings.AsNoTracking()
                .Where(s => s.Key == perBookKey)
                .Select(s => s.Value)
                .FirstOrDefaultAsync();
            // Bug #17: never resume earlier than the furthest step reached.
            if (BookResumeUrlHelper.IsSafeResumePath(perBookUrl)
                && BookResumeUrlHelper.TryParseBookIdFromWorkUrl(perBookUrl!) == bookId
                && BookFlowStateService.StepRank(BookFlowStateService.StepFromWorkUrl(perBookUrl)) >= BookFlowStateService.StepRank(flow.Step))
            {
                resumeUrl = perBookUrl!.Trim();
            }
            else
            {
                resumeUrl = _bookFlow.BuildResumeUrl(bookId, flow.Step, flow.Path);
            }

            return Json(new
            {
                ok = true,
                bookId = book.BookId,
                title = book.Title ?? "Untitled",
                flowStep,
                resumeUrl
            });
        }

        /// <summary>Every book needs a valid Authors row (AuthorId ≠ UserId). Creates author on first book for new users.</summary>
        private async Task<(int AuthorId, int CategoryId, int LanguageId)> EnsureAuthorAndDefaultsForUserAsync(Users user)
        {
            var author = await _context.Authors
                .FirstOrDefaultAsync(a => a.AuthorCode == user.UserId.ToString());
            if (author == null)
            {
                author = new Authors
                {
                    AuthorCode = user.UserId.ToString(),
                    FullName = string.IsNullOrWhiteSpace(user.FullName) ? "Author" : user.FullName,
                    AuthorEmail = user.UserEmail ?? "",
                    CategoryId = 1,
                    Country = "",
                    City = "",
                    Region = "",
                    PostalCode = "",
                    CountryCode = "",
                    Phone = "",
                    Address = "",
                    Status = "Active",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Authors.Add(author);
                await _context.SaveChangesAsync();
            }

            var categoryId = 1;
            var languageId = 1;
            try
            {
                var cat = await _context.Categories.AsNoTracking().FirstOrDefaultAsync();
                if (cat != null) categoryId = cat.CategoryId;
                var lang = await _context.Languages.AsNoTracking().FirstOrDefaultAsync();
                if (lang != null) languageId = lang.LanguageId;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "EnsureAuthorAndDefaults: category/language lookup skipped for user {UserId}", user.UserId);
            }

            return (author.AuthorId, categoryId, languageId);
        }

        private async Task<IActionResult> IndexCoreAsync()
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

            // Get user information (claims NameIdentifier is authoritative; session is synced)
            Users? user = null;
            try
            {
                user = await _currentUser.GetUserAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard: could not load user for current principal.");
            }

            if (user == null)
            {
                ViewBag.UserName = User.Identity?.Name ?? "User";
                ViewBag.UserEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
                ViewBag.AuthorName = User.FindFirst("AuthorName")?.Value ?? "";
                ViewBag.Genre = User.FindFirst("Genre")?.Value ?? "";
                return View(new DashboardIndexViewModel
                {
                    UserName = ViewBag.UserName as string ?? "User",
                    HasAnyBooks = false,
                    StartNewBookUrl = Url.Action(nameof(StartNewBook), "Dashboard") ?? "/Dashboard/StartNewBook"
                });
            }

            // Get author information
            int? userId = user.UserId;
            ViewBag.UserId = userId;
            var author = await _context.Authors
                .FirstOrDefaultAsync(a => a.AuthorCode == user.UserId.ToString());

            // Get user's books
            var books = new List<Books>();
            try
            {
                books = await _context.Books
                    .AsNoTracking()
                    .Where(b => b.UserId == user.UserId)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard: could not load books for user {UserId}.", user.UserId);
            }

            // Get chapter counts from apirawresponse (generated chapters per UserId + BookId)
            var bookIds = books.Select(b => b.BookId).ToList();
            var chaptersGeneratedByBookId = new Dictionary<int, int>();
            try
            {
                if (bookIds.Count > 0)
                {
                    var rawResponseCounts = await _context.APIRawResponse
                        .Where(r => r.UserId == user.UserId && r.BookId != null && bookIds.Contains(r.BookId.Value))
                        .GroupBy(r => r.BookId)
                        .Select(g => new { BookId = g.Key, Count = g.Count() })
                        .ToListAsync();
                    chaptersGeneratedByBookId = rawResponseCounts.ToDictionary(x => x.BookId!.Value, x => x.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard: APIRawResponse stats skipped for user {UserId}.", user.UserId);
            }

            // Calculate real statistics from database
            var totalBooksPublished = books.Count(b => b.Status == "Published");
            var totalBooksGenerated = books.Count(b => b.Status == "Finalized");

            // Load user reading stats (UserStats table) - optional on older DBs
            UserStats? userStats = null;
            try
            {
                userStats = await _context.UserStats
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.UserId == user.UserId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard: UserStats skipped for user {UserId}.", user.UserId);
            }

            // Calculate monthly revenue from BookPrice table (optional)
            var currentMonth = DateTime.UtcNow.Month;
            var currentYear = DateTime.UtcNow.Year;
            decimal monthlyRevenue = 0m;
            try
            {
                monthlyRevenue = await _context.BookPrice
                    .Where(bp => bp.CreatedAt.Month == currentMonth && bp.CreatedAt.Year == currentYear)
                    .Join(_context.Books.Where(b => b.UserId == user.UserId),
                        bp => bp.BookId,
                        b => b.BookId,
                        (bp, b) => bp)
                    .SumAsync(bp => (decimal?)bp.bookPrice) ?? 0m;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard: BookPrice revenue skipped for user {UserId}.", user.UserId);
            }
            
            // Calculate total downloads from database (if you have a Downloads table)
            // For now, using book count as a placeholder - replace with actual downloads table when available
            var totalDownloads = books.Count; // Replace with actual downloads count when Downloads table exists
            
            // Calculate average rating from database (if you have a Ratings table)
            // For now, return 0 if no ratings exist
            var averageRating = 0m; // Replace with actual rating calculation when Ratings table exists

            totalBooksPublished = books.Count(b => BookFlowStateService.IsPublishedStatus(b.Status));
            totalBooksGenerated = books.Count(b => b.Status == "Finalized");

            // Resolve AI-generated covers from Settings (exact preview URLs — no stock fallbacks)
            var aiCoverByBookId = new Dictionary<int, string>();
            try
            {
                if (bookIds.Count > 0)
                {
                    var coverKeys = bookIds.SelectMany(id => new[]
                    {
                        $"book:{id}:printReadyCoverFront",
                        $"book:{id}:aiCoverLastPreview"
                    }).ToList();
                    var coverRows = await _context.Settings.AsNoTracking()
                        .Where(s => coverKeys.Contains(s.Key))
                        .ToListAsync();
                    var frontCoverByBookId = new Dictionary<int, string>();
                    foreach (var row in coverRows)
                    {
                        var parts = row.Key.Split(':');
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var bid) || string.IsNullOrWhiteSpace(row.Value))
                            continue;
                        var val = row.Value.Trim();
                        if (row.Key.EndsWith(":printReadyCoverFront", StringComparison.Ordinal))
                            frontCoverByBookId[bid] = val;
                        else
                            aiCoverByBookId[bid] = val;
                    }
                    foreach (var kv in frontCoverByBookId)
                        aiCoverByBookId[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard: aiCoverLastPreview skipped for user {UserId}.", user.UserId);
            }

            string ResolveBookCover(Books b)
            {
                if (aiCoverByBookId.TryGetValue(b.BookId, out var ai) && !string.IsNullOrWhiteSpace(ai))
                    return ai;
                return (b.CoverImagePath ?? "").Trim();
            }

            string ResolveContinueEditingCover(Books b)
            {
                aiCoverByBookId.TryGetValue(b.BookId, out var ai);
                return BookCoverResolver.ResolveContinueEditingCoverUrl(b.CoverImagePath, ai);
            }

            // Dashboard display data — exclude seeded demo placeholders from user drafts.
            var userDraftBooks = books
                .Where(b => !BookFlowStateService.IsPublishedStatus(b.Status) && !IsDemoSeedTitle(b.Title))
                .ToList();
            var meaningfulDraftBooks = new List<Books>();
            foreach (var draft in userDraftBooks)
            {
                if (!await BookDraftGuard.HasGeneratedManuscriptAsync(_context, user.UserId, draft.BookId))
                    continue;
                meaningfulDraftBooks.Add(draft);
            }
            var publishedBooks = books
                .Where(b => BookFlowStateService.IsPublishedStatus(b.Status) && !IsDemoSeedTitle(b.Title))
                .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .ToList();
            var lastWorkedBook = await ResolveLastWorkedBookAsync(user.UserId, meaningfulDraftBooks);
            var pendingBooks = meaningfulDraftBooks;
            var allUserBookIds = books.Where(b => !IsDemoSeedTitle(b.Title)).Select(b => b.BookId).ToList();
            var flowMap = await LoadBookFlowMapAsync(allUserBookIds);
            var epubByBookId = await LoadEpubPathsByBookIdAsync(publishedBooks.Select(b => b.BookId).ToList());
            var demoPublished = EnrichPublishedWithResume(
                GetDemoPublishedBooks(publishedBooks, ResolveBookCover, epubByBookId),
                bookId => Url.Action(nameof(SelectBook), "Dashboard", new { bookId }) ?? $"/Dashboard/SelectBook?bookId={bookId}");
            // Hide phantom "Untitled Book — No chapters yet" ghosts (no real title AND no generated chapters).
            var visibleDraftBooks = userDraftBooks
                .Where(b => !IsGhostBook(b, chaptersGeneratedByBookId))
                .ToList();
            var demoDrafts = EnrichDraftsWithFlow(GetDemoDrafts(visibleDraftBooks, aiCoverByBookId), flowMap, _bookFlow);
            string? heroDisplayTitle = null;
            if (lastWorkedBook != null)
            {
                heroDisplayTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                    _context, user.UserId, lastWorkedBook.BookId, lastWorkedBook.Title);
            }
            var demoHero = lastWorkedBook != null
                ? (title: heroDisplayTitle ?? lastWorkedBook.Title ?? "Your Book",
                    description: "Continue where you left off. Edit chapters, format pages, and get ready to publish.",
                    coverUrl: ResolveBookCover(lastWorkedBook),
                    book: (Books?)lastWorkedBook)
                : BuildHeroFromBook(null, ResolveBookCover);
            var demoCurrentRead = BuildCurrentReadFromBook(lastWorkedBook, chaptersGeneratedByBookId, ResolveBookCover);
            var hasAnyBooks = books.Count > 0;
            var demoReaderFriends = hasAnyBooks ? GetDemoReaderFriends() : new List<DemoReaderFriendViewModel>();

            string? heroResumeUrl = null;
            if (lastWorkedBook != null && !BookFlowStateService.IsPublishedStatus(lastWorkedBook.Status))
                heroResumeUrl = Url.Action(nameof(SelectBook), "Dashboard", new { bookId = lastWorkedBook.BookId });

            var viewModel = new DashboardIndexViewModel
            {
                UserName = user.FullName,
                UserEmail = user.UserEmail,
                HasAnyBooks = hasAnyBooks,
                StartNewBookUrl = Url.Action(nameof(StartNewBook), "Dashboard") ?? "/Dashboard/StartNewBook",
                TotalBooksPublished = totalBooksPublished,
                MonthlyRevenue = monthlyRevenue,
                TotalDownloads = totalDownloads,
                AverageRating = averageRating,
                CurrentProjects = pendingBooks.Select(b =>
                {
                    var flow = flowMap.GetValueOrDefault(b.BookId, (Step: BookFlowStateService.StepGenerate, Path: "ebook"));
                    var stepLabel = BookFlowStateService.StepToLabel(flow.Step);
                    var progressPct = BookFlowStateService.StepToPercent(flow.Step);
                    var totalChapters = chaptersGeneratedByBookId.GetValueOrDefault(b.BookId, 0);
                    var progressText = $"{stepLabel} · {progressPct}%";
                    if (flow.Step == BookFlowStateService.StepGenerate && totalChapters > 0)
                        progressText = $"{stepLabel} · {totalChapters} chapter{(totalChapters == 1 ? "" : "s")}";
                    return new ProjectViewModel
                    {
                        BookId = b.BookId,
                        Title = b.Title,
                        Status = b.Status,
                        ProgressPercentage = progressPct,
                        ProgressText = progressText,
                        FlowStepLabel = stepLabel,
                        ResumeUrl = _bookFlow.BuildResumeUrl(b.BookId, flow.Step, flow.Path),
                        CoverImagePath = ResolveContinueEditingCover(b),
                        LastEditedAt = b.UpdatedAt ?? b.CreatedAt,
                        LastEditedText = FormatLastEditedText(b.UpdatedAt ?? b.CreatedAt)
                    };
                }).ToList(),
                RecentActivities = hasAnyBooks
                    ? new List<ActivityViewModel>
                    {
                        new ActivityViewModel { Title = "Chapter 5 of \"The Art of Digital Publishing\" was updated", Description = "Your latest changes have been saved successfully", TimeAgo = "2 hours ago", IconClass = "fas fa-book" },
                        new ActivityViewModel { Title = "New order for \"Modern Web Development\" received", Description = "Customer purchased 3 copies of your book", TimeAgo = "5 hours ago", IconClass = "fas fa-shopping-cart" },
                        new ActivityViewModel { Title = "New review for \"AI in Everyday Life\"", Description = "Received 5-star rating with positive feedback", TimeAgo = "1 day ago", IconClass = "fas fa-comment" },
                        new ActivityViewModel { Title = "New manuscript uploaded for \"Creative Writing Techniques\"", Description = "File processed and ready for editing", TimeAgo = "2 days ago", IconClass = "fas fa-file-alt" }
                    }
                    : new List<ActivityViewModel>(),
                CurrentWorkingBook = CreateCurrentWorkingBook(lastWorkedBook, chaptersGeneratedByBookId, ResolveBookCover),
                TotalBooksGenerated = totalBooksGenerated,
                BooksRead = hasAnyBooks ? totalBooksPublished : 0,
                HoursRead = hasAnyBooks ? (userStats?.HoursRead ?? 0) : 0,
                PagesRead = hasAnyBooks ? (userStats?.PagesRead ?? 0) : 0,
                DayStreak = hasAnyBooks ? (userStats?.DayStreak ?? 0) : 0,
                DemoHeroTitle = demoHero.title,
                DemoHeroDescription = demoHero.description,
                DemoHeroCoverUrl = demoHero.coverUrl,
                DemoHeroBookId = demoHero.book?.BookId,
                DemoHeroResumeUrl = heroResumeUrl,
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

        private static readonly string[] DemoSeedTitles =
        {
            "The Wizarding Chronicles",
            "The Bird",
            "SOUL",
            "Good Things Are Up Ahead",
            "Fairy Tale",
            "Conquest of Flames",
            "The Chambers of Secrets"
        };

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
            var demoTitles = new[] { "The Wizarding Chronicles", "The Bird", "SOUL", "Good Things Are Up Ahead", "Fairy Tale", "Conquest of Flames", "The Chambers of Secrets" };
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
                ["The Bird"] = "Draft",
                ["SOUL"] = "Draft",
                ["Good Things Are Up Ahead"] = "Draft",
                ["Fairy Tale"] = "Draft",
                ["Conquest of Flames"] = "Draft",
                ["The Chambers of Secrets"] = "Draft"
            };
            var coverUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["The Wizarding Chronicles"] = DemoCoverUrls[5],
                ["The Bird"] = DemoCoverUrls[0],
                ["SOUL"] = DemoCoverUrls[1],
                ["Good Things Are Up Ahead"] = DemoCoverUrls[2],
                ["Fairy Tale"] = DemoCoverUrls[3],
                ["Conquest of Flames"] = DemoCoverUrls[4],
                ["The Chambers of Secrets"] = DemoCoverUrls[6]
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

        private static List<DemoDraftViewModel> EnrichDraftsWithFlow(
            List<DemoDraftViewModel> drafts,
            Dictionary<int, (string Step, string Path)> flowMap,
            BookFlowStateService bookFlow)
        {
            foreach (var d in drafts)
            {
                var flow = flowMap.GetValueOrDefault(d.BookId, (Step: BookFlowStateService.StepGenerate, Path: "ebook"));
                d.FlowStepLabel = BookFlowStateService.StepToLabel(flow.Step);
                d.FlowStepPercent = BookFlowStateService.StepToPercent(flow.Step);
                d.Subtitle = $"Continue at {d.FlowStepLabel} ({d.FlowStepPercent}% complete)";
                d.ResumeUrl = bookFlow.BuildResumeUrl(d.BookId, flow.Step, flow.Path);
            }
            return drafts;
        }

        private static List<DemoPublishedBookViewModel> EnrichPublishedWithResume(
            List<DemoPublishedBookViewModel> published,
            Func<int, string> selectBookUrl)
        {
            foreach (var p in published)
                p.ResumeUrl = selectBookUrl(p.BookId);
            return published;
        }

        private async Task<Dictionary<int, (string Step, string Path)>> LoadBookFlowMapAsync(List<int> bookIds)
        {
            var map = new Dictionary<int, (string Step, string Path)>();
            if (bookIds.Count == 0) return map;
            var keys = bookIds.SelectMany(id => new[] { $"book:{id}:flowStep", $"book:{id}:flowMaxStep", $"book:{id}:flowPath" }).ToList();
            var rows = await _context.Settings.AsNoTracking()
                .Where(s => keys.Contains(s.Key))
                .ToListAsync();
            foreach (var id in bookIds)
            {
                var step = rows.FirstOrDefault(r => r.Key == $"book:{id}:flowStep")?.Value?.Trim() ?? "";
                var maxStep = rows.FirstOrDefault(r => r.Key == $"book:{id}:flowMaxStep")?.Value?.Trim() ?? "";
                var path = rows.FirstOrDefault(r => r.Key == $"book:{id}:flowPath")?.Value?.Trim() ?? "";
                if (string.IsNullOrEmpty(step)) step = BookFlowStateService.StepGenerate;
                if (string.IsNullOrEmpty(path)) path = "ebook";
                // Bug #17: resume at the furthest step reached, not the last (possibly backward) step.
                if (BookFlowStateService.StepRank(maxStep) > BookFlowStateService.StepRank(step))
                    step = maxStep;
                map[id] = (step, path);
            }
            return map;
        }

        private async Task<Dictionary<int, string>> LoadEpubPathsByBookIdAsync(IReadOnlyList<int> bookIds)
        {
            var map = new Dictionary<int, string>();
            if (bookIds.Count == 0) return map;
            try
            {
                var keys = bookIds.Select(id => $"book:{id}:epubFilePath").ToList();
                var rows = await _context.Settings.AsNoTracking()
                    .Where(s => keys.Contains(s.Key))
                    .ToListAsync();
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.Value)) continue;
                    var parts = row.Key.Split(':');
                    if (parts.Length < 2 || !int.TryParse(parts[1], out var bid)) continue;
                    map[bid] = row.Value.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "LoadEpubPathsByBookIdAsync skipped.");
            }
            return map;
        }

        private static List<DemoPublishedBookViewModel> GetDemoPublishedBooks(
            List<Books> books,
            Func<Books, string> resolveCover,
            IReadOnlyDictionary<int, string> epubByBookId)
        {
            var published = books
                .Where(b => BookFlowStateService.IsPublishedStatus(b.Status))
                .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .ToList();
            return published.Select(b => new DemoPublishedBookViewModel
            {
                Title = b.Title,
                Author = string.Empty,
                Subtitle = string.IsNullOrWhiteSpace(b.Subtitle) ? null : b.Subtitle,
                CoverUrl = BookCoverResolver.ResolveDisplayUrl(resolveCover(b)),
                EpubUrl = epubByBookId.TryGetValue(b.BookId, out var epub) ? epub : null,
                BookId = b.BookId,
                Status = string.IsNullOrWhiteSpace(b.Status) ? "Draft" : b.Status,
                LastEditedText = FormatLastEditedText(b.UpdatedAt ?? b.CreatedAt)
            }).ToList();
        }

        /// <summary>A book with no real title and no generated chapters — a phantom "Untitled Book" tile we must not display.</summary>
        private static bool IsGhostBook(Books b, IReadOnlyDictionary<int, int> chaptersByBookId)
        {
            var t = (b.Title ?? "").Trim();
            var hasRealTitle = t.Length > 0
                && !t.Equals("Untitled Book", StringComparison.OrdinalIgnoreCase)
                && !t.Equals("Untitled", StringComparison.OrdinalIgnoreCase);
            var hasChapters = chaptersByBookId.TryGetValue(b.BookId, out var n) && n > 0;
            return !hasRealTitle && !hasChapters;
        }

        private static List<DemoDraftViewModel> GetDemoDrafts(List<Books> books, IReadOnlyDictionary<int, string> aiCoverByBookId)
        {
            var drafts = books
                .Where(b => !BookFlowStateService.IsPublishedStatus(b.Status))
                .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .ToList();
            return drafts.Select(b => new DemoDraftViewModel
            {
                Title = b.Title,
                Subtitle = string.IsNullOrWhiteSpace(b.Subtitle) ? "Continue writing your manuscript" : b.Subtitle!,
                Volumes = "Draft",
                CoverUrl = BookCoverResolver.ResolveContinueEditingCoverUrl(
                    b.CoverImagePath,
                    aiCoverByBookId.TryGetValue(b.BookId, out var ai) ? ai : null),
                BookId = b.BookId,
                Status = string.IsNullOrWhiteSpace(b.Status) ? "Draft" : b.Status,
                LastEditedText = FormatLastEditedText(b.UpdatedAt ?? b.CreatedAt)
            }).ToList();
        }

        private async Task<Books?> ResolveLastWorkedBookAsync(int userId, List<Books> books)
        {
            if (books == null || books.Count == 0)
                return null;

            var active = books.FirstOrDefault(b => b.isActive == 1);
            if (active != null)
                return active;

            try
            {
                var lastBookKey = $"user:{userId}:lastBookId";
                var lastUrlKey = $"user:{userId}:lastBookWorkUrl";
                var rows = await _context.Settings.AsNoTracking()
                    .Where(s => s.Key == lastBookKey || s.Key == lastUrlKey)
                    .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");

                if (rows.TryGetValue(lastBookKey, out var idRaw) && int.TryParse(idRaw, out var savedId) && savedId > 0)
                {
                    var fromSaved = books.FirstOrDefault(b => b.BookId == savedId);
                    if (fromSaved != null) return fromSaved;
                }

                if (rows.TryGetValue(lastUrlKey, out var urlRaw) && !string.IsNullOrWhiteSpace(urlRaw))
                {
                    var fromUrl = TryParseBookIdFromWorkUrl(urlRaw);
                    if (fromUrl > 0)
                    {
                        var match = books.FirstOrDefault(b => b.BookId == fromUrl);
                        if (match != null) return match;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Dashboard: could not resolve last worked book from Settings for user {UserId}", userId);
            }

            return books
                .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .FirstOrDefault();
        }

        private static int TryParseBookIdFromWorkUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return 0;
            var qIndex = url.IndexOf('?');
            if (qIndex < 0) return 0;
            var query = url[(qIndex + 1)..];
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("bookId", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(Uri.UnescapeDataString(kv[1]), out var bid) && bid > 0)
                    return bid;
            }
            return 0;
        }

        private static (string title, string description, string coverUrl, Books? book) BuildHeroFromBook(Books? book, Func<Books, string> resolveCover)
        {
            if (book == null)
                return ("Welcome to your studio", "Create your first book in one click, then write with AI Writer — Format → Cover → Publish.", "", null);
            return (book.Title, "Continue where you left off. Edit chapters, format pages, and get ready to publish.", resolveCover(book), book);
        }

        private static (string title, string progressLabel, int percent, string coverUrl, int? bookId) BuildCurrentReadFromBook(
            Books? book,
            Dictionary<int, int> chaptersGeneratedByBookId,
            Func<Books, string> resolveCover)
        {
            if (book == null) return ("Start reading", "0 / 0 pages", 0, "", null);
            var totalChapters = chaptersGeneratedByBookId.GetValueOrDefault(book.BookId, 0);
            var percent = book.Status is "Published" or "Finalized"
                ? 100
                : totalChapters > 0
                    ? Math.Min(100, totalChapters * 12)
                    : 0;
            var progressLabel = totalChapters > 0
                ? $"{totalChapters} chapter{(totalChapters == 1 ? "" : "s")}"
                : "No chapters yet";
            return (book.Title, progressLabel, percent, resolveCover(book), book.BookId);
        }

        private static (string title, string description, string coverUrl, Books? book) GetDemoHeroBook(List<Books> books, Func<Books, string> resolveCover)
        {
            var book = books
                .OrderByDescending(b => b.isActive)
                .ThenByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .FirstOrDefault();
            if (book == null)
                return ("Your Library", "Create your first book and start writing with AI.", "", null);
            return (book.Title, "Continue where you left off. Edit chapters, format pages, and get ready to publish.", resolveCover(book), book);
        }

        private static (string title, string progressLabel, int percent, string coverUrl, int? bookId) GetDemoCurrentRead(List<Books> books, Func<Books, string> resolveCover)
        {
            var book = books
                .OrderByDescending(b => b.isActive)
                .ThenByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .FirstOrDefault();
            if (book == null) return ("Start reading", "0 / 0 pages", 0, "", null);
            return (book.Title, "In progress", 51, resolveCover(book), book.BookId);
        }

        private static string FormatLastEditedText(DateTime when)
        {
            var utc = when.Kind == DateTimeKind.Utc ? when : DateTime.SpecifyKind(when, DateTimeKind.Utc);
            var diff = DateTime.UtcNow - utc;
            if (diff.TotalMinutes < 1) return "Edited just now";
            if (diff.TotalHours < 1) return $"Edited {(int)Math.Max(1, diff.TotalMinutes)} min ago";
            if (diff.TotalDays < 1) return $"Edited {(int)Math.Max(1, diff.TotalHours)}h ago";
            if (diff.TotalDays < 7) return $"Edited {(int)Math.Max(1, diff.TotalDays)}d ago";
            if (diff.TotalDays < 30) return $"Edited {(int)Math.Max(7, diff.TotalDays)}d ago";
            var months = (int)Math.Max(1, Math.Floor(diff.TotalDays / 30d));
            return months == 1 ? "Edited 1 month ago" : $"Edited {months} months ago";
        }

        private static string NormalizePublishingPlatformForExport(string? platform) =>
            string.IsNullOrWhiteSpace(platform) ? "" : platform.Trim();

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

        private static BookViewModel CreateCurrentWorkingBook(Books? workingBook, Dictionary<int, int> chaptersGeneratedByBookId, Func<Books, string>? resolveCover = null)
        {
            if (workingBook == null)
                return new BookViewModel();
            var totalChapters = chaptersGeneratedByBookId?.GetValueOrDefault(workingBook.BookId, 0) ?? 0;
            var progressPct = workingBook.Status is "Published" or "Finalized"
                ? 100
                : totalChapters > 0
                    ? Math.Min(100, totalChapters * 12)
                    : 0;
            var progressText = workingBook.Status is "Published" or "Finalized"
                ? "Complete"
                : totalChapters > 0
                    ? $"{totalChapters} chapter{(totalChapters == 1 ? "" : "s")}"
                    : "No chapters yet";
            return new BookViewModel
            {
                BookId = workingBook.BookId,
                Title = workingBook.Title,
                BookIdText = workingBook.BookId.ToString(),
                ProgressPercentage = progressPct,
                ProgressText = progressText,
                CoverImagePath = resolveCover != null ? resolveCover(workingBook) : workingBook.CoverImagePath
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

        [HttpGet]
        [Route("TourStatus")]
        public async Task<IActionResult> TourStatus()
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            if (user == null)
                return Json(new { success = false, hasCompletedTour = true });
            // false = brand-new account (DB default 0) → auto-start tour once.
            // true or NULL = existing/legacy users → skip auto-start.
            return Json(new { success = true, hasCompletedTour = user.HasCompletedTour != false });
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("CompleteTour")]
        public async Task<IActionResult> CompleteTour()
        {
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            if (user == null)
                return Json(new { success = false });
            if (user.HasCompletedTour != true)
            {
                user.HasCompletedTour = true;
                user.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return Json(new { success = true, hasCompletedTour = true });
        }

        [Route("EBook")]
        public IActionResult EBook()
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            return View();
        }

        [Route("CoverDesign")]
        public async Task<IActionResult> CoverDesign(int? bookId = null, string? flow = null, string? coverType = null, bool skipPrintReadyAuto = false)
        {
            var requestedFlow = (flow ?? string.Empty).Trim();
            var requestedCoverType = (coverType ?? string.Empty).Trim();
            // flow=printready selects print-ready cover mode on this page (do not skip Cover Design).
            ViewBag.UserName = User.Identity?.Name ?? "User";
            ViewBag.BookId = bookId ?? 0;
            ViewBag.CoverFlow = requestedFlow;
            ViewBag.CoverType = requestedCoverType;
            ViewBag.SkipPrintReadyAuto = skipPrintReadyAuto;
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            var roleId = user?.RoleId ?? 0;
            // RoleId 1 or 2: page stays open, no Upgrade Required popup
            ViewBag.ShowUpgradePrompt = (roleId != 1 && roleId != 2);

            if (!bookId.HasValue || bookId.Value <= 0)
            {
                var sessionBookId = HttpContext.Session.GetInt32("LastSelectedBookId")
                    ?? HttpContext.Session.GetInt32(BookFlowStateService.SessionEntryBookIdKey);
                if (sessionBookId is > 0)
                    return RedirectToAction(nameof(CoverDesign), new { bookId = sessionBookId, flow = requestedFlow, coverType = requestedCoverType, skipPrintReadyAuto });
                TempData["InfoMessage"] = "Select a book from the Dashboard to continue cover design.";
                return RedirectToAction("Index");
            }
            Books? ownedBookRow = null;
            if (user != null)
            {
                ownedBookRow = await _context.Books.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BookId == bookId.Value && b.UserId == user.UserId);
                if (ownedBookRow == null)
                {
                    TempData["InfoMessage"] = "That book was not found. Choose a project from the Dashboard.";
                    return RedirectToAction("Index");
                }
                // Published books stay editable — author can revisit Cover Design and re-publish.
            }

            // Deep links (Publish / My Books → Cover Design) must not require Dashboard pick first.
            HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId.Value);
            HttpContext.Session.SetInt32("LastSelectedBookId", bookId.Value);

            var (savedFlowStep, savedFlowPath) = await _bookFlow.GetStepAsync(bookId.Value);
            var isReEditableBook = ownedBookRow != null
                && BookPublishReadinessService.IsPublishReadyBookStatus(ownedBookRow.Status);
            var formattingDone = HttpContext.Session.GetString("FormattingDone") == "1";
            var hasGeneratedBook = HttpContext.Session.GetString("HasGeneratedBook") == "1";
            if (isReEditableBook)
            {
                BookResumeUrlHelper.SyncFlowSessionFlags(HttpContext, BookFlowStateService.StepPublish);
                hasGeneratedBook = true;
                formattingDone = true;
            }
            else
            {
                BookResumeUrlHelper.SyncFlowSessionFlags(HttpContext, savedFlowStep);
                BookResumeUrlHelper.HydrateSessionFromFlowStep(HttpContext, savedFlowStep, ref hasGeneratedBook, ref formattingDone);
            }

            if (BookFlowStateService.SessionEntryMatches(HttpContext, bookId.Value)
                || BookFlowStateService.IsStepAtLeast(savedFlowStep, BookFlowStateService.StepFormat))
            {
                formattingDone = HttpContext.Session.GetString("FormattingDone") == "1";
                hasGeneratedBook = HttpContext.Session.GetString("HasGeneratedBook") == "1";
            }

            var savedRank = BookFlowStateService.StepRank(savedFlowStep);
            var formatRank = BookFlowStateService.StepRank(BookFlowStateService.StepFormat);
            var canOpenCover = isReEditableBook
                || BookFlowStateService.IsStepAtLeast(savedFlowStep, BookFlowStateService.StepCover)
                || (formattingDone && savedRank == formatRank);
            if (!canOpenCover)
            {
                TempData["InfoMessage"] = savedRank < formatRank
                    ? "Complete Book Formatting first, then continue from the Dashboard."
                    : "Continue your project from the Dashboard.";
                return Redirect(_bookFlow.BuildResumeUrl(bookId.Value, savedFlowStep, savedFlowPath));
            }

            if (!isReEditableBook && (!hasGeneratedBook || !formattingDone))
            {
                var lockBookQ = $"?bookId={bookId.Value}";
                ViewBag.LockMessage = !hasGeneratedBook ? "Select a book from the Dashboard first." : "Complete Book Formatting first, then AI Cover Design will unlock.";
                ViewBag.LockGoto = !hasGeneratedBook
                    ? "/Dashboard"
                    : $"/BookDesign/CoverDesignCalculatorFixing{lockBookQ}";
                ViewBag.LockButtonText = !hasGeneratedBook ? "Go to Dashboard" : "Go to Formatting";
            }

            if (user != null)
            {
                var bookTitle = await _context.Books.AsNoTracking()
                    .Where(b => b.BookId == bookId.Value && b.UserId == user.UserId)
                    .Select(b => b.Title)
                    .FirstOrDefaultAsync() ?? "";
                if (BookTitleResolver.IsPlaceholderTitle(bookTitle))
                {
                    var displayTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                        _context, user.UserId, bookId.Value, bookTitle);
                    await BookTitleResolver.SyncBookTitleAsync(_context, user.UserId, bookId.Value, displayTitle);
                }
            }

            if (!string.Equals(savedFlowStep, BookFlowStateService.StepCover, StringComparison.OrdinalIgnoreCase))
                await _bookFlow.SaveStepAsync(bookId.Value, BookFlowStateService.StepCover, savedFlowPath);
            HttpContext.Session.SetInt32("LastSelectedBookId", bookId.Value);
            HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId.Value);
            ViewBag.FlowBookId = bookId.Value;
            ViewBag.FlowStep = BookFlowStateService.StepCover;
            ViewBag.FlowPath = savedFlowPath;
            ViewBag.FlowBackUrl = savedFlowPath.Equals("print", StringComparison.OrdinalIgnoreCase)
                ? $"/BookDesign/CoverDesignCalculatorFixing?bookId={bookId.Value}&format=Paperback"
                : $"/BookDesign/CoverDesignCalculatorFixing?bookId={bookId.Value}&format=Ebook";

            return View();
        }

        /// <summary>
        /// AI cover generation for Cover Design. Default: background job + client poll (avoids nginx 504).
        /// Pass <c>wait=true</c> on the request body to block until the upstream API finishes.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("GenerateCover")]
        [Microsoft.AspNetCore.Http.Timeouts.RequestTimeout("CoverGeneration")]
        public async Task<IActionResult> GenerateCover([FromBody] DashboardGenerateCoverRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0)
                return Json(new { success = false, status = "error", message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, status = "error", message = "Please sign in." });

            var book = await _context.Books.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (book == null)
                return Json(new { success = false, status = "error", message = "Book not found." });

            if (!req.Wait)
            {
                _coverGenerationJobQueue.Start(sessionUserId.Value, req);
                return Json(new
                {
                    success = true,
                    status = "processing",
                    message = "Generating front cover — this may take a few minutes while the AI queue runs."
                });
            }

            try
            {
                var result = await _coverFrontGenerationService.GenerateAsync(sessionUserId.Value, req, CancellationToken.None);
                if (!result.Success)
                    return Json(new { success = false, status = "error", message = result.Message });

                HttpContext.Session.SetString("CoverDesignHasGenerated", "1");
                HttpContext.Session.SetString("CoverDesignLastBookId", req.BookId.ToString());

                return Json(new
                {
                    success = true,
                    status = "success",
                    coverUrl = result.CoverUrl,
                    image_base64 = result.ImageBase64,
                    imageDataUrl = result.ImageDataUrl,
                    options = result.Options
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

        /// <summary>Poll background front-cover generation started by POST /Dashboard/GenerateCover.</summary>
        [HttpGet]
        [IgnoreAntiforgeryToken]
        [Route("GenerateCoverStatus")]
        public async Task<IActionResult> GenerateCoverStatus(int bookId, CancellationToken cancellationToken)
        {
            if (bookId <= 0)
                return Json(new { success = false, status = "error", message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, status = "error", message = "Please sign in." });

            var snap = _coverGenerationJobQueue.GetStatus(sessionUserId.Value, bookId)
                ?? await _coverGenerationJobQueue.LoadStatusFromDbAsync(bookId, cancellationToken);
            if (snap == null)
                return Json(new { success = true, status = "idle", message = "No cover job in progress." });

            if (snap.Status == "complete")
            {
                HttpContext.Session.SetString("CoverDesignHasGenerated", "1");
                HttpContext.Session.SetString("CoverDesignLastBookId", bookId.ToString());
            }

            return Json(new
            {
                success = snap.Status != "error",
                status = snap.Status,
                message = snap.Message,
                coverUrl = snap.CoverUrl,
                image_base64 = snap.ImageBase64,
                imageDataUrl = snap.ImageDataUrl,
                options = snap.Options
            });
        }

        /// <summary>Same body as <see cref="GenerateCover"/> — alias for clients that call a dedicated regenerate action.</summary>
        [HttpPost]
        [Route("RegenerateCover")]
        public Task<IActionResult> RegenerateCover([FromBody] DashboardGenerateCoverRequest req, CancellationToken cancellationToken)
            => GenerateCover(req, cancellationToken);

        /// <summary>
        /// #8: persist the cover variation the user picked from the options grid as the ACTIVE front
        /// cover, so the Publish/export step uses their choice instead of the default first variation.
        /// </summary>
        [HttpPost]
        [Route("SetActiveCover")]
        public async Task<IActionResult> SetActiveCover([FromBody] DashboardSetActiveCoverRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0 || string.IsNullOrWhiteSpace(req.Url))
                return Json(new { success = false, message = "BookId and Url are required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, message = "Please sign in." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns)
                return Json(new { success = false, message = "Book not found." });

            var url = req.Url.Trim();
            // Only accept a server-side path/URL (the variations are persisted to /uploads/...).
            // A data: URL is a transient preview and is not stored as the active cover.
            if (!url.StartsWith("/", StringComparison.Ordinal) && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Selected cover is not a stored image yet." });

            try
            {
                await SaveFrontCoverPreviewAsync(sessionUserId.Value, req.BookId, url, cancellationToken);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SetActiveCover failed for book {BookId}", req.BookId);
                return Json(new { success = false, message = "Could not save selected cover." });
            }
        }

        /// <summary>Edit / refine cover via external POST /api/edit-cover (base64 image + direction).</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Microsoft.AspNetCore.Http.Timeouts.RequestTimeout("CoverGeneration")]
        [Route("EditCover")]
        public async Task<IActionResult> EditCover([FromBody] DashboardEditCoverRequest req, CancellationToken cancellationToken)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.EncodedImage))
                return Json(new { success = false, status = "error", message = "encoded_image is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, status = "error", message = "Please sign in." });

            var size = !string.IsNullOrWhiteSpace(req.Size) ? req.Size!.Trim() : (_configuration["ExternalApi:CoverGenerateSize"] ?? "1024x1536").Trim();
            size = BookApiInputValidation.NormalizeSize(size, "1024x1536");
            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.EditCoverUrl, "/api/edit-cover").Trim();
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, status = "error", message = ExternalApiKeyResolver.MissingKeyUserMessage });

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
                var client = _bookApiClient;
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                httpRequest.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

                using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
                var response = await client.SendAsync(httpRequest, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
                var responseData = await response.Content.ReadAsStringAsync(upstreamCts.Token);
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
                                await SaveFrontCoverPreviewAsync(sessionUserId.Value, bookIdEdit, persisted, cancellationToken);
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

            var exportOpt = await LoadExportOptionsAsync(sessionUserId.Value, bookId, cancellationToken);
            var metrics = _bookPageMetricsService.Estimate(details, exportOpt);

            var title = (details.BookTitle ?? "").Trim();
            var author = (details.AuthorName ?? "").Trim();
            var genre = (details.Genre ?? "").Trim();
            var phBase = BookManuscriptHtmlFormatter.CreateBaseContext(
                title, details.Subtitle, details.Description, genre, author);

            var chapters = BookChapterExportHelper.OrderForExport(details.Chapters);
            var list = new List<object>();
            const int previewMax = 900;
            var previewNarrative = 0;
            for (var i = 0; i < chapters.Count; i++)
            {
                var ch = chapters[i];
                if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                    previewNarrative++;
                var phNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 1 : previewNarrative;
                var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
                var tRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
                var fullHtml = BookManuscriptHtmlFormatter.FormatBodyToHtml(
                    BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Content ?? "", ph));
                var html = fullHtml;
                if (html.Length > previewMax)
                    html = html.Substring(0, previewMax) + "…";
                var displayHeading = BookChapterExportHelper.GetExportHeading(ch.Title, ch.ChapterNumber, phNum);
                list.Add(new
                {
                    displayChapterNumber = phNum,
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
                pageCount = metrics.PageCount,
                wordCount = metrics.WordCount,
                chapters = list,
                formatting = new
                {
                    interiorStyle = exportOpt.InteriorStyle,
                    textSize = exportOpt.TextSize,
                    lineSpacing = exportOpt.LineSpacing,
                    format = exportOpt.Format,
                    publishingPlatform = exportOpt.PublishingPlatform,
                    publishingPlatforms = exportOpt.PublishingPlatforms
                },
                pageMetrics = new
                {
                    pageCount = metrics.PageCount,
                    wordCount = metrics.WordCount,
                    chapterCount = metrics.ChapterCount,
                    imageCount = metrics.ImageCount,
                    basis = metrics.Basis
                }
            });
        }

        /// <summary>Full book PDF: cover (client data URL or server path) + formatted chapters.</summary>
        [HttpPost]
        [Route("DownloadBookPdf")]
        public async Task<IActionResult> DownloadBookPdf([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0)
                return BadRequest(new { success = false, message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Unauthorized(new { success = false, message = "Please sign in." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns)
                return NotFound(new { success = false, message = "Book not found." });

            var details = await _bookService.GetBookDetailsForPreviewAsync(sessionUserId.Value, req.BookId);
            if (details == null || !details.Success)
                return BadRequest(new { success = false, message = details?.Message ?? "Could not load book." });

            var orderedChapters = details.Chapters.OrderBy(c => c.ChapterNumber).ToList();
            if (orderedChapters.Count == 0)
                return BadRequest(new { success = false, message = "Add at least one chapter in AI Writer before exporting the PDF." });
            if (!orderedChapters.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
                return BadRequest(new { success = false, message = "Your chapters need body text. Add content in AI Writer, save, then download again." });

            try
            {
                var exportOpt = await LoadExportOptionsAsync(sessionUserId.Value, req.BookId, cancellationToken);
                exportOpt.ApplyRequestOverrides(req);
                if (!string.IsNullOrWhiteSpace(req.PublishingPlatform))
                    exportOpt.PublishingPlatform = NormalizePublishingPlatformForExport(req.PublishingPlatform);
                if (exportOpt.PublishingPlatform.Equals("Just Print Ready File", StringComparison.OrdinalIgnoreCase)
                    || exportOpt.Format.Equals("Paperback", StringComparison.OrdinalIgnoreCase))
                    exportOpt.IncludeCoverPage = false;

                var metrics = _bookPageMetricsService.Estimate(details, exportOpt);

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

                if (pdfBytes == null || pdfBytes.Length < 128)
                    return StatusCode(500, new { success = false, message = "PDF generation produced an empty file." });
                if (!IsValidPdfBytes(pdfBytes))
                    return StatusCode(500, new { success = false, message = "PDF generation produced invalid output. Verify Chromium/Puppeteer is available on the server." });

                var rawName = (req.DisplayTitle ?? details.BookTitle ?? "book").Trim();
                if (string.IsNullOrEmpty(rawName)) rawName = "book";
                var safe = Regex.Replace(rawName, @"[^\w\-\s]", "");
                safe = Regex.Replace(safe, @"\s+", "-").Trim('-');
                if (string.IsNullOrEmpty(safe)) safe = "book";
                var fileName = $"{safe}-{req.BookId}.pdf";
                Response.Headers["X-Book-Page-Count"] = metrics.PageCount.ToString();
                Response.Headers["X-Pdf-Interior"] = $"{exportOpt.InteriorStyle}|{exportOpt.TextSize}|{exportOpt.LineSpacing}";

                try
                {
                    await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyPageCount", metrics.PageCount.ToString(), "Book", cancellationToken);
                    await _bookService.MarkPublishedAsync(req.BookId, sessionUserId.Value, cancellationToken);
                    await _bookFlow.SaveStepAsync(req.BookId, BookFlowStateService.StepPublish, exportOpt.Format?.Equals("Paperback", StringComparison.OrdinalIgnoreCase) == true ? "print" : "ebook");
                }
                catch (Exception sideEffectEx)
                {
                    _logger.LogWarning(sideEffectEx, "Post-export bookkeeping failed for book {BookId}; PDF download still succeeded.", req.BookId);
                }

                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DownloadBookPdf failed for book {BookId}", req.BookId);
                return StatusCode(500, new { success = false, message = "PDF generation failed. If this persists, verify Chromium (Puppeteer) can run on this server." });
            }
        }

        /// <summary>Marks a book published after a successful export (used when download is client-side only).</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("MarkBookPublished")]
        public async Task<IActionResult> MarkBookPublished([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0)
                return BadRequest(new { success = false, message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Unauthorized(new { success = false, message = "Please sign in." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns)
                return NotFound(new { success = false, message = "Book not found." });

            var flowPath = req.BookFormat?.Equals("Paperback", StringComparison.OrdinalIgnoreCase) == true
                || (req.PublishingPlatform ?? "").Contains("print", StringComparison.OrdinalIgnoreCase)
                ? "print"
                : "ebook";
            await FinalizeBookAsPublishedAfterExportAsync(req.BookId, sessionUserId.Value, flowPath, cancellationToken);
            return Json(new { success = true });
        }

        private async Task FinalizeBookAsPublishedAfterExportAsync(int bookId, int userId, string flowPath, CancellationToken cancellationToken = default)
        {
            await _bookService.MarkPublishedAsync(bookId, userId, cancellationToken);
            await _bookFlow.SaveStepAsync(bookId, BookFlowStateService.StepPublish, flowPath, cancellationToken);
            try
            {
                var key = $"book:{bookId}:publishedAt";
                var existing = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
                var stamp = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                if (existing == null)
                    _context.Settings.Add(new Settings { Key = key, Value = stamp });
                else
                    existing.Value = stamp;
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not persist publishedAt for book {BookId}", bookId);
            }
        }

        [HttpPost]
        [Route("DownloadBookInteriorPdf")]
        public async Task<IActionResult> DownloadBookInteriorPdf([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0)
                return BadRequest(new { success = false, message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Unauthorized(new { success = false, message = "Please sign in." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns)
                return NotFound(new { success = false, message = "Book not found." });

            var details = await _bookService.GetBookDetailsForPreviewAsync(sessionUserId.Value, req.BookId);
            if (details == null || !details.Success)
                return BadRequest(new { success = false, message = details?.Message ?? "Could not load book." });

            var orderedChapters = details.Chapters.OrderBy(c => c.ChapterNumber).ToList();
            if (orderedChapters.Count == 0)
                return BadRequest(new { success = false, message = "Add at least one chapter in AI Writer before exporting the PDF." });
            if (!orderedChapters.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
                return BadRequest(new { success = false, message = "Your chapters need body text. Add content in AI Writer, save, then download again." });

            try
            {
                var exportOpt = await LoadExportOptionsAsync(sessionUserId.Value, req.BookId, cancellationToken);
                exportOpt.ApplyRequestOverrides(req);
                exportOpt.IncludeCoverPage = false;
                if (!string.IsNullOrWhiteSpace(req.PublishingPlatform))
                    exportOpt.PublishingPlatform = NormalizePublishingPlatformForExport(req.PublishingPlatform);
                else if (exportOpt.Format.Equals("Paperback", StringComparison.OrdinalIgnoreCase)
                         || exportOpt.Format.Equals("Both", StringComparison.OrdinalIgnoreCase))
                    exportOpt.PublishingPlatform = "Amazon KDP";

                var metrics = _bookPageMetricsService.Estimate(details, exportOpt);
                var userRow = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value, cancellationToken);
                var publisherLabel = userRow?.FullName;
                if (string.IsNullOrWhiteSpace(publisherLabel)) publisherLabel = userRow?.UserEmail;

                var pdfBytes = await _bookPdfService.RenderFullBookPdfAsync(
                    details,
                    null,
                    (req.DisplayTitle ?? details.BookTitle ?? "").Trim(),
                    (req.DisplayAuthor ?? details.AuthorName ?? "").Trim(),
                    (req.DisplayGenre ?? details.Genre ?? "").Trim(),
                    exportOpt,
                    publisherLabel,
                    cancellationToken);

                if (pdfBytes == null || pdfBytes.Length < 128)
                    return StatusCode(500, new { success = false, message = "Interior PDF generation produced an empty file." });
                if (!IsValidPdfBytes(pdfBytes))
                    return StatusCode(500, new { success = false, message = "Interior PDF generation produced invalid output. Verify Chromium/Puppeteer is available on the server." });

                var rawName = (req.DisplayTitle ?? details.BookTitle ?? "book-interior").Trim();
                if (string.IsNullOrEmpty(rawName)) rawName = "book-interior";
                var safe = Regex.Replace(rawName, @"[^\w\-\s]", "");
                safe = Regex.Replace(safe, @"\s+", "-").Trim('-');
                if (string.IsNullOrEmpty(safe)) safe = "book-interior";
                var fileName = $"{safe}-{req.BookId}-interior.pdf";
                await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyPageCount", metrics.PageCount.ToString(), "Book", cancellationToken);
                await FinalizeBookAsPublishedAfterExportAsync(req.BookId, sessionUserId.Value, "print", cancellationToken);
                Response.Headers["X-Book-Page-Count"] = metrics.PageCount.ToString();
                Response.Headers["X-Pdf-Interior"] = $"{exportOpt.InteriorStyle}|{exportOpt.TextSize}|{exportOpt.LineSpacing}";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "DownloadBookInteriorPdf failed for book {BookId}", req.BookId);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DownloadBookInteriorPdf failed for book {BookId}", req.BookId);
                return StatusCode(500, new { success = false, message = "Interior PDF generation failed. " + ex.Message });
            }
        }

        [HttpGet]
        [Route("GetPrintReadyCoverAssets")]
        public async Task<IActionResult> GetPrintReadyCoverAssets(int bookId, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetPrintReadyCoverAssetsCoreAsync(bookId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPrintReadyCoverAssets failed for book {BookId}", bookId);
                return Json(new { success = false, message = "Could not load cover assets. Please refresh and try again." });
            }
        }

        private async Task<IActionResult> GetPrintReadyCoverAssetsCoreAsync(int bookId, CancellationToken cancellationToken)
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

            var keys = new[]
            {
                $"book:{bookId}:printReadyCoverWrap",
                $"book:{bookId}:printReadyCoverWrapApi",
                $"book:{bookId}:printReadyCoverFront",
                $"book:{bookId}:printReadyCoverBack",
                $"book:{bookId}:printReadyCoverSpine",
                $"book:{bookId}:aiCoverLastPreview",
                $"book:{bookId}:printReadyPageCount",
                $"book:{bookId}:printReadyTrimSize",
                $"book:{bookId}:printReadySpineInches",
                $"book:{bookId}:printReadyCoverFrontSha256",
                $"book:{bookId}:printReadyCoverFrontAssetRef",
                $"book:{bookId}:printReadyCoverWrapStatus"
            };
            var rows = await _context.Settings.AsNoTracking()
                .Where(s => keys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);

            var pageCount = await ResolvePrintReadyPageCountAsync(bookId, cancellationToken);
            if (pageCount <= 0)
            {
                try
                {
                    var details = await _bookService.GetBookDetailsForPreviewAsync(sessionUserId.Value, bookId);
                    if (details != null && details.Success)
                    {
                        var exportOpt = await LoadExportOptionsAsync(sessionUserId.Value, bookId, cancellationToken);
                        pageCount = _bookPageMetricsService.Estimate(details, exportOpt).PageCount;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "GetPrintReadyCoverAssets: page metrics for book {BookId}", bookId);
                }
            }
            var hasKnownPageCount = pageCount > 0;
            if (hasKnownPageCount)
                pageCount = Math.Clamp(pageCount, 1, Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount);

            var trimSize = rows.GetValueOrDefault($"book:{bookId}:printReadyTrimSize", "").Trim();
            if (string.IsNullOrWhiteSpace(trimSize)) trimSize = "6 x 9 in";

            var kdp = hasKnownPageCount ? CalculatePrintReadyKdp(pageCount, trimSize) : null;
            var wrapStatusRaw = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrapStatus", "").Trim();
            var frontStatus = ResolveFrontCoverStatus(rows, bookId);
            var wrapMatchesFront = await WrapMatchesSavedFrontAsync(bookId, rows, cancellationToken);
            var wrapStatus = ResolveWrapCoverStatus(rows, bookId, frontStatus, wrapStatusRaw, wrapMatchesFront);

            return Json(new
            {
                success = true,
                bookId,
                pageCount,
                trimSize,
                frontCoverStatus = frontStatus,
                wrapCoverStatus = wrapStatus,
                wrapMatchesFront,
                kdp = kdp == null ? null : new
                {
                    spineInches = (double)kdp.SpineWidth,
                    spineMm = (double)(kdp.SpineWidth * 25.4m),
                    wrapWidthInches = (double)kdp.FullCoverWidth,
                    wrapHeightInches = (double)kdp.FullCoverHeight,
                    pixelWidth = kdp.PixelWidth,
                    pixelHeight = kdp.PixelHeight,
                    spinePixels = kdp.SpinePixels,
                    paperType = kdp.PaperType,
                    interiorType = kdp.InteriorType,
                    bindingType = kdp.BindingType,
                    outerMarginInches = (double)kdp.Bleed,
                    bleedInches = (double)kdp.Bleed,
                    safeAreaWidth = (double)kdp.SafeAreaWidth,
                    safeAreaHeight = (double)kdp.SafeAreaHeight,
                    spineMargin = (double)kdp.SpineMargin,
                    barcodeMargin = (double)kdp.BarcodeMargin,
                    panelWidthInches = (double)kdp.FrontCoverWidth,
                    panelHeightInches = (double)(kdp.SafeAreaHeight + kdp.MarginHeight),
                    spineWidthInchesPerPage = (double)kdp.SpineInchesPerPage,
                    dpi = kdp.Dpi
                },
                layout = kdp == null ? null : new
                {
                    backPanelXInches = (double)kdp.BackPanelXInches,
                    spineXInches = (double)kdp.SpineXInches,
                    frontPanelXInches = (double)kdp.FrontPanelXInches,
                    panelTopYInches = (double)kdp.PanelTopYInches
                },
                cover = await BuildCoverUrls(bookId, sessionUserId.Value, rows, cancellationToken)
            });
        }

        private static string ResolveFrontCoverStatus(Dictionary<string, string> rows, int bookId)
        {
            var savedFront = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverFront", "").Trim();
            var aiPreview = rows.GetValueOrDefault($"book:{bookId}:aiCoverLastPreview", "").Trim();
            return !string.IsNullOrWhiteSpace(savedFront) || !string.IsNullOrWhiteSpace(aiPreview)
                ? "Ready"
                : "Missing";
        }

        private static string ResolveWrapCoverStatus(
            Dictionary<string, string> rows,
            int bookId,
            string frontStatus,
            string wrapStatusRaw,
            bool wrapMatchesFront)
        {
            var wrap = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrap", "").Trim();
            if (string.IsNullOrWhiteSpace(wrap))
                wrap = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrapApi", "").Trim();
            if (!string.IsNullOrWhiteSpace(wrap) && wrapMatchesFront)
                return PrintWrapGenerationService.StatusReady;
            if (!string.IsNullOrWhiteSpace(wrap) && !wrapMatchesFront)
                return "Stale";
            if (!string.IsNullOrWhiteSpace(wrapStatusRaw))
                return wrapStatusRaw;
            return frontStatus == PrintWrapGenerationService.StatusReady ? PrintWrapGenerationService.StatusGenerating : "Missing";
        }

        /// <summary>True when saved wrap was built from the current <c>printReadyCoverFront</c> asset.</summary>
        private async Task<bool> WrapMatchesSavedFrontAsync(
            int bookId,
            Dictionary<string, string> rows,
            CancellationToken cancellationToken)
        {
            var wrap = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrap", "").Trim();
            if (string.IsNullOrWhiteSpace(wrap))
                wrap = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrapApi", "").Trim();
            if (string.IsNullOrWhiteSpace(wrap)) return false;

            var frontRef = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverFront", "").Trim();
            if (string.IsNullOrWhiteSpace(frontRef))
                frontRef = rows.GetValueOrDefault($"book:{bookId}:aiCoverLastPreview", "").Trim();
            if (string.IsNullOrWhiteSpace(frontRef)) return false;

            var storedSha = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverFrontSha256", "").Trim();
            var storedFrontRef = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverFrontAssetRef", "").Trim();
            if (string.IsNullOrEmpty(storedSha)) return false;

            if (!string.IsNullOrEmpty(storedFrontRef)
                && !string.Equals(storedFrontRef, frontRef, StringComparison.OrdinalIgnoreCase))
                return false;

            var frontBytes = await CoverImageRefLoader.TryReadAsBytesAsync(
                frontRef, webRootPath: null, _httpClientFactory, cancellationToken);
            if (frontBytes == null || frontBytes.Length == 0) return false;

            var hash = Convert.ToHexString(SHA256.HashData(frontBytes));
            if (!string.Equals(storedSha, hash, StringComparison.OrdinalIgnoreCase))
                return false;

            var wrapBytes = await CoverImageRefLoader.TryReadAsBytesAsync(
                wrap, webRootPath: null, _httpClientFactory, cancellationToken);
            if (wrapBytes == null || wrapBytes.Length == 0) return false;

            var pageCountStr = rows.GetValueOrDefault($"book:{bookId}:printReadyPageCount", "").Trim();
            var pageCount = int.TryParse(pageCountStr, out var parsedPages) && parsedPages > 0 ? parsedPages : 24;
            var trimSize = rows.GetValueOrDefault($"book:{bookId}:printReadyTrimSize", "").Trim();
            if (string.IsNullOrWhiteSpace(trimSize)) trimSize = "6 x 9 in";

            return CoverWrapPanelExtractor.FrontPanelMatchesSavedFront(wrapBytes, frontBytes, pageCount, trimSize);
        }

        private async Task<object> BuildCoverUrls(int bookId, int userId, Dictionary<string, string> rows, CancellationToken ct)
        {
            var wrap = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrap", "").Trim();
            if (string.IsNullOrEmpty(wrap))
                wrap = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrapApi", "").Trim();
            var frontStored = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverFront", "").Trim();
            var aiCover = rows.GetValueOrDefault($"book:{bookId}:aiCoverLastPreview", "").Trim();
            var back = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverBack", "").Trim();
            var spine = rows.GetValueOrDefault($"book:{bookId}:printReadyCoverSpine", "").Trim();

            var book = await _context.Books.AsNoTracking()
                .Where(b => b.BookId == bookId && b.UserId == userId)
                .Select(b => new { b.CoverImagePath })
                .FirstOrDefaultAsync(ct);
            var bookPath = (book?.CoverImagePath ?? "").Trim();

            var front = BookCoverRefResolver.NormalizeCoverUrlRef(
                BookCoverRefResolver.ResolveEbookFrontCoverRef(frontStored, aiCover, bookPath, wrap));

            return new { wrap, front, back, spine };
        }

        [HttpGet]
        [Route("DownloadPrintReadyCoverAsset")]
        public async Task<IActionResult> DownloadPrintReadyCoverAsset(int bookId, string? part = null, CancellationToken cancellationToken = default)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            if (bookId <= 0) return BadRequest("Invalid book.");

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns) return NotFound("Book not found.");

            var partNorm = (part ?? "wrap").Trim().ToLowerInvariant();
            var keySuffix = partNorm switch
            {
                "front" => "printReadyCoverFront",
                "back" => "printReadyCoverBack",
                "spine" => "printReadyCoverSpine",
                _ => "printReadyCoverWrap"
            };
            var settingKey = $"book:{bookId}:{keySuffix}";

            string refValue;
            // True when the front download is actually serving the full wrap (no standalone
            // front exists). In that case we must always crop the front panel out of the wrap.
            var frontSourceIsWrap = false;
            if (partNorm == "front")
            {
                var coverKeys = new[]
                {
                    $"book:{bookId}:printReadyCoverFront",
                    $"book:{bookId}:aiCoverLastPreview",
                    $"book:{bookId}:printReadyCoverWrap"
                };
                var rows = await _context.Settings.AsNoTracking()
                    .Where(s => coverKeys.Contains(s.Key))
                    .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);
                var bookRow = await _context.Books.AsNoTracking()
                    .Where(b => b.BookId == bookId && b.UserId == sessionUserId.Value)
                    .Select(b => new { b.CoverImagePath })
                    .FirstOrDefaultAsync(cancellationToken);
                var wrapForFront = (rows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrap") ?? "").Trim();
                refValue = BookCoverRefResolver.ResolveEbookFrontCoverRef(
                    rows.GetValueOrDefault($"book:{bookId}:printReadyCoverFront"),
                    rows.GetValueOrDefault($"book:{bookId}:aiCoverLastPreview"),
                    bookRow?.CoverImagePath,
                    wrapForFront);
                // Both/Paperback flows often store only the full wrap (no standalone front).
                // Fall back to the wrap here so we can crop the front panel from it below.
                if (string.IsNullOrWhiteSpace(refValue))
                {
                    refValue = wrapForFront;
                    frontSourceIsWrap = true;
                }
                else if (!string.IsNullOrEmpty(wrapForFront)
                    && string.Equals(refValue, wrapForFront, StringComparison.OrdinalIgnoreCase))
                {
                    frontSourceIsWrap = true;
                }
            }
            else
            {
                if (partNorm == "wrap")
                {
                    var wrapKeys = new[]
                    {
                        $"book:{bookId}:printReadyCoverWrap",
                        $"book:{bookId}:printReadyCoverWrapApi"
                    };
                    var wrapRows = await _context.Settings.AsNoTracking()
                        .Where(s => wrapKeys.Contains(s.Key))
                        .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);
                    refValue = (wrapRows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrap") ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(refValue))
                        refValue = (wrapRows.GetValueOrDefault($"book:{bookId}:printReadyCoverWrapApi") ?? "").Trim();
                }
                else
                {
                    var row = await _context.Settings.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Key == settingKey, cancellationToken);
                    refValue = (row?.Value ?? "").Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(refValue))
                return NotFound("Cover asset not found.");

            await FinalizeBookAsPublishedAfterExportAsync(bookId, sessionUserId.Value, "print", cancellationToken);

            byte[]? bytes = await CoverImageRefLoader.TryReadAsBytesAsync(
                refValue, webRootPath: null, _httpClientFactory, cancellationToken);
            var ext = "png";
            var contentType = "image/png";

            if (bytes == null || bytes.Length == 0)
            {
                if (refValue.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                {
                    var comma = refValue.IndexOf(',', StringComparison.Ordinal);
                    if (comma >= 0)
                    {
                        try
                        {
                            bytes = Convert.FromBase64String(refValue[(comma + 1)..].Trim());
                            ext = refValue.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
                            contentType = ext == "jpg" ? "image/jpeg" : "image/png";
                        }
                        catch (FormatException)
                        {
                            return BadRequest("Invalid cover data URL.");
                        }
                    }
                }
            }

            if (bytes == null || bytes.Length == 0)
                return NotFound("Cover file missing or could not be loaded.");

            if (refValue.StartsWith("/", StringComparison.Ordinal))
            {
                ext = Path.GetExtension(refValue).ToLowerInvariant().TrimStart('.');
                if (string.IsNullOrEmpty(ext)) ext = "png";
                contentType = ext switch
                {
                    "jpg" or "jpeg" => "image/jpeg",
                    "webp" => "image/webp",
                    "gif" => "image/gif",
                    _ => "image/png"
                };
            }

            // For the standalone front cover, crop the front panel out of a full wrap when needed
            // (Both/Paperback store only the wrap). EnsureFrontPanelBytes is a no-op for true fronts.
            if (partNorm == "front")
            {
                try
                {
                    var pages = await ResolvePrintReadyPageCountAsync(bookId, cancellationToken);
                    var trim = (await _context.Settings.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:printReadyTrimSize", cancellationToken))?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(trim)) trim = "6 x 9 in";
                    var frontBytes = CoverWrapPanelExtractor.EnsureFrontPanelBytes(bytes, pages, trim, assumeWrap: frontSourceIsWrap);
                    if (frontBytes is { Length: > 0 } && !ReferenceEquals(frontBytes, bytes))
                    {
                        bytes = frontBytes;
                        ext = "png";
                        contentType = "image/png";
                    }
                }
                catch (Exception cropEx)
                {
                    _logger.LogWarning(cropEx, "Front-panel crop failed for book {BookId}; serving original image.", bookId);
                }
            }

            return File(bytes, contentType, $"book-{bookId}-{keySuffix}.{ext}");
        }

        [HttpGet]
        [Route("BookPageMetrics")]
        public async Task<IActionResult> BookPageMetrics(int bookId, CancellationToken cancellationToken = default)
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

            var exportOpt = await LoadExportOptionsAsync(sessionUserId.Value, bookId, cancellationToken);
            var metrics = _bookPageMetricsService.Estimate(details, exportOpt);

            // B3/B4: single source of truth for page count. When Book Formatting has run and
            // persisted its measured reader-page count (printReadyPageCount / formattingDraft),
            // prefer that — exactly what the Publish page uses — so AI Writer, Book Formatting
            // and Publish all agree. Fall back to the word-count estimate only when nothing has
            // been saved yet.
            var savedFormatterPages = await ResolvePrintReadyPageCountAsync(bookId, cancellationToken);
            var resolvedPageCount = savedFormatterPages > 0 ? savedFormatterPages : metrics.PageCount;
            var pageCountSource = savedFormatterPages > 0 ? "formatter" : "estimate";

            return Json(new
            {
                success = true,
                bookId,
                pageCount = resolvedPageCount,
                pageCountSource,
                wordCount = metrics.WordCount,
                chapterCount = metrics.ChapterCount,
                imageCount = metrics.ImageCount,
                basis = metrics.Basis
            });
        }

        [HttpPost]
        [Route("GeneratePrintReadyCover")]
        [Microsoft.AspNetCore.Http.Timeouts.RequestTimeout("CoverGeneration")]
        public async Task<IActionResult> GeneratePrintReadyCover([FromBody] PrintReadyCoverRequest req)
        {
            if (req == null || req.BookId <= 0)
                return Json(new { success = false, status = "error", message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, status = "error", message = "Please sign in." });

            var book = await _context.Books
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value);
            if (book == null)
                return Json(new { success = false, status = "error", message = "Book not found." });

            var assetRows = await _context.Settings.AsNoTracking()
                .Where(s => s.Key == $"book:{req.BookId}:printReadyCoverFront"
                    || s.Key == $"book:{req.BookId}:aiCoverLastPreview")
                .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");
            var savedFront = BookCoverRefResolver.ResolveEbookFrontCoverRef(
                assetRows.GetValueOrDefault($"book:{req.BookId}:printReadyCoverFront"),
                assetRows.GetValueOrDefault($"book:{req.BookId}:aiCoverLastPreview"),
                book.CoverImagePath,
                null);

            // Saved front cover → local compositor only (front panel stays pixel-identical).
            if (!string.IsNullOrWhiteSpace(savedFront))
            {
                req.Force = true;
                req.Wait = true;
                return await GeneratePrintReadyWrapFromFront(req);
            }

            // No saved front — formatter may request a full AI cover set (legacy path).
            var dbCancel = HttpContext.RequestAborted;
            await ClearPrintWrapCacheAsync(req.BookId, dbCancel);

            var details = await _bookService.GetBookDetailsForPreviewAsync(sessionUserId.Value, req.BookId);
            if (details == null || !details.Success)
                return Json(new { success = false, status = "error", message = details?.Message ?? "Could not load book details." });

            var exportOpt = await LoadExportOptionsAsync(sessionUserId.Value, req.BookId, dbCancel);
            var metrics = _bookPageMetricsService.Estimate(details, exportOpt);
            var savedPageCount = await ResolvePrintReadyPageCountAsync(req.BookId, dbCancel);
            var pageCountForCover = req.PageCount is > 0 ? req.PageCount.Value
                : (savedPageCount > 0 ? savedPageCount
                    : (metrics.PageCount > 0 ? metrics.PageCount : Application.Kdp.Constants.KdpPaperbackConstants.MinPageCount));
            pageCountForCover = Math.Clamp(pageCountForCover, 1, Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount);
            var trimSize = NormalizeTrimSizeForApi(req.TrimSize, exportOpt);
            var kdp = CalculatePrintReadyKdp(pageCountForCover, trimSize);

            var title = string.IsNullOrWhiteSpace(req.Title) ? (details.BookTitle ?? book.Title ?? "My Book").Trim() : req.Title!.Trim();
            if (string.IsNullOrWhiteSpace(title)) title = "My Book";

            var user = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value, dbCancel);
            var authorName = (req.Author ?? "").Trim();
            if (string.IsNullOrWhiteSpace(authorName))
                authorName = (user?.FullName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(authorName))
                authorName = (user?.UserEmail ?? "").Trim();
            if (string.IsNullOrWhiteSpace(authorName))
                authorName = sessionUserId.Value.ToString();

            var category = string.IsNullOrWhiteSpace(req.Genre)
                ? (details.Genre ?? book.Genre ?? "General").Trim()
                : req.Genre!.Trim();
            var styleKey = string.IsNullOrWhiteSpace(req.Style) ? "modern" : req.Style.Trim();
            var imageDirection = (req.Description ?? req.CoverStyle ?? "").Trim();
            var coverStyleBase = string.IsNullOrWhiteSpace(imageDirection)
                ? (_externalApiOptions.Value.PrintReadyCoverStyle ?? "").Trim()
                : CoverExternalApiHelper.MapCoverStyleForExternalApi(styleKey, imageDirection);
            if (string.IsNullOrWhiteSpace(coverStyleBase))
            {
                coverStyleBase = "Deep navy blue background with subtle damask pattern, ornate gold baroque decorative frame on front cover, elegant gold serif typography, luxurious premium publishing style.";
            }
            var coverStyle = BuildPrintReadyCoverStyleDirective(coverStyleBase, kdp);
            var quality = BookApiInputValidation.NormalizeQuality(
                (req.Quality ?? _externalApiOptions.Value.PrintReadyCoverQuality ?? "high").Trim(),
                "high");
            var size = BookApiInputValidation.NormalizeSize(
                (req.Size ?? _externalApiOptions.Value.PrintReadyCoverSize ?? "1536x1024").Trim(),
                "1536x1024");
            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.GenerateSpineBookCoverUrl, "/api/generate-spine-book-cover").Trim();
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, status = "error", message = ExternalApiKeyResolver.MissingKeyUserMessage });

            var payloadObj = new JObject
            {
                ["title"] = title,
                ["author_name"] = authorName,
                ["category"] = category,
                ["cover_style"] = coverStyle,
                ["size"] = size,
                ["quality"] = quality,
                ["Interior_trim_size"] = trimSize,
                ["page_count"] = pageCountForCover,
                ["paper_type"] = NormalizePaperTypeForExternalApi(kdp.PaperType)
            };

            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                httpRequest.Content = new StringContent(payloadObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

                using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
                var response = await _bookApiClient.SendAsync(httpRequest, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
                var responseData = await response.Content.ReadAsStringAsync(upstreamCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("GeneratePrintReadyCover HTTP {Code} for book {BookId}: {Body}", (int)response.StatusCode, req.BookId, responseData);
                    var upstreamMsg = CoverExternalApiHelper.TryExtractErrorMessage(responseData);
                    return Json(new
                    {
                        success = false,
                        status = "error",
                        message = upstreamMsg ?? $"Cover service returned {(int)response.StatusCode}. The AI queue may be busy — try again in a few minutes."
                    });
                }

                var urls = CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse(responseData);
                var assets = CoverExternalApiHelper.ExtractNamedCoverAssetsFromApiResponse(responseData);
                var wrapRef = !string.IsNullOrWhiteSpace(assets.Wrap) ? assets.Wrap : (urls.FirstOrDefault() ?? "");
                if (string.IsNullOrWhiteSpace(wrapRef))
                    return Json(new { success = false, status = "error", message = "No image found in print-ready cover response." });

                var persistedWrap = await TryPersistCoverReferenceAsync(sessionUserId.Value, req.BookId, wrapRef, CancellationToken.None) ?? wrapRef;
                var persistedFront = string.IsNullOrWhiteSpace(assets.Front)
                    ? ""
                    : (await TryPersistCoverReferenceAsync(sessionUserId.Value, req.BookId, assets.Front, CancellationToken.None) ?? CoverExternalApiHelper.NormalizeImageRef(assets.Front));
                var persistedBack = string.IsNullOrWhiteSpace(assets.Back)
                    ? ""
                    : (await TryPersistCoverReferenceAsync(sessionUserId.Value, req.BookId, assets.Back, CancellationToken.None) ?? CoverExternalApiHelper.NormalizeImageRef(assets.Back));
                var persistedSpine = string.IsNullOrWhiteSpace(assets.Spine)
                    ? ""
                    : (await TryPersistCoverReferenceAsync(sessionUserId.Value, req.BookId, assets.Spine, CancellationToken.None) ?? CoverExternalApiHelper.NormalizeImageRef(assets.Spine));

                if (persistedWrap.Length <= Models.Settings.DbCompatMaxValueLength)
                {
                    await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyCoverWrapApi", persistedWrap, "Book", CancellationToken.None);
                    await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyCoverWrap", persistedWrap, "Book", CancellationToken.None);
                }
                if (!string.IsNullOrWhiteSpace(persistedFront))
                    await SaveFrontCoverPreviewAsync(sessionUserId.Value, req.BookId, persistedFront, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(persistedBack))
                    await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyCoverBack", persistedBack, "Book", CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(persistedSpine))
                    await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyCoverSpine", persistedSpine, "Book", CancellationToken.None);
                await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyPageCount", pageCountForCover.ToString(), "Book", CancellationToken.None);
                await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyTrimSize", trimSize, "Book", CancellationToken.None);

                return Json(new
                {
                    success = true,
                    status = "success",
                    bookId = req.BookId,
                    pageCount = pageCountForCover,
                    trimSize,
                    kdp = new
                    {
                        spineInches = (double)kdp.SpineWidth,
                        spineMm = (double)(kdp.SpineWidth * 25.4m),
                        wrapWidthInches = (double)kdp.FullCoverWidth,
                        wrapHeightInches = (double)kdp.FullCoverHeight,
                        wrapWidthMm = (double)(kdp.FullCoverWidth * 25.4m),
                        wrapHeightMm = (double)(kdp.FullCoverHeight * 25.4m),
                        pixelWidth = kdp.PixelWidth,
                        pixelHeight = kdp.PixelHeight,
                        spinePixels = kdp.SpinePixels,
                        paperType = kdp.PaperType,
                        interiorType = kdp.InteriorType,
                        bindingType = kdp.BindingType,
                        outerMarginInches = (double)kdp.Bleed,
                        hingeGapInches = 0,
                        panelWidthInches = (double)kdp.FrontCoverWidth,
                        panelHeightInches = (double)(kdp.SafeAreaHeight + kdp.MarginHeight)
                    },
                    layout = new
                    {
                        backPanelXInches = (double)kdp.BackPanelXInches,
                        spineXInches = (double)kdp.SpineXInches,
                        frontPanelXInches = (double)kdp.FrontPanelXInches,
                        panelTopYInches = (double)kdp.PanelTopYInches
                    },
                    cover = new
                    {
                        wrap = persistedWrap,
                        front = persistedFront,
                        spine = persistedSpine,
                        back = persistedBack
                    },
                    options = urls.ToArray()
                });
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("GeneratePrintReadyCover timed out for book {BookId}", req.BookId);
                return Json(new
                {
                    success = false,
                    status = "error",
                    message = "Print wrap generation timed out. Please retry with a shorter prompt or try again in a moment."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GeneratePrintReadyCover failed for book {BookId}", req.BookId);
                return Json(new { success = false, status = "error", message = ex.Message });
            }
        }

        /// <summary>
        /// Builds full print wrap from the saved front cover. Default: queues background work (fast JSON).
        /// Set <see cref="PrintReadyCoverRequest.Wait"/> to wait for upstream and return saved assets.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("GeneratePrintReadyWrapFromFront")]
        [Microsoft.AspNetCore.Http.Timeouts.RequestTimeout("CoverGeneration")]
        public async Task<IActionResult> GeneratePrintReadyWrapFromFront([FromBody] PrintReadyCoverRequest req)
        {
            if (req == null || req.BookId <= 0)
                return Json(new { success = false, status = "error", message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, status = "error", message = "Please sign in." });

            var book = await _context.Books
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value);
            if (book == null)
                return Json(new { success = false, status = "error", message = "Book not found." });

            var assetKeys = new[]
            {
                $"book:{req.BookId}:printReadyCoverFront",
                $"book:{req.BookId}:aiCoverLastPreview"
            };
            var assetRows = await _context.Settings.AsNoTracking()
                .Where(s => assetKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");
            var savedFront = BookCoverRefResolver.ResolveEbookFrontCoverRef(
                assetRows.GetValueOrDefault($"book:{req.BookId}:printReadyCoverFront"),
                assetRows.GetValueOrDefault($"book:{req.BookId}:aiCoverLastPreview"),
                book.CoverImagePath,
                null);
            if (string.IsNullOrWhiteSpace(savedFront))
                return Json(new { success = false, status = "error", message = "Generate a front cover in Cover Design first." });

            if (req.PageCount is > 0)
            {
                await UpsertDashboardSettingAsync(
                    $"book:{req.BookId}:printReadyPageCount",
                    req.PageCount.Value.ToString(CultureInfo.InvariantCulture),
                    "Book",
                    CancellationToken.None);
            }

            if (req.Force)
                await ClearPrintWrapCacheAsync(req.BookId, CancellationToken.None);

            if (!req.Wait)
            {
                _printWrapPregenerationQueue.QueueAfterFrontCoverSaved(sessionUserId.Value, req.BookId);
                return Json(new
                {
                    success = true,
                    status = "processing",
                    message = "Building full print wrap in background — poll GetPrintReadyCoverAssets."
                });
            }

            // Calls upstream split API (realistic AI wrap); falls back to local compositor if API fails.
            var ok = await _printWrapGenerationService.TryGenerateFromSavedFrontAsync(
                sessionUserId.Value,
                req.BookId,
                req.PageCount,
                req.Force,
                CancellationToken.None);

            if (!ok)
            {
                var statusKey = $"book:{req.BookId}:printReadyCoverWrapStatus";
                var statusRow = await _context.Settings.AsNoTracking()
                    .Where(s => s.Key == statusKey)
                    .Select(s => s.Value)
                    .FirstOrDefaultAsync(CancellationToken.None);
                var failMessage = (statusRow ?? "").Trim() switch
                {
                    PrintWrapGenerationService.StatusMissingFront =>
                        "Generate a front cover in Cover Design first — the full wrap uses that exact image.",
                    _ => "Full wrap generation failed. Confirm your front cover is saved and retry."
                };
                return Json(new { success = false, status = "error", message = failMessage });
            }

            return await GetPrintReadyCoverAssetsCoreAsync(req.BookId, CancellationToken.None);
        }

        private async Task<BookPdfExportOptions> LoadExportOptionsAsync(int userId, int bookId, CancellationToken cancellationToken)
        {
            var draftRow = await _context.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:formattingDraft", cancellationToken);
            var fmtRow = await _context.BookFormatting.AsNoTracking()
                .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == userId, cancellationToken);
            return BookPdfExportOptions.LoadFromPersistence(fmtRow, draftRow?.Value);
        }

        private static bool IsValidPdfBytes(byte[]? pdfBytes) =>
            pdfBytes is { Length: >= 128 }
            && pdfBytes[0] == (byte)'%'
            && pdfBytes[1] == (byte)'P'
            && pdfBytes[2] == (byte)'D'
            && pdfBytes[3] == (byte)'F';

        private static string InferPaperTypeFromGenre(string? genre)
        {
            var g = (genre ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(g)) return "White paper";
            if (g.Contains("kids") || g.Contains("children") || g.Contains("child") || g.Contains("picture"))
                return "White paper";
            if (g.Contains("romance") || g.Contains("fantasy") || g.Contains("historical") || g.Contains("poetry") || g.Contains("drama"))
                return "Cream paper";
            return "White paper";
        }

        /// <summary>
        /// Saves the user's publishing format chosen on the Publish page.
        /// Ebook → front cover + EPUB; Paperback → full wrap + interior PDF; Both → all files.
        /// Writes BookFormatting.Format, which wins over any stale formatter draft.
        /// </summary>
        [HttpPost]
        [Route("Dashboard/SetPublishFormat")]
        public async Task<IActionResult> SetPublishFormat([FromBody] SetPublishFormatRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0)
                return Json(new { success = false, message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, message = "Please sign in." });

            var format = (req.Format ?? "").Trim();
            if (format.Equals("Print", StringComparison.OrdinalIgnoreCase)) format = "Paperback";
            if (!format.Equals("Ebook", StringComparison.OrdinalIgnoreCase)
                && !format.Equals("Paperback", StringComparison.OrdinalIgnoreCase)
                && !format.Equals("Both", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Format must be Ebook, Paperback, or Both." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns)
                return Json(new { success = false, message = "Book not found." });

            var row = await _context.BookFormatting
                .FirstOrDefaultAsync(f => f.BookId == req.BookId && f.UserId == sessionUserId.Value, cancellationToken);
            if (row == null)
            {
                row = new BookFormatting
                {
                    BookId = req.BookId,
                    UserId = sessionUserId.Value,
                    CreatedAt = DateTime.UtcNow
                };
                _context.BookFormatting.Add(row);
            }
            row.Format = char.ToUpperInvariant(format[0]) + format.Substring(1).ToLowerInvariant();
            row.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return Json(new { success = true, format = row.Format });
        }

        [HttpPost]
        [Route("SavePrintReadyComposedWrap")]
        public async Task<IActionResult> SavePrintReadyComposedWrap([FromBody] SavePrintReadyWrapRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0)
                return Json(new { success = false, message = "BookId is required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, message = "Please sign in." });

            var book = await _context.Books.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (book == null)
                return Json(new { success = false, message = "Book not found." });

            await _draftReset.ClearCoverAssetsAsync(req.BookId, cancellationToken);

            var wrapRef = (req.WrapImageDataUrl ?? req.WrapImageBase64 ?? "").Trim();
            if (string.IsNullOrWhiteSpace(wrapRef))
                return Json(new { success = false, message = "Wrap image is required." });

            var persisted = await TryPersistCoverReferenceAsync(sessionUserId.Value, req.BookId, wrapRef, cancellationToken);
            if (string.IsNullOrWhiteSpace(persisted))
                return Json(new { success = false, message = "Could not save wrap image." });

            await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyCoverWrap", persisted, "Book", cancellationToken);
            if (req.SpineInches.HasValue)
                await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadySpineInches", req.SpineInches.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), "Book", cancellationToken);
            if (req.PageCount.HasValue)
                await UpsertDashboardSettingAsync($"book:{req.BookId}:printReadyPageCount", req.PageCount.Value.ToString(), "Book", cancellationToken);

            return Json(new { success = true, wrap = persisted });
        }

        private static string NormalizeTrimSizeForApi(string? trimFromRequest, BookPdfExportOptions exportOptions)
        {
            var src = (trimFromRequest ?? "").Trim();
            if (string.IsNullOrWhiteSpace(src))
                src = (exportOptions.Format ?? "").Trim();

            var compact = src.Replace(" ", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            if (compact == "5.5x8.5" || compact == "5.5xin8.5in") return "5.5 x 8.5 in";
            if (compact == "8.5x11" || compact == "8.5xin11in") return "8.5 x 11 in";
            if (compact == "6x9" || compact == "6xin9in" || compact == "paperback" || compact == "print" || compact == "both")
                return "6 x 9 in";
            return "6 x 9 in";
        }

        /// <summary>Preview page count saved from Book Formatting (printReadyPageCount or formattingDraft).</summary>
        private async Task<int> ResolvePrintReadyPageCountAsync(int bookId, CancellationToken cancellationToken = default)
        {
            const int max = Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount;

            var pageKey = $"book:{bookId}:printReadyPageCount";
            var saved = await _context.Settings.AsNoTracking()
                .Where(s => s.Key == pageKey)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
            if (int.TryParse(saved, out var fromSaved) && fromSaved > 0 && fromSaved <= max)
                return fromSaved;

            var draftKey = $"book:{bookId}:formattingDraft";
            var draft = await _context.Settings.AsNoTracking()
                .Where(s => s.Key == draftKey)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
            return TryParsePreviewPageCountFromDraft(draft);
        }

        private static int TryParsePreviewPageCountFromDraft(string? draftJson)
        {
            const int max = Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount;
            if (string.IsNullOrWhiteSpace(draftJson)) return 0;
            try
            {
                using var doc = JsonDocument.Parse(draftJson);
                if (doc.RootElement.TryGetProperty("previewPageCount", out var pp)
                    && pp.TryGetInt32(out var n) && n >= 1 && n <= max)
                    return n;
            }
            catch (JsonException) { /* ignore malformed draft */ }
            return 0;
        }

        /// <summary>Print-ready flow: Standard Color + White Paper, bleed on, 150 DPI (KDP Cover Calculator defaults).</summary>
        private KdpCalculateResponse CalculatePrintReadyKdp(int pageCount, string trimSizeLabel)
        {
            var trim = ParseTrimInches(trimSizeLabel);
            return _kdpCoverDimensions.Calculate(new KdpCalculateRequest
            {
                PageCount = pageCount,
                TrimWidth = trim.W,
                TrimHeight = trim.H,
                Dpi = Application.Kdp.Constants.KdpPaperbackConstants.DefaultDpi,
                Bleed = true,
                InteriorType = Application.Kdp.Constants.KdpPaperbackConstants.InteriorTypeStandardColor,
                PaperType = Application.Kdp.Constants.KdpPaperbackConstants.PaperTypeWhite
            });
        }

        private static (decimal W, decimal H) ParseTrimInches(string? trimSize)
        {
            var src = (trimSize ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (src.Contains("5.5") && src.Contains("8.5"))
                return (5.5m, 8.5m);
            if (src.Contains("8.5") && src.Contains("11"))
                return (8.5m, 11m);
            if (src.Contains("4.75") || src.Contains("4-3/4") || (src.Contains("5.25") && !src.Contains("8.5")))
                return (4.75m, 5.25m);
            return (6m, 9m);
        }

        private static string BuildPrintReadyCoverStyleDirective(string baseStyle, KdpCalculateResponse kdp)
        {
            var style = (baseStyle ?? "").Trim();
            if (string.IsNullOrWhiteSpace(style))
                style = "Premium market-ready wrap cover with elegant typography and rich layered background.";

            var layoutNote =
                $"Paperback layout: {kdp.Bleed:F3} in bleed, spine {kdp.SpineWidth:F3} in for {kdp.PageCount} pages.";

            var cohesion = string.Join(" ",
                "MANDATORY PRINT WRAP RULES:",
                "Create ONE continuous wraparound design (back + spine + front) with identical palette, textures, gradients, and ornamental language on all three panels.",
                "The back cover MUST repeat the same background colors and decorative frame system as the front — never a different color scheme on the back.",
                "The spine strip must visually continue the front/back background seamlessly across the exact spine width.",
                layoutNote,
                $"Full cover canvas: {kdp.FullCoverWidth:F3} in × {kdp.FullCoverHeight:F3} in.",
                "Panel order left-to-right: back cover, spine, front cover.",
                "No unrelated artwork on the back; back continues the front design with synopsis-safe space.");

            return style.Contains("MANDATORY PRINT WRAP", StringComparison.OrdinalIgnoreCase)
                ? style
                : style + " " + cohesion;
        }

        private static string NormalizePaperTypeForExternalApi(string? paperType)
        {
            if ((paperType ?? "").Contains("cream", StringComparison.OrdinalIgnoreCase))
                return "cream";
            return "white";
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

        /// <summary>Removes saved full-wrap assets so the next Publish/export rebuilds from the current front cover.</summary>
        private async Task ClearPrintWrapCacheAsync(int bookId, CancellationToken cancellationToken)
        {
            var keys = new[]
            {
                $"book:{bookId}:printReadyCoverWrap",
                $"book:{bookId}:printReadyCoverWrapApi",
                $"book:{bookId}:printReadyCoverBack",
                $"book:{bookId}:printReadyCoverSpine"
            };
            var rows = await _context.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(cancellationToken);
            if (rows.Count == 0) return;
            _context.Settings.RemoveRange(rows);
            await _context.SaveChangesAsync(cancellationToken);
        }

        /// <summary>Data URLs and remote http(s) URLs are saved under wwwroot/uploads; local paths are returned as-is.</summary>
        private async Task<string?> TryPersistCoverReferenceAsync(int userId, int bookId, string imageRef, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imageRef)) return null;
            var t = imageRef.Trim();
            if (t.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                return await SaveDataUrlCoverToUploadsAsync(userId, bookId, t, cancellationToken);
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return await DownloadRemoteCoverToUploadsAsync(userId, bookId, t, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not download remote cover for book {BookId}; storing URL reference", bookId);
                    return t;
                }
            }
            if (t.StartsWith("/", StringComparison.Ordinal))
                return t;
            return null;
        }

        /// <summary>Persists front-cover preview keys and updates <see cref="Book.CoverImagePath"/> when stored locally.</summary>
        private async Task SaveFrontCoverPreviewAsync(
            int userId,
            int bookId,
            string persisted,
            CancellationToken cancellationToken,
            bool invalidateCachedWrap = true)
        {
            if (string.IsNullOrWhiteSpace(persisted)) return;

            if (invalidateCachedWrap)
                await ClearPrintWrapCacheAsync(bookId, cancellationToken);

            if (persisted.Length <= Models.Settings.DbCompatMaxValueLength)
            {
                await UpsertDashboardSettingAsync($"book:{bookId}:aiCoverLastPreview", persisted, "Book", cancellationToken);
                await UpsertDashboardSettingAsync($"book:{bookId}:printReadyCoverFront", persisted, "Book", cancellationToken);
                await UpsertDashboardSettingAsync($"book:{bookId}:printReadyCoverFrontAssetRef", persisted, "Book", cancellationToken);
                var frontBytes = await CoverImageRefLoader.TryReadAsBytesAsync(
                    persisted, webRootPath: null, _httpClientFactory, cancellationToken);
                if (frontBytes is { Length: > 0 })
                {
                    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frontBytes));
                    await UpsertDashboardSettingAsync($"book:{bookId}:printReadyCoverFrontSha256", hash, "Book", cancellationToken);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Cover preview URL/path length {Len} exceeds DbCompatMaxValueLength; skipping Settings save. Run: ALTER TABLE `Settings` MODIFY COLUMN `Value` LONGTEXT NULL;",
                    persisted.Length);
            }

            await UpdateBookCoverImagePathIfLocalAsync(userId, bookId, persisted, cancellationToken);
        }

        private async Task UpdateBookCoverImagePathIfLocalAsync(int userId, int bookId, string persisted, CancellationToken cancellationToken)
        {
            var p = persisted.Trim();
            if (!p.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return;

            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
            if (book == null) return;
            book.CoverImagePath = p;
            book.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
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
            return await SaveCoverBytesToUploadsAsync(userId, bookId, bytes, ext, "cover_ai", cancellationToken);
        }

        private async Task<string> DownloadRemoteCoverToUploadsAsync(int userId, int bookId, string url, CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(2);
            using var response = await client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length == 0) throw new InvalidOperationException("Remote cover image was empty.");

            var ext = ".png";
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || contentType.Contains("jpg", StringComparison.OrdinalIgnoreCase))
                ext = ".jpg";
            else if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase))
                ext = ".webp";
            else
            {
                var pathExt = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
                if (pathExt is ".jpg" or ".jpeg" or ".png" or ".webp")
                    ext = pathExt == ".jpeg" ? ".jpg" : pathExt;
            }

            return await SaveCoverBytesToUploadsAsync(userId, bookId, bytes, ext, "cover_ai", cancellationToken);
        }

        private static async Task<string> SaveCoverBytesToUploadsAsync(
            int userId,
            int bookId,
            byte[] bytes,
            string ext,
            string namePrefix,
            CancellationToken cancellationToken)
        {
            var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", userId.ToString(), "books", bookId.ToString());
            Directory.CreateDirectory(uploadsRoot);
            var fileName = $"{namePrefix}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
            var fullPath = Path.Combine(uploadsRoot, fileName);
            await System.IO.File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
            return $"/uploads/{userId}/books/{bookId}/{fileName}";
        }

        [Route("AudioBook")]
        public IActionResult AudioBook()
        {
            // Feature not available yet — sidebar entry is commented out; block direct URL access.
            TempData["InfoMessage"] = "Audio book tools are not available yet. Check back later.";
            return RedirectToAction("Index", "Dashboard");
        }

        private async Task<Dictionary<int, int>> BuildExportableChapterCountsAsync(
            int userId,
            IReadOnlyCollection<int> bookIds,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<int, int>();
            if (userId <= 0 || bookIds.Count == 0)
                return result;

            var ids = bookIds.Distinct().ToList();

            var chapterPairs = await _context.Chapters.AsNoTracking()
                .Where(c => ids.Contains(c.BookId) && c.Content != null && c.Content.Trim().Length > 0)
                .Select(c => new { c.BookId, c.ChapterNumber })
                .ToListAsync(cancellationToken);

            var rawPairs = await _context.APIRawResponse.AsNoTracking()
                .Where(r => r.UserId == userId
                    && r.BookId != null
                    && ids.Contains(r.BookId.Value)
                    && r.Content != null
                    && r.Content.Trim().Length > 0)
                .Select(r => new { BookId = r.BookId!.Value, ChapterNumber = r.Chapter })
                .ToListAsync(cancellationToken);

            var iterationPairs = await _context.ChapterIterations.AsNoTracking()
                .Where(i => i.UserId == userId
                    && ids.Contains(i.BookId)
                    && i.Content != null
                    && i.Content.Trim().Length > 0)
                .Select(i => new { i.BookId, i.ChapterNumber })
                .ToListAsync(cancellationToken);

            foreach (var g in chapterPairs
                .Concat(rawPairs)
                .Concat(iterationPairs)
                .Distinct()
                .GroupBy(x => x.BookId))
            {
                result[g.Key] = g.Count();
            }

            return result;
        }

        /// <summary>Publishing hub: external platform guides + full-service option (demo gating by role/plan).</summary>
        [Route("/publish")]
        [Route("Publish")]
        public async Task<IActionResult> Publish(int? bookId = null, string? flow = null)
        {
            ViewBag.UserName = User.Identity?.Name ?? "User";
            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            var roleId = user?.RoleId ?? 0;
            ViewBag.PublishViaPlatformPaid = true;
            ViewBag.PublishableKey = StripeKeys.Publishable(_configuration) ?? "";
            ViewBag.SelectedBookId = bookId;
            ViewBag.PublishBookTitle = (string?)null;
            ViewBag.PublishBookDescription = (string?)null;
            ViewBag.PublishBookGenre = (string?)null;
            ViewBag.PublishBookCover = (string?)null;
            ViewBag.PublishBookStatus = (string?)null;
            ViewBag.PublishBookAlreadyListed = false;
            ViewBag.PublishBookChapterCount = 0;
            ViewBag.PublishBookEstimatedPages = 0;
            ViewBag.PublishBookWordCount = 0;
            ViewBag.PublishBookReady = false;
            ViewBag.PublishPrintReadyMode = false;
            ViewBag.ExternalApiConfigured = !string.IsNullOrEmpty(ExternalApiKeyResolver.Resolve(_configuration));

            var forcedPrintReadyFlow = string.Equals((flow ?? "").Trim(), "printready", StringComparison.OrdinalIgnoreCase);

            if (user != null)
            {
                var publishBookRows = await _context.Books.AsNoTracking()
                    .Where(b => b.UserId == user.UserId)
                    .OrderByDescending(b => b.UpdatedAt)
                    .ThenByDescending(b => b.CreatedAt)
                    .Select(b => new { b.BookId, Title = string.IsNullOrWhiteSpace(b.Title) ? "Untitled" : b.Title! })
                    .ToListAsync();

                var publishBookIds = publishBookRows.Select(x => x.BookId).ToList();
                var exportableCounts = await BuildExportableChapterCountsAsync(user.UserId, publishBookIds, HttpContext.RequestAborted);
                var publishPicker = new List<PublishBookPickerItem>();
                foreach (var row in publishBookRows)
                {
                    var exportable = exportableCounts.GetValueOrDefault(row.BookId, 0);

                    publishPicker.Add(new PublishBookPickerItem
                    {
                        BookId = row.BookId,
                        Title = row.Title,
                        ExportableChapterCount = exportable,
                        CanExport = exportable > 0
                    });
                }

                ViewBag.PublishBooks = publishPicker;
            }
            else
            {
                ViewBag.PublishBooks = new List<PublishBookPickerItem>();
            }

            if (user != null && bookId.HasValue && bookId.Value > 0)
            {
                var pb = await _context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.BookId == bookId.Value && b.UserId == user.UserId);
                if (pb != null)
                {
                    HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId.Value);
                    HttpContext.Session.SetInt32("LastSelectedBookId", bookId.Value);
                    var (publishFlowStep, _) = await _bookFlow.GetStepAsync(bookId.Value);
                    if (BookPublishReadinessService.IsPublishReadyBookStatus(pb.Status))
                        BookResumeUrlHelper.SyncFlowSessionFlags(HttpContext, BookFlowStateService.StepPublish);
                    else
                        BookResumeUrlHelper.SyncFlowSessionFlags(HttpContext, publishFlowStep);

                    ViewBag.PublishBookTitle = pb.Title;
                    ViewBag.PublishBookDescription = pb.Description;
                    ViewBag.PublishBookGenre = pb.Genre;
                    var frontCoverKey = $"book:{bookId.Value}:printReadyCoverFront";
                    var aiCoverKey = $"book:{bookId.Value}:aiCoverLastPreview";
                    var wrapKey = $"book:{bookId.Value}:printReadyCoverWrap";
                    var wrapApiKey = $"book:{bookId.Value}:printReadyCoverWrapApi";
                    var coverRows = await _context.Settings.AsNoTracking()
                        .Where(s => s.Key == frontCoverKey || s.Key == aiCoverKey || s.Key == wrapKey || s.Key == wrapApiKey)
                        .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");
                    var frontCover = (coverRows.GetValueOrDefault(frontCoverKey) ?? "").Trim();
                    var aiCover = (coverRows.GetValueOrDefault(aiCoverKey) ?? "").Trim();
                    var wrapCover = (coverRows.GetValueOrDefault(wrapKey) ?? "").Trim();
                    if (string.IsNullOrEmpty(wrapCover))
                        wrapCover = (coverRows.GetValueOrDefault(wrapApiKey) ?? "").Trim();
                    var pathCover = (pb.CoverImagePath ?? "").Trim();
                    // Screen preview: front panel only. Full wrap is download-only (printReadyCoverWrap).
                    var resolvedFront = BookCoverRefResolver.ResolveEbookFrontCoverRef(frontCover, aiCover, pathCover, wrapCover);
                    ViewBag.PublishBookCover = !string.IsNullOrEmpty(resolvedFront)
                        ? resolvedFront
                        : (!string.IsNullOrEmpty(aiCover) ? aiCover : pathCover);
                    ViewBag.PublishBookCoverWrap = !string.IsNullOrEmpty(wrapCover) ? wrapCover : "";
                    ViewBag.PublishHasFrontCover = !string.IsNullOrEmpty(frontCover) || !string.IsNullOrEmpty(aiCover) || !string.IsNullOrEmpty(pathCover);
                    ViewBag.PublishHasCoverWrap = !string.IsNullOrEmpty(wrapCover);
                    ViewBag.PublishBookStatus = pb.Status;
                    var ps = pb.Status ?? "";
                    ViewBag.PublishBookAlreadyListed = BookPublishReadinessService.IsListedBookStatus(ps);

                    var bid = bookId.Value;
                    var uid = user.UserId;
                    var exportableChapterCount = 0;
                    BookDetailsResponseDto? details = null;
                    try
                    {
                        details = await _bookService.GetBookDetailsForPreviewAsync(user.UserId, bid);
                        if (details != null && details.Success)
                        {
                            exportableChapterCount = details.Chapters?
                                .Count(c => !string.IsNullOrWhiteSpace(c.Content)) ?? 0;
                            ViewBag.PublishBookChapterCount = exportableChapterCount;

                            var exportOpt = await LoadExportOptionsAsync(user.UserId, bid, HttpContext.RequestAborted);
                            var metrics = _bookPageMetricsService.Estimate(details, exportOpt);

                            var savedPageKey = $"book:{bid}:printReadyPageCount";
                            var savedPageRaw = await _context.Settings.AsNoTracking()
                                .Where(s => s.Key == savedPageKey)
                                .Select(s => s.Value)
                                .FirstOrDefaultAsync(HttpContext.RequestAborted);
                            var estimatedPages = metrics.PageCount;
                            if (int.TryParse(savedPageRaw, out var savedPages)
                                && savedPages >= 1
                                && savedPages <= Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount)
                                estimatedPages = savedPages;
                            else
                            {
                                var draftKey = $"book:{bid}:formattingDraft";
                                var draftRaw = await _context.Settings.AsNoTracking()
                                    .Where(s => s.Key == draftKey)
                                    .Select(s => s.Value)
                                    .FirstOrDefaultAsync(HttpContext.RequestAborted);
                                var draftPages = TryParsePreviewPageCountFromDraft(draftRaw);
                                if (draftPages > 0) estimatedPages = draftPages;
                                // #11/#12: when no formatter-measured count exists, fall back to the
                                // canonical BookPageMetricsService estimate (already in estimatedPages)
                                // — the SAME value used by /Dashboard/BookPageMetrics, the KDP spine
                                // calc and pricing — instead of a Publish-only formula that produced a
                                // mismatching page count.
                            }

                            ViewBag.PublishBookEstimatedPages = estimatedPages;
                            ViewBag.PublishBookWordCount = metrics.WordCount;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Publish: could not compute page metrics for book {BookId}", bid);
                    }

                    var hasChapterContent = exportableChapterCount > 0;
                    if (details != null && details.Success)
                    {
                        await _publishReadiness.TryPromoteBookToFinalizedAsync(
                            user.UserId,
                            bid,
                            details.Chapters ?? new List<ChapterDto>(),
                            HttpContext.RequestAborted);
                    }
                    var statusReady = BookPublishReadinessService.IsPublishReadyBookStatus(ps) || hasChapterContent;

                    var fmt = await _context.BookFormatting.AsNoTracking()
                        .FirstOrDefaultAsync(f => f.BookId == bookId.Value && f.UserId == user.UserId);
                    var exportOptForMode = await LoadExportOptionsAsync(user.UserId, bid, HttpContext.RequestAborted);
                    var publishFormat = (exportOptForMode.Format ?? fmt?.Format ?? "").Trim();
                    if (publishFormat.Equals("Print", StringComparison.OrdinalIgnoreCase))
                        publishFormat = "Paperback";
                    if (!publishFormat.Equals("Ebook", StringComparison.OrdinalIgnoreCase)
                        && !publishFormat.Equals("Paperback", StringComparison.OrdinalIgnoreCase)
                        && !publishFormat.Equals("Both", StringComparison.OrdinalIgnoreCase))
                        publishFormat = "Ebook";
                    ViewBag.PublishBookFormat = publishFormat;
                    var primaryPlatform = exportOptForMode.PrimaryPlatformToken();
                    if (string.IsNullOrWhiteSpace(primaryPlatform))
                        primaryPlatform = (fmt?.PublishingPlatform ?? "").Trim();
                    var platformCsv = (exportOptForMode.PublishingPlatforms ?? fmt?.PublishingPlatforms ?? "").Trim();
                    ViewBag.PublishFormatterSnapshot = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        interiorStyle = exportOptForMode.InteriorStyle,
                        textSize = exportOptForMode.TextSize,
                        lineSpacing = exportOptForMode.LineSpacing,
                        bookFormat = exportOptForMode.Format,
                        publishingPlatform = primaryPlatform,
                        publishingPlatforms = platformCsv,
                        previewAccent = exportOptForMode.PreviewAccent ?? "",
                        pageBackgroundColor = exportOptForMode.ResolvePageBackgroundColor()
                    });
                    ViewBag.PublishPrimaryPlatform = string.IsNullOrWhiteSpace(primaryPlatform) ? "Amazon KDP" : primaryPlatform;
                    var selectedPlatforms = platformCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var hasPrintReadyPlatform =
                        primaryPlatform.Equals("Just Print Ready File", StringComparison.OrdinalIgnoreCase)
                        || primaryPlatform.Equals("Publishable Book", StringComparison.OrdinalIgnoreCase)
                        || selectedPlatforms.Any(p => p.Equals("Just Print Ready File", StringComparison.OrdinalIgnoreCase)
                            || p.Equals("Publishable Book", StringComparison.OrdinalIgnoreCase));
                    var isPaperbackFormat = publishFormat.Equals("Paperback", StringComparison.OrdinalIgnoreCase);
                    var isBothFormatFlag = publishFormat.Equals("Both", StringComparison.OrdinalIgnoreCase);
                    var isEbookOnly = publishFormat.Equals("Ebook", StringComparison.OrdinalIgnoreCase);
                    // Print wrap exports only for Paperback/Both — never for Ebook-only, even with ?flow=printready.
                    var isPrintReadyFlow = isPaperbackFormat || isBothFormatFlag
                        || (!isEbookOnly && (hasPrintReadyPlatform || forcedPrintReadyFlow));
                    ViewBag.PublishPrintReadyMode = isPrintReadyFlow;
                    ViewBag.PublishBothFormat = isBothFormatFlag;
                    var canExport = hasChapterContent;
                    ViewBag.PublishCanExport = canExport;
                    ViewBag.PublishBookReady = canExport;
                }

                var (_, publishPath) = await _bookFlow.GetStepAsync(bookId.Value);
                if (forcedPrintReadyFlow || (ViewBag.PublishPrintReadyMode is bool prm && prm))
                    publishPath = "print";
                await _bookFlow.SaveStepAsync(bookId.Value, BookFlowStateService.StepPublish, publishPath);
                ViewBag.FlowBookId = bookId.Value;
                ViewBag.FlowStep = BookFlowStateService.StepPublish;
                ViewBag.FlowPath = publishPath;
                ViewBag.FlowBackUrl = $"/Dashboard/CoverDesign?bookId={bookId.Value}";
            }

            return View();
        }

        /// <summary>Soft or hard flow step reset when user navigates back (legacy API — delegates to <see cref="IEditorDraftResetService"/>).</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Dashboard/ResetFlowStep")]
        public async Task<IActionResult> ResetFlowStep([FromBody] ResetFlowStepRequest req)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Json(new { success = false, message = "Please sign in." });
            if (req == null || req.BookId <= 0 || string.IsNullOrWhiteSpace(req.Step))
                return Json(new { success = false, message = "Invalid request." });

            EditorDraftResetScope scope;
            if (req.WipeAllWork)
                scope = EditorDraftResetScope.BackToWriter;
            else if (req.DestructiveBack)
                scope = EditorDraftResetScope.StepBack;
            else
            {
                await _bookFlow.RegressStepAsync(req.BookId, req.Step.Trim().ToLowerInvariant());
                var (fallbackStep, fallbackPath) = await _bookFlow.GetStepAsync(req.BookId);
                return Json(new
                {
                    success = true,
                    step = fallbackStep,
                    wiped = false,
                    resumeUrl = _bookFlow.BuildResumeUrl(req.BookId, fallbackStep, fallbackPath)
                });
            }

            var result = await _draftReset.ResetAsync(
                req.BookId,
                userId.Value,
                scope,
                req.Step,
                HttpContext,
                HttpContext.RequestAborted);

            if (result.Success) ClearEditorTempData();
            return Json(ToResetJson(result));
        }

        /// <summary>Resets temporary editor draft data (back, start over, new project, full reset).</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("ResetEditorDraft")]
        public async Task<IActionResult> ResetEditorDraft([FromBody] EditorDraftResetRequest req)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
                return Json(new { success = false, message = "Please sign in." });
            if (req == null || req.BookId <= 0)
                return Json(new { success = false, message = "BookId is required." });

            if (!TryParseResetScope(req.Scope, out var scope))
                return Json(new { success = false, message = "Invalid reset scope." });

            var result = await _draftReset.ResetAsync(
                req.BookId,
                userId.Value,
                scope,
                req.CurrentStep,
                HttpContext,
                HttpContext.RequestAborted);

            if (result.Success) ClearEditorTempData();
            return Json(ToResetJson(result));
        }

        private void ClearEditorTempData()
        {
            foreach (var key in TempData.Keys.ToList())
                TempData.Remove(key);
        }

        private static object ToResetJson(EditorDraftResetResult result) => new
        {
            success = result.Success,
            message = result.Message,
            bookId = result.BookId,
            step = result.Step,
            wiped = result.Wiped,
            resumeUrl = result.ResumeUrl
        };

        private static bool TryParseResetScope(string? raw, out EditorDraftResetScope scope)
        {
            scope = EditorDraftResetScope.FullProject;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return raw.Trim().ToLowerInvariant() switch
            {
                "stepback" or "step_back" => Assign(EditorDraftResetScope.StepBack, out scope),
                "backtowriter" or "back_to_writer" or "back" => Assign(EditorDraftResetScope.BackToWriter, out scope),
                "fullproject" or "full_project" or "reset" or "startover" or "start_over" => Assign(EditorDraftResetScope.FullProject, out scope),
                "fullprojectwithchapters" or "full_with_chapters" or "nuclear" => Assign(EditorDraftResetScope.FullProjectWithChapters, out scope),
                _ => false
            };
        }

        private static bool Assign(EditorDraftResetScope value, out EditorDraftResetScope scope)
        {
            scope = value;
            return true;
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
            var user = await ResolveProfileUserAsync();
            if (user == null)
            {
                var userEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
                user = await _context.Users
                    .Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.UserEmail == userEmail);
            }
            else
            {
                await _context.Entry(user).Reference(u => u.Role).LoadAsync();
            }

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

            // Profile settings (stored in Settings for backward compatibility / no schema migration)
            var settingsPrefix = $"profile:";
            var profileSettings = await _context.Settings.AsNoTracking()
                .Where(s =>
                    s.Key == $"profile:username:{user.UserId}" ||
                    s.Key == $"profile:country:{user.UserId}" ||
                    s.Key == $"profile:bio:{user.UserId}" ||
                    s.Key == $"profile:background:{user.UserId}")
                .ToListAsync();

            string? GetProfileSetting(string name)
            {
                var key = $"{settingsPrefix}{name}:{user.UserId}";
                return profileSettings.FirstOrDefault(s => s.Key == key)?.Value;
            }

            var profileUsername = GetProfileSetting("username") ?? "";
            var profileCountry = string.IsNullOrWhiteSpace(GetProfileSetting("country")) ? "United States" : (GetProfileSetting("country") ?? "United States");
            var profileBio = GetProfileSetting("bio") ?? "";
            var profileBackgroundPath = GetProfileSetting("background") ?? "";

            // Compute profile metrics from DB where possible
            var userBooks = await _context.Books.Where(b => b.UserId == user.UserId).ToListAsync();
            var totalBooks = userBooks.Count;
            var booksRead = userBooks.Count(b => BookFlowStateService.IsPublishedStatus(b.Status));
            var booksReading = userBooks.Count(b => !BookFlowStateService.IsPublishedStatus(b.Status));

            // Create view model
            var viewModel = new DashboardProfileViewModel
            {
                UserName = user.FullName,
                UserEmail = user.UserEmail,
                UserRole = user.Role?.RoleName ?? "Reader",
                MemberSince = user.CreatedAt,
                Country = profileCountry,
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

            // Optional profile values/account type (kept backward compatible without schema changes)
            ViewBag.ProfileUsername = profileUsername;
            ViewBag.ProfileBio = profileBio;
            ViewBag.AccountType = activePlan?.Plan?.PlanName ?? "Free";

            // Pass profile picture path to view
            ViewBag.ProfilePicturePath = user.ProfilePicturePath;
            ViewBag.ProfileBackgroundPath = profileBackgroundPath;
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
                var user = await ResolveProfileUserAsync();
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found. Please sign in again." });
                }

                if (request == null)
                {
                    return Json(new { success = false, message = "Invalid request." });
                }

                if (string.IsNullOrWhiteSpace(request.FullName))
                {
                    return Json(new { success = false, message = "Full name is required." });
                }

                var normalizedFullName = Regex.Replace(request.FullName.Trim(), @"\s{2,}", " ");
                if (normalizedFullName.Length < 2 || normalizedFullName.Length > 80)
                {
                    return Json(new { success = false, message = "Full name must be between 2 and 80 characters." });
                }
                if (!Regex.IsMatch(normalizedFullName, @"^[a-zA-Z0-9\s\.\-']+$"))
                {
                    return Json(new { success = false, message = "Full name contains invalid characters." });
                }
                user.FullName = normalizedFullName;

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

                // Store username/country/bio in Settings table for backward compatibility
                if (!string.IsNullOrWhiteSpace(request.Username))
                {
                    var username = request.Username.Trim();
                    if (username.Length < 3 || username.Length > 32)
                    {
                        return Json(new { success = false, message = "Username must be between 3 and 32 characters." });
                    }
                    if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_.-]+$"))
                    {
                        return Json(new { success = false, message = "Username can contain only letters, numbers, dot, underscore, and hyphen." });
                    }
                    var usernameInUse = await _context.Settings.AsNoTracking().AnyAsync(s =>
                        s.Key.StartsWith("profile:username:") &&
                        s.Key != $"profile:username:{user.UserId}" &&
                        s.Value != null &&
                        s.Value == username);
                    if (usernameInUse)
                    {
                        return Json(new { success = false, message = "Username is already in use." });
                    }

                    await UpsertProfileSettingAsync(user.UserId, "username", username, "Public profile username", CancellationToken.None);
                }
                else
                {
                    await UpsertProfileSettingAsync(user.UserId, "username", null, "Public profile username", CancellationToken.None);
                }

                if (!string.IsNullOrWhiteSpace(request.Country))
                {
                    var country = request.Country.Trim();
                    if (country.Length > 60)
                    {
                        return Json(new { success = false, message = "Country must be 60 characters or fewer." });
                    }
                    if (!Regex.IsMatch(country, @"^[a-zA-Z\s\.\-']+$"))
                    {
                        return Json(new { success = false, message = "Country contains invalid characters." });
                    }
                    await UpsertProfileSettingAsync(user.UserId, "country", country, "Profile country", CancellationToken.None);
                }
                else
                {
                    await UpsertProfileSettingAsync(user.UserId, "country", null, "Profile country", CancellationToken.None);
                }

                if (!string.IsNullOrWhiteSpace(request.Bio))
                {
                    var bio = request.Bio.Trim();
                    if (bio.Length > 500)
                    {
                        return Json(new { success = false, message = "Bio must be 500 characters or fewer." });
                    }
                    await UpsertProfileSettingAsync(user.UserId, "bio", bio, "Profile biography", CancellationToken.None);
                }
                else
                {
                    await UpsertProfileSettingAsync(user.UserId, "bio", null, "Profile biography", CancellationToken.None);
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
                    email = user.UserEmail,
                    username = request.Username?.Trim() ?? "",
                    country = request.Country?.Trim() ?? "",
                    bio = request.Bio?.Trim() ?? ""
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
                var user = await ResolveProfileUserAsync();
                
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found. Please sign in again." });
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
                var user = await ResolveProfileUserAsync();
                
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found. Please sign in again." });
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("UploadProfileBackground")]
        public async Task<IActionResult> UploadProfileBackground(IFormFile backgroundImage)
        {
            try
            {
                var user = await ResolveProfileUserAsync();
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found. Please sign in again." });
                }

                if (backgroundImage == null || backgroundImage.Length == 0)
                {
                    return Json(new { success = false, message = "Please select a background image" });
                }

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
                var fileExtension = Path.GetExtension(backgroundImage.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    return Json(new { success = false, message = "Only JPG, JPEG, PNG, and WEBP files are allowed" });
                }

                const long maxFileSize = 5 * 1024 * 1024; // 5 MB
                if (backgroundImage.Length > maxFileSize)
                {
                    return Json(new { success = false, message = "Background image size must be less than 5 MB" });
                }

                var oldBackground = await GetProfileSettingValueAsync(user.UserId, "background", HttpContext.RequestAborted);
                if (!string.IsNullOrWhiteSpace(oldBackground))
                {
                    var oldFilePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldBackground.TrimStart('/'));
                    if (System.IO.File.Exists(oldFilePath))
                    {
                        System.IO.File.Delete(oldFilePath);
                    }
                }

                var fileName = $"profile_cover_{user.UserId}_{DateTime.UtcNow.Ticks}{fileExtension}";
                var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "UserImages", fileName);
                var relativePath = $"/UserImages/{fileName}";

                var directory = Path.GetDirectoryName(uploadPath);
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await using (var stream = new FileStream(uploadPath, FileMode.Create))
                {
                    await backgroundImage.CopyToAsync(stream);
                }

                await UpsertProfileSettingAsync(user.UserId, "background", relativePath, "Profile background image", HttpContext.RequestAborted);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Background image updated successfully", imagePath = relativePath });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error uploading background image: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("RemoveProfileBackground")]
        public async Task<IActionResult> RemoveProfileBackground()
        {
            try
            {
                var user = await ResolveProfileUserAsync();
                if (user == null)
                {
                    return Json(new { success = false, message = "User not found. Please sign in again." });
                }

                var existingBackground = await GetProfileSettingValueAsync(user.UserId, "background", HttpContext.RequestAborted);
                if (!string.IsNullOrWhiteSpace(existingBackground))
                {
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", existingBackground.TrimStart('/'));
                    if (System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
                    }
                }

                await UpsertProfileSettingAsync(user.UserId, "background", null, "Profile background image", HttpContext.RequestAborted);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Background image removed successfully" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error removing background image: {ex.Message}" });
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
                return RedirectToAction("UserLogin", "Account");
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
                return RedirectToAction("UserLogin", "Account");
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
                return RedirectToAction("UserLogin", "Account");
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
                return RedirectToAction("UserLogin", "Account");
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
            var user = await _currentUser.GetUserAsync();
            
            if (user == null)
            {
                return RedirectToAction("UserLogin", "Account");
            }

            // My Books library — published titles only (drafts resume from Dashboard modal)
            var bookEntities = await _context.Books
                .Where(b => b.UserId == user.UserId)
                .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .ThenByDescending(b => b.CreatedAt)
                .ToListAsync();

            bookEntities = bookEntities
                .Where(b => BookFlowStateService.IsPublishedStatus(b.Status))
                .ToList();

            var bookIds = bookEntities.Select(b => b.BookId).ToList();
            var aiCoverByBookId = new Dictionary<int, string>();
            if (bookIds.Count > 0)
            {
                try
                {
                    var coverKeys = bookIds.SelectMany(id => new[]
                    {
                        $"book:{id}:printReadyCoverFront",
                        $"book:{id}:aiCoverLastPreview"
                    }).ToList();
                    var coverRows = await _context.Settings.AsNoTracking()
                        .Where(s => coverKeys.Contains(s.Key))
                        .ToListAsync();
                    var frontCoverByBookId = new Dictionary<int, string>();
                    foreach (var row in coverRows)
                    {
                        var parts = row.Key.Split(':');
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var bid) || string.IsNullOrWhiteSpace(row.Value))
                            continue;
                        var val = row.Value.Trim();
                        if (row.Key.EndsWith(":printReadyCoverFront", StringComparison.Ordinal))
                            frontCoverByBookId[bid] = val;
                        else
                            aiCoverByBookId[bid] = val;
                    }
                    foreach (var kv in frontCoverByBookId)
                        aiCoverByBookId[kv.Key] = kv.Value;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "MyBooks: ai cover settings skipped for user {UserId}", user.UserId);
                }
            }

            string ResolveBookCover(int bookId, string? coverPath)
            {
                aiCoverByBookId.TryGetValue(bookId, out var ai);
                return BookCoverResolver.ResolveMyBooksCoverUrl(coverPath, ai);
            }

            var userBooks = bookEntities.Select(b => new
            {
                b.BookId,
                b.Title,
                b.Status,
                b.CreatedAt,
                b.UpdatedAt,
                CoverImagePath = ResolveBookCover(b.BookId, b.CoverImagePath),
                b.Description,
                b.Genre,
                b.WordCount
            }).ToList();

            bookIds = userBooks.Select(b => b.BookId).ToList();
            var exportableChaptersByBookId = bookIds.ToDictionary(id => id, _ => 0);
            if (bookIds.Count > 0)
            {
                try
                {
                    var chapterCounts = await _context.Chapters.AsNoTracking()
                        .Where(c => bookIds.Contains(c.BookId) && c.Content != null && c.Content != "")
                        .GroupBy(c => c.BookId)
                        .Select(g => new { BookId = g.Key, Count = g.Count() })
                        .ToListAsync();

                    foreach (var row in chapterCounts)
                        exportableChaptersByBookId[row.BookId] = row.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "MyBooks: exportable chapter counts query failed for user {UserId}", user.UserId);
                }
            }

            var books = userBooks.Select(b =>
            {
                dynamic item = new ExpandoObject();
                item.BookId = b.BookId;
                item.Title = b.Title;
                item.Status = b.Status;
                item.CreatedAt = b.CreatedAt;
                item.UpdatedAt = b.UpdatedAt;
                item.ChapterCount = exportableChaptersByBookId.GetValueOrDefault(b.BookId, 0);
                item.CoverImagePath = b.CoverImagePath;
                item.Description = b.Description;
                item.Genre = b.Genre;
                item.WordCount = b.WordCount;
                return (dynamic)item;
            }).ToList();

            ViewBag.Books = books;
            ViewBag.UserId = user.UserId;
            ViewBag.AuthorName = user.FullName ?? user.UserEmail ?? "Author";
            ViewBag.StripePaymentsReady =
                !string.IsNullOrWhiteSpace(StripeKeys.Publishable(_configuration))
                && !string.IsNullOrWhiteSpace(StripeKeys.Secret(_configuration));
            ViewBag.PaymentJustCompleted = string.Equals(Request.Query["payment"], "success", StringComparison.OrdinalIgnoreCase);
            ViewBag.SelectedBookId = HttpContext.Session.GetInt32("LastSelectedBookId")
                ?? (int.TryParse(Request.Query["bookId"], out var qBid) && qBid > 0 ? qBid : null);

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
                return RedirectToAction("UserLogin", "Account");
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
                return RedirectToAction("UserLogin", "Account");
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

        private async Task<Users?> ResolveProfileUserAsync()
        {
            var user = await _currentUser.GetUserAsync();
            if (user != null)
                return user;

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId is > 0)
            {
                var fromSession = await _context.Users.FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value);
                if (fromSession != null)
                    return fromSession;
            }

            var userEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "";
            if (!string.IsNullOrWhiteSpace(userEmail))
                return await _context.Users.FirstOrDefaultAsync(u => u.UserEmail == userEmail);

            return null;
        }

        private static string BuildProfileSettingKey(int userId, string name)
        {
            return $"profile:{name}:{userId}";
        }

        private async Task<string?> GetProfileSettingValueAsync(int userId, string name, CancellationToken cancellationToken = default)
        {
            var key = BuildProfileSettingKey(userId, name);
            return await _context.Settings.AsNoTracking()
                .Where(s => s.Key == key)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private async Task UpsertProfileSettingAsync(int userId, string name, string? value, string description, CancellationToken cancellationToken = default)
        {
            var key = BuildProfileSettingKey(userId, name);
            var existing = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (existing != null)
                {
                    _context.Settings.Remove(existing);
                }
                return;
            }

            var clampedValue = EBookDashboard.Models.Settings.ClampValueLength(
                value.Trim(),
                EBookDashboard.Models.Settings.DbCompatMaxValueLength);
            if (existing == null)
            {
                var nextId = await _context.NextSettingIdAsync(cancellationToken);
                _context.Settings.Add(new Settings
                {
                    SettingId = nextId,
                    Key = key,
                    Value = clampedValue,
                    Category = "Profile",
                    Description = description,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.Value = clampedValue;
                existing.Category = "Profile";
                existing.Description = description;
                existing.UpdatedAt = DateTime.UtcNow;
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
                return RedirectToAction("UserLogin", "Account");
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
    public string Country { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
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