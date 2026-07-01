using EBookDashboard.Infrastructure;
using EBookDashboard.Models.Options;
using EBookDashboard.Services.BookApi;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.ViewModels;
using EBookDashboard.Services;
using static EBookDashboard.Services.CoverExternalApiHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.Cmp;
using Org.BouncyCastle.Asn1.Ocsp;
using PuppeteerSharp;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection.Metadata;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;  
using static System.Runtime.InteropServices.JavaScript.JSType;
using Newtonsoft.Json.Linq;

namespace EBookDashboard.Controllers
{
    [Authorize]
    //[Route("[controller]/[action]")]
    public class BooksController : Controller
    {
        private readonly IBookApiClient _bookApiClient;
        private readonly IOptionsSnapshot<ExternalApiOptions> _externalApiOptions;
        private readonly HttpClient _httpClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IBookService _bookService;
        private readonly IAPIRawResponseService _rawResponseService;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BooksController> _logger;
        private readonly IChapterIterationService _chapterIterationService;
        private readonly IBookPdfService _bookPdfService;
        private readonly IEpubExportService _epubExportService;
        private readonly IDocxExportService _docxExportService;
        private readonly IBookPageMetricsService _bookPageMetricsService;
        private readonly BookPublishReadinessService _publishReadiness;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly BookFlowStateService _bookFlow;
        private readonly IUpstreamQueueProbe _queueProbe;
        private readonly IBookGeneratorService _bookGeneratorService;

        public BooksController(
            IBookService bookService,
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IBookApiClient bookApiClient,
            IUpstreamQueueProbe queueProbe,
            IOptionsSnapshot<ExternalApiOptions> externalApiOptions,
            IAPIRawResponseService rawResponseService,
            IConfiguration configuration,
            ILogger<BooksController> logger,
            IChapterIterationService chapterIterationService,
            IBookPdfService bookPdfService,
            IEpubExportService epubExportService,
            IDocxExportService docxExportService,
            IBookPageMetricsService bookPageMetricsService,
            BookPublishReadinessService publishReadiness,
            IWebHostEnvironment hostEnvironment,
            BookFlowStateService bookFlow,
            IBookGeneratorService bookGeneratorService)
        {
            _httpClientFactory = httpClientFactory;
            _bookApiClient = bookApiClient;
            _queueProbe = queueProbe;
            _externalApiOptions = externalApiOptions;
            _httpClient = httpClientFactory.CreateClient();
            _bookService = bookService;
            _rawResponseService = rawResponseService;
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _chapterIterationService = chapterIterationService;
            _bookPdfService = bookPdfService;
            _epubExportService = epubExportService;
            _docxExportService = docxExportService;
            _bookPageMetricsService = bookPageMetricsService;
            _publishReadiness = publishReadiness;
            _hostEnvironment = hostEnvironment;
            _bookFlow = bookFlow;
            _bookGeneratorService = bookGeneratorService;
        }
        //===========================================
        //           On Page Load 
        // ✅ 1️⃣ — GET: Show the Razor view page
        //============================================
        [HttpGet]
        public async Task<IActionResult> AIGenerateBook(int? bookId = null)
        {
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                // not logged in → redirect to login
                return RedirectToAction("UserLogin", "Account");
            }
            if (!bookId.HasValue || bookId.Value <= 0)
            {
                TempData["InfoMessage"] = "Select a book from the Dashboard to continue your project.";
                return RedirectToAction("Index", "Dashboard");
            }
            var ownsBook = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId.Value && b.UserId == userId.Value);
            if (!ownsBook)
            {
                TempData["InfoMessage"] = "That book was not found. Choose a project from the Dashboard.";
                return RedirectToAction("Index", "Dashboard");
            }
            // Published books remain fully editable — the author can revisit AI Writer, re-edit
            // chapters and re-publish. (Previously this redirected published books to Publish.)
            var entryBookId = HttpContext.Session.GetInt32(BookFlowStateService.SessionEntryBookIdKey);
            if (!entryBookId.HasValue || entryBookId.Value != bookId.Value)
            {
                // Allow direct navigation after Create Book or deep links when the user owns this book.
                HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId.Value);
                HttpContext.Session.SetInt32("LastSelectedBookId", bookId.Value);
            }
            try
            {
                await SetActiveBookAsync(userId.Value, bookId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AIGenerateBook: SetActiveBookAsync failed for user {UserId}, book {BookId}. Page will still load.", userId.Value, bookId.Value);
            }
            await _bookFlow.SaveStepAsync(bookId.Value, BookFlowStateService.StepGenerate);
            var bookTitle = await _context.Books.AsNoTracking()
                .Where(b => b.BookId == bookId.Value)
                .Select(b => b.Title)
                .FirstOrDefaultAsync();
            var displayTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                _context, userId.Value, bookId.Value, bookTitle);
            ViewBag.UserId = userId;
            ViewBag.SelectedBookId = bookId;
            ViewBag.SelectedBookTitle = displayTitle;
            ViewBag.FlowBookId = bookId.Value;
            ViewBag.FlowStep = BookFlowStateService.StepGenerate;
            ViewBag.FlowBackUrl = "/Dashboard";

            List<BookDropdownItem> userBooks = new();
            try
            {
                userBooks = await _context.Books
                    .AsNoTracking()
                    .Where(b => b.UserId == userId.Value)
                    .OrderByDescending(b => b.CreatedAt)
                    .Select(b => new BookDropdownItem
                    {
                        BookId = b.BookId,
                        Title = string.IsNullOrWhiteSpace(b.Title) ? "Untitled" : b.Title
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AIGenerateBook: Books list query failed for user {UserId}.", userId.Value);
            }

            List<Plans> plans = new();
            try
            {
                plans = await _context.Plans
                    .AsNoTracking()
                    .OrderBy(p => p.PlanId)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AIGenerateBook: Plans query failed (check MySQL table name/casing and migrations).");
            }

            var model = new AIGenerateBookViewModel
            {
                BookRequest = new AIBookRequest(),
                AvailablePlans = plans,
                UserBooks = userBooks
            };
            // Browser fetch() abort budget — must be ≥ typical chapter generation + Polly pipeline (see BookApiLong total timeout).
            var fetchMins = int.TryParse(_configuration["ChapterGeneration:BrowserFetchTimeoutMinutes"], out var fm) ? fm : 55;
            fetchMins = Math.Clamp(fetchMins, 5, 180);
            ViewBag.ChapterGenerateFetchTimeoutMs = fetchMins * 60 * 1000;
            var typicalMins = int.TryParse(_configuration["ChapterGeneration:TypicalGenerationMinutes"], out var tm) ? tm : 5;
            typicalMins = Math.Clamp(typicalMins, 1, 30);
            ViewBag.ChapterGenerateEstimatedSeconds = typicalMins * 60;
            var exportOpt = await LoadExportOptionsForBookAsync(userId.Value, bookId.Value, CancellationToken.None);
            ViewBag.InteriorThemeCss = InteriorLayoutTokens.BuildFormatterSyncCss(exportOpt)
                + InteriorExportTheme.BuildAiWriterThemeBridgeCss(exportOpt);
            return View(model);
        }

        //// ✅ 1️⃣ — GET: Show the Razor view page ----1
        //[HttpGet]
        //public IActionResult AIGenerateBook()
        //{
        //    int? userId = HttpContext.Session.GetInt32("UserId");
        //    if (userId == null)
        //    {
        //        // not logged in → redirect to login
        //        return RedirectToAction("UserLogin", "Account");
        //    }
        //    ViewBag.UserId = userId; // ✅ send to Razor view
        //    return View(); // this will look for Views/Books/AIGenerateBook.cshtml
        //}
        //=======================================
        //     Chapter List View (Table UI)
        //=======================================
        public async Task<IActionResult> BookChaptersList(int bookId)
        {
            if (bookId == 0)
                return BadRequest("BookId is required");

            int? userId = HttpContext.Session.GetInt32("UserId");

            if (!userId.HasValue)
            {
                // Not logged in → redirect to login
                return RedirectToAction("UserLogin", "Account");
            }
            // 1️⃣ Get Book
            var book = await _context.Books
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId.Value);

            if (book == null)
                return NotFound("Book not found");

            // 2️⃣ Get Chapters
            var chapters = await _rawResponseService
                .GetChaptersByBookAsync(bookId, userId.Value);

            // 3️⃣ Build ViewModel
            var vm = new BookChaptersViewModel
            {
                BookId = book.BookId,
                Title = book.Title,
                Subtitle = book.Subtitle,
                Description = book.Description,
                Chapters = chapters
            };
            //Since your action name and view name are the same, you can simplify
            // View name matches Razor file: Views/Books/BookChaptersList.cshtml

            return View(vm);

        }
        // =============================================
        //   Single Chapter Reading Page
        // =============================================
        public async Task<IActionResult> Read(int id)
        {
            var chapter = await _rawResponseService.GetChapterAsync(id);

            if (chapter == null)
                return NotFound();

            return View(chapter);
        }

        //=======================================================
        // Get Selected Chapter's Content for a user and book
        //=======================================================
        [HttpGet]
        public async Task<IActionResult> GetBookChapterResponse(int userId, int bookId, int chapterNo)
        {
            try
            {

                if (userId == 0 || bookId == 0)
                {
                    return Json(new { success = false, message = "User ID and Book ID are required" });
                }
                var result = await _bookService.GetBookDetailsAsync2(userId, bookId, chapterNo);
                //var response = await _bookService.GetSelectedBookResponseAsync(userId, bookId, chapterNo);
                if (result == null)
                    return Json(new { success = false, message = "No response found." });

                if (!result.Success)
                {
                    Console.WriteLine($"❌ [Controller] Error loading book details: {result.Message}");
                    return Json(new { success = false, message = result.Message });
                }

                Console.WriteLine($"✅ [Controller] Successfully loaded book: {result.BookTitle} with {result.TotalChapters} chapters");
                // Return the result in the expected JSON format
                return Json(new
                {
                    success = true,
                    bookId = result.BookId,
                    bookTitle = result.BookTitle,
                    description = result.Description,
                    genre = result.Genre,
                    totalChapters = result.TotalChapters,
                    chapters = result.Chapters.Select(c => new
                    {
                        responseId = c.ResponseId,
                        chapterNo = c.ChapterNumber,
                        chapterTitle = c.Title,
                        requestData = c.RequestData,
                        content = c.Content,
                        statusCode = c.StatusCode
                    }).ToList()
                });
                }
              catch (Exception ex)
              {
                  Console.WriteLine($"❌ [Controller] Error in GetBookDetails: {ex.Message}");
                  return Json(new { success = false, message = ex.Message});
               }
        }

        //=======================================================
        // Get full book content (all chapters) for page-flip preview
        //=======================================================
        [HttpGet]
        public async Task<IActionResult> GetFullBookContent(int bookId, int userId = 0)
        {
            try
            {
                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (sessionUserId == null || sessionUserId.Value <= 0)
                    return Json(new { success = false, message = "Please sign in." });

                if (bookId <= 0)
                    return Json(new { success = false, message = "Book ID is required." });

                var effectiveUserId = sessionUserId.Value;

                var result = await _bookService.GetBookDetailsForPreviewAsync(effectiveUserId, bookId);
                if (result == null || !result.Success)
                    return Json(new { success = false, message = result?.Message ?? "No book found." });

                var exportOpt = await LoadExportOptionsForBookAsync(effectiveUserId, bookId, CancellationToken.None);
                var interiorCss = InteriorLayoutTokens.BuildFormatterSyncCss(exportOpt)
                    + InteriorExportTheme.BuildAiWriterThemeBridgeCss(exportOpt);

                return Json(new
                {
                    success = true,
                    bookId = result.BookId,
                    bookTitle = result.BookTitle,
                    subtitle = result.Subtitle ?? "",
                    description = result.Description ?? "",
                    genre = result.Genre ?? "",
                    authorName = result.AuthorName ?? "",
                    coverImagePath = result.CoverImagePath ?? "",
                    totalChapters = result.TotalChapters,
                    formatting = new
                    {
                        interiorStyle = exportOpt.InteriorStyle ?? "Novel",
                        textSize = exportOpt.TextSize ?? "Medium",
                        lineSpacing = exportOpt.LineSpacing ?? "1.6",
                        pageBackgroundColor = exportOpt.PageBackgroundColor ?? "",
                        previewAccent = exportOpt.PreviewAccent ?? ""
                    },
                    interiorCss,
                    chapters = result.Chapters.OrderBy(c => c.ChapterNumber).Select(c => new
                    {
                        chapterNo = c.ChapterNumber,
                        chapterTitle = c.Title,
                        content = c.Content ?? ""
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetFullBookContent error");
                return Json(new { success = false, message = ex.Message });
            }
        }
        
        //=======================================
        // Get a single saved API response for a user and book
        //====================================
        [HttpGet]
        public async Task<IActionResult> GetBookLastResponse(int userId, int bookId)
        {
            // var result = await _bookService.GetBookApiResponseAsync(userId, bookId);
            var response = await _bookService.GetLatestBookResponseAsync(userId, bookId);
            if (response == null)
                return Json(new { success = false, message = "No response found." });

            return Json(new
            {
                success = true,
                responseId = response.ResponseId,
                data = response.ResponseData
            });
        }
        //=======================================
        // Get a single saved API response for a user and book
        //====================================
        // 🟢 Get categories and load the create book view
        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.Categories
                .Select(c => new { c.CategoryId, c.CategoryName })
                .ToListAsync();

            ViewBag.Categories = categories;
            return View();
        }
        //==============================================
        //    Create a new book entry in the database
        //==============================================
        [HttpGet]
        public async Task<IActionResult> CreateBook(int userId = 0)
        {
            try
            {
                // Load categories using service
                var categories = await _bookService.GetAllCategoriesAsync();
                ViewBag.Categories = categories;

                // Get user info: prefer query param, fall back to session
                if (userId <= 0)
                {
                    userId = HttpContext.Session.GetInt32("UserId") ?? 0;
                }
                ViewBag.UserId = userId;
                //ViewBag.AuthorId = GetCurrentAuthorId();

                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading CreateBook page");
                ViewBag.Error = "Unable to load categories. Please try again.";
                return View();
            }
        }
        // POST: /Books/CreateBook
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBook([FromBody] CreateBookRequest request)
        {
            try
            {
                if (request == null)
                {
                    return BadRequest(new { success = false, message = "Invalid request data." });
                }

                // Basic validation - allow optional title and category (e.g. when creating from AIGenerateBook modal)
                if (string.IsNullOrWhiteSpace(request.Title))
                    request.Title = "Untitled Book";
                else
                    request.Title = request.Title.Trim();
                if (request.CategoryId <= 0)
                    request.CategoryId = 1;

                // Use service to create book - this should now work
                var book = await _bookService.CreateBookFromRequestAsync(request);

                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (sessionUserId.HasValue && sessionUserId.Value == book.UserId)
                {
                    HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, book.BookId);
                    HttpContext.Session.SetInt32("LastSelectedBookId", book.BookId);
                    try
                    {
                        await _bookFlow.SaveStepAsync(book.BookId, BookFlowStateService.StepGenerate);
                    }
                    catch (Exception flowEx)
                    {
                        _logger.LogWarning(flowEx, "CreateBook: flow step save failed for book {BookId}", book.BookId);
                    }
                }

                return Ok(new
                {
                    success = true,
                    message = "Book created successfully!",
                    bookId = book.BookId
                });
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, "Database error creating book for user {UserId}", request?.UserId);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Database error occurred while creating book."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating book for user {UserId}", request?.UserId);
                return StatusCode(500, new
                {
                    success = false,
                    message = "An unexpected error occurred while creating book."
                });
            }
        }

        /// <summary>
        /// Upload a picture for a book and save the path in the Books table.
        /// Call from CreateBook.cshtml (e.g. openFileUpload) with form field "profilePicture" and optional "bookId".
        /// If bookId is provided, updates that book's CoverImagePath; otherwise returns imagePath for use when creating the book.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadBookPicture(IFormFile profilePicture, int? bookId = null)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null || userId.Value <= 0)
                return Json(new { success = false, message = "User not authenticated." });

            if (profilePicture == null || profilePicture.Length == 0)
                return Json(new { success = false, message = "Please select an image file." });

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var ext = Path.GetExtension(profilePicture.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                return Json(new { success = false, message = "Only JPG, JPEG, and PNG files are allowed." });

            const long maxSize = 2 * 1024 * 1024; // 2 MB
            if (profilePicture.Length > maxSize)
                return Json(new { success = false, message = "File size must be less than 2 MB." });

            var sessionUserId = userId.Value.ToString();
            var bookFolder = bookId.HasValue && bookId.Value > 0 ? bookId.Value.ToString() : "temp";
            var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", sessionUserId, "books", bookFolder);
            Directory.CreateDirectory(uploadsRoot);
            var fileName = $"{Guid.NewGuid()}{ext}";
            var savePath = Path.Combine(uploadsRoot, fileName);
            var relativePath = $"/uploads/{sessionUserId}/books/{bookFolder}/{fileName}";

            using (var stream = new FileStream(savePath, FileMode.Create))
            {
                await profilePicture.CopyToAsync(stream);
            }

            if (bookId.HasValue && bookId.Value > 0)
            {
                var updated = await _bookService.UpdateBookCoverImagePathAsync(bookId.Value, userId.Value, relativePath);
                if (!updated)
                    return Json(new { success = false, message = "Book not found or could not update." });
            }

            return Json(new { success = true, message = "Picture saved.", imagePath = relativePath });
        }

        // Helper methods to get current user/author from session or claims
        public async Task<bool> UpdateBookAsync(Books book)
        {
            book.UpdatedAt = DateTime.UtcNow;
            _context.Books.Update(book);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteBookAsync(int bookId)
        {
            var book = await _context.Books.FindAsync(bookId);
            if (book == null) return false;

            _context.Books.Remove(book);
            return await _context.SaveChangesAsync() > 0;
        }
        //==================================
        //     Generate Book
        //==================================
        /// <summary>
        /// Parses POST JSON using Newtonsoft/JToken so both PascalCase (browser) and snake_case (external API shape)
        /// bind correctly. [FromBody] + System.Text.Json ignored Newtonsoft [JsonProperty] on <see cref="AIBookRequest"/>,
        /// which broke chapter generation when keys did not match exactly.
        /// </summary>
        private AIBookRequest? ParseAIBookRequestFromBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;
            try
            {
                var jo = JObject.Parse(body);

                var chTok = jo["Chapter"] ?? jo["chapter"];
                var chapterVal = 0;
                if (chTok != null && chTok.Type != JTokenType.Null)
                {
                    if (chTok.Type == JTokenType.Integer)
                        chapterVal = chTok.Value<int>();
                    else if (chTok.Type == JTokenType.Float)
                        chapterVal = (int)Math.Round(chTok.Value<double>());
                    else if (chTok.Type == JTokenType.String && int.TryParse(chTok.Value<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ci))
                        chapterVal = ci;
                }

                var pvTok = jo["PreviewOnly"] ?? jo["preview_only"];
                var previewVal = false;
                if (pvTok != null && pvTok.Type != JTokenType.Null)
                {
                    if (pvTok.Type == JTokenType.Boolean)
                        previewVal = pvTok.Value<bool>();
                    else if (pvTok.Type == JTokenType.String && bool.TryParse(pvTok.Value<string>(), out var pb))
                        previewVal = pb;
                    else if (pvTok.Type == JTokenType.Integer)
                        previewVal = pvTok.Value<int>() != 0;
                }

                return new AIBookRequest
                {
                    ResponseId = jo["response_id"]?.ToString() ?? jo["ResponseId"]?.ToString() ?? string.Empty,
                    UserId = jo["UserId"]?.ToString() ?? jo["user_id"]?.ToString(),
                    BookId = jo["BookId"]?.ToString() ?? jo["book_id"]?.ToString(),
                    Title = jo["Title"]?.ToString() ?? jo["title"]?.ToString() ?? string.Empty,
                    Chapter = chapterVal,
                    UserInput = jo["UserInput"]?.ToString() ?? jo["user_input"]?.ToString() ?? string.Empty,
                    ChapterTopic = jo["ChapterTopic"]?.ToString() ?? jo["chapter_topic"]?.ToString() ?? string.Empty,
                    PreviewOnly = previewVal
                };
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "AIGenerateBook: JSON parse failed");
                return null;
            }
        }

        // ✅ 2️⃣ — POST: Call external API and return book data as JSON
        // Generate Book via API
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [DisableRequestTimeout]
        [Route("Books/AIGenerateBook")]
        public async Task<IActionResult> AIGenerateBook()
        {
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                body = await reader.ReadToEndAsync();

            var model = ParseAIBookRequestFromBody(body);
            if (model == null)
                return Json(new { error = true, message = "Invalid request data — empty body or invalid JSON." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (!sessionUserId.HasValue || sessionUserId.Value <= 0)
                return Json(new { error = true, message = "Please sign in again, then retry chapter generation." });
            model.UserId = sessionUserId.Value.ToString(CultureInfo.InvariantCulture);
            if (int.TryParse(model.BookId, out var genBookId) && genBookId > 0)
            {
                var owns = await _context.Books.AsNoTracking()
                    .AnyAsync(b => b.BookId == genBookId && b.UserId == sessionUserId.Value);
                if (!owns)
                    return Json(new { error = true, message = "Book not found. Open your project from the Dashboard and try again." });
            }

            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.GenerateUrl, "/api/generate_chapter");
            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out _))
            {
                _logger.LogError("AIGenerateBook: invalid absolute upstream URL: {Url}", apiUrl);
                return Json(new { error = true, message = "Server misconfiguration: ExternalApi generate URL is not a valid absolute URL.", detail = apiUrl });
            }
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { error = true, message = ExternalApiKeyResolver.MissingKeyUserMessage });

            var queueSnapshot = await _queueProbe.TryGetSnapshotAsync(CancellationToken.None);
            var queueBlock = UpstreamQueueGuard.GetBlockReason(queueSnapshot, _configuration);
            if (!string.IsNullOrEmpty(queueBlock))
            {
                _logger.LogWarning("AIGenerateBook blocked: {Reason}", queueBlock);
                return Json(new { error = true, message = queueBlock, queueStuck = queueSnapshot?.IsStuck == true });
            }

            if (queueSnapshot?.IsStuck == true)
                _logger.LogWarning("AIGenerateBook: upstream queue reports waiting={Waiting} running={Running} — request may take a long time.",
                    queueSnapshot.Waiting, queueSnapshot.Running);

            var responseData = string.Empty;
            int? rawResponseId = null;
            try
            {
                var userBrief = ChapterPromptComposer.NormalizeUserBrief(
                    !string.IsNullOrWhiteSpace(model.ChapterTopic) ? model.ChapterTopic : model.UserInput);
                model.UserInput = userBrief;
                model.ChapterTopic = userBrief;

                var continuityPrefix = string.Empty;
                if (int.TryParse(model.BookId, out var continuityBookId) && continuityBookId > 0 && model.Chapter > 1)
                {
                    continuityPrefix = await ChapterPromptComposer.BuildContinuityPrefixAsync(
                        _context, continuityBookId, model.Chapter, CancellationToken.None);
                }

                var augmentedInput = ChapterPromptComposer.BuildAugmentedUserInput(userBrief, continuityPrefix);
                var client = _bookApiClient;
                var apiPayload = GenerateChapterPayloadBuilder.BuildUpstreamGeneratePayload(model, augmentedInput);
                var json = JsonConvert.SerializeObject(apiPayload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = content };

                _logger.LogInformation("Sending generate_chapter to upstream (payload length {Len}).", json.Length);
                using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
                using var response = await client.SendAsync(httpRequest, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
                responseData = await response.Content.ReadAsStringAsync(upstreamCts.Token);

                    // Log the API response in VS Output or console
                    Console.WriteLine($"📥API Response Status: {response.StatusCode}");
                    Console.WriteLine($"📥API Response Data: {responseData}");

                    // ✅ SAVE RAW RESPONSE FIRST (never fail the user response if DB/audit save fails)
                    try
                    {
                        rawResponseId = await _rawResponseService.SaveRawResponseAsync(
                            model,
                            responseData,
                            apiUrl,
                            response.StatusCode.ToString()
                        );
                    }
                    catch (Exception saveEx)
                    {
                        _logger.LogWarning(saveEx, "SaveRawResponseAsync failed after successful upstream call; returning generation JSON to client.");
                    }

                    Console.WriteLine($"📥 API Response Status: {response.StatusCode}");
                    Console.WriteLine($"📥 Raw Response saved with ID: {rawResponseId}");


                if (!response.IsSuccessStatusCode)
                {
                    var shortDetail = responseData?.Length > 200 ? responseData.Substring(0, 200) + "..." : responseData;
                    var hint = (int)response.StatusCode == 401 || (int)response.StatusCode == 403
                        ? " Check ExternalApi__ApiKey on the server matches the upstream API key."
                        : "";
                    return Json(new { error = true, message = $"AI service returned {(int)response.StatusCode}.{hint}", detail = shortDetail });
                }
                    // Parse and save to database only when not preview-only (dashboard: save on Finalize)
                    if (!model.PreviewOnly)
                    {
                        try
                        {
                            var apiResponse = JsonConvert.DeserializeObject<AIBookResponse>(responseData);
                            if (apiResponse != null)
                            {
                                Console.WriteLine($"🔍 API Response has content, proceeding to save...");
                                await SaveBookToDatabase(model, apiResponse, rawResponseId);
                                Console.WriteLine("✅ Book successfully saved to database");
                            }
                            else
                            {
                                Console.WriteLine("⚠️ No chapters to save to database");
                            }
                        }
                        catch (Exception dbEx)
                        {
                            Console.WriteLine($"⚠️ Database save failed but returning API response: {dbEx.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("📋 PreviewOnly: returning content without saving to database.");
                    }

                    // Track each successful generation as its own iteration (chapter_iterations), including PreviewOnly —
                    // the writer always uses preview mode so drafts are not written to `chapters` until Finalize.
                    if (rawResponseId > 0)
                    {
                        try
                        {
                            // Do not tie to RequestAborted — browser/proxy disconnect (504) must not cancel draft bookkeeping.
                            await _chapterIterationService.RecordSuccessfulGenerationAsync(rawResponseId.Value, CancellationToken.None);
                            var promoteBookId = genBookId;
                            if (promoteBookId <= 0)
                            {
                                var savedRaw = await _context.APIRawResponse.AsNoTracking()
                                    .FirstOrDefaultAsync(r => r.ResponseId == rawResponseId.Value, CancellationToken.None);
                                promoteBookId = savedRaw?.BookId ?? 0;
                            }
                            if (promoteBookId > 0 && model.Chapter > 0)
                            {
                                await _chapterIterationService.PromoteAsCurrentVersionAsync(
                                    sessionUserId.Value,
                                    promoteBookId,
                                    model.Chapter,
                                    rawResponseId.Value,
                                    CancellationToken.None);
                            }
                        }
                        catch (Exception itEx)
                        {
                            _logger.LogWarning(itEx, "Chapter iteration not recorded — ensure chapter_iterations table exists (see DatabaseScripts/create_chapter_iterations_table.sql).");
                        }
                    }

                    if (rawResponseId.HasValue)
                        Response.Headers.Append("X-Saved-Response-Id", rawResponseId.Value.ToString(CultureInfo.InvariantCulture));

                    var normalized = UpstreamResponseParser.NormalizeChapterJson(responseData);
                    return Content(normalized ?? responseData, "application/json");
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                if (rawResponseId == null)
                    try { await _rawResponseService.SaveRawResponseAsync(model, responseData ?? "", apiUrl, "504", "Timeout waiting for generation API"); } catch { }
                Console.WriteLine($"❌ Generate chapter timeout: {ex.Message}");
                return Json(new { error = true, message = "The AI service did not respond in time. The upstream API at 162.229.248.26 may be busy — wait 2–3 minutes and try again, or contact support if this continues." });
            }
            catch (OperationCanceledException)
            {
                // Browser fetch aborted (our client timeout) or user navigated away — RequestAborted linked to SendAsync.
                _logger.LogInformation("AIGenerateBook cancelled (client disconnect or timeout).");
                return Json(new { error = true, message = "Generation was stopped (timeout or connection closed). Check your network and try again — your API usually finishes within a few minutes." });
            }
            catch (HttpRequestException ex)
            {
                // Save error response
                if (rawResponseId == null)
                {
                    await _rawResponseService.SaveRawResponseAsync(
                        model,
                        responseData,
                        apiUrl,
                        "500",
                        $"Network error: {ex.Message}"
                    );
                }

                Console.WriteLine($"❌ Network error: {ex.Message}");
                return Json(new { error = true, message = "Network error. Please try again." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AIGenerateBook failed");
                if (rawResponseId == null)
                    try { await _rawResponseService.SaveRawResponseAsync(model, responseData ?? "", apiUrl, "500", ex.Message); } catch { }
                Console.WriteLine($"❌ Generate chapter error: {ex.Message}");
                return Json(new { error = true, message = "Generation failed. Please try again.", detail = ex.Message });
            }
        }

        /// <summary>Legacy edit forwarder (query/form). Prefer <see cref="AIEditBook"/> or <see cref="EditChapter"/> with JSON body.</summary>
        [HttpPost]
        [Route("Books/EditChapterFromQuery")]
        public async Task<ActionResult> EditChapterFromQuery(string userId, string bookId, string chapter, string changes)
        {
            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.EditUrl, "/api/edit");
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { error = true, message = ExternalApiKeyResolver.MissingKeyUserMessage });

            var payload = new { user_id = userId, book_id = bookId, chapter, changes };
            var json = JsonConvert.SerializeObject(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
            using var response = await _bookApiClient.SendAsync(req, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
            var result = await response.Content.ReadAsStringAsync();
            return Content(result, "application/json");
        }

        //==================================
        //     Edit Book
        //==================================
        // ✅ 2️⃣ — POST: Call external API and return book data as JSON
        // Generate Book via API
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [DisableRequestTimeout]
        [Route("Books/AIEditBook")]
        public async Task<IActionResult> AIEditBook([FromBody] AIBookRequestEdit model)
        {
            if (model == null)
                return Json(new { error = true, message = "Invalid request data" });

            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.EditUrl, "/api/edit");
            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out _))
            {
                _logger.LogError("AIEditBook: invalid absolute upstream URL: {Url}", apiUrl);
                return Json(new { error = true, message = "Server misconfiguration: ExternalApi edit URL is not a valid absolute URL.", detail = apiUrl });
            }
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { error = true, message = ExternalApiKeyResolver.MissingKeyUserMessage });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (!sessionUserId.HasValue || sessionUserId.Value <= 0)
                return Json(new { error = true, message = "Please sign in again, then retry." });
            model.UserId = sessionUserId.Value.ToString(CultureInfo.InvariantCulture);

            var userId = model.UserId ?? "";
            var bookId = model.BookId ?? "";
            if (int.TryParse(bookId, out var editBookId) && editBookId > 0)
            {
                var owns = await _context.Books.AsNoTracking()
                    .AnyAsync(b => b.BookId == editBookId && b.UserId == sessionUserId.Value);
                if (!owns)
                    return Json(new { error = true, message = "Book not found. Open your project from the Dashboard." });
            }
            var chapter = model.Chapter;
            var changes = model.Changes ?? "";

            var payload = new JObject
            {
                ["user_id"] = userId,
                ["book_id"] = bookId,
                ["chapter"] = chapter.ToString(),
                ["changes"] = changes
            };
            var json = payload.ToString();
            string responseData = "";

            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var requestMsg = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = content };

                    using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
                    using var response = await _bookApiClient.SendAsync(requestMsg, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
                    responseData = await response.Content.ReadAsStringAsync(upstreamCts.Token);

                    try
                    {
                        var saveTask = _rawResponseService.SaveRawResponseEditAsync(model, responseData, apiUrl, response.StatusCode.ToString());
                        if (await Task.WhenAny(saveTask, Task.Delay(TimeSpan.FromSeconds(8))) == saveTask)
                            await saveTask;
                    }
                    catch (Exception saveEx) { _logger.LogWarning(saveEx, "Raw response save failed"); }

                    if (response.IsSuccessStatusCode)
                    {
                        var normalized = UpstreamResponseParser.NormalizeChapterJson(responseData);
                        return Content(normalized ?? responseData, "application/json");
                    }

                    if (attempt == 1 && (int)response.StatusCode >= 500) { await Task.Delay(1000); continue; }
                    return Json(new { error = true, message = $"API error: {response.StatusCode}", detail = responseData?.Length > 300 ? responseData.Substring(0, 300) + "..." : responseData });
                }
                catch (TaskCanceledException ex)
                {
                    _logger.LogWarning(ex, "Edit API timeout");
                    if (attempt == 2) return Json(new { error = true, message = "The edit request took too long. Please try again with shorter instructions, or retry in a few minutes." });
                    await Task.Delay(1000);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogWarning(ex, "Edit API network error");
                    try { await _rawResponseService.SaveRawResponseEditAsync(model, "Network error: " + ex.Message, apiUrl, "500", ex.Message); } catch { }
                    if (attempt == 2) return Json(new { error = true, message = "Network error. Please try again." });
                    await Task.Delay(1000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Edit API error");
                    try { await _rawResponseService.SaveRawResponseEditAsync(model, responseData, apiUrl, "500", ex.Message); } catch { }
                    if (attempt == 2) return Json(new { error = true, message = "Editing failed. Please try again." });
                    await Task.Delay(1000);
                }
            }

            return Json(new { error = true, message = "Editing failed. Please try again." });
        }
        //------------------------------------------
        // Upload Book Cover Page Picture
        //------------------------------------------
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
        // Default method to get next IDs
        [HttpGet]
        public async Task<IActionResult> GetNextIds()
        {
            var lastUserId = await _context.Users.OrderByDescending(u => u.UserId).Select(u => u.UserId).FirstOrDefaultAsync();
            var lastBookId = await _context.Books.OrderByDescending(b => b.BookId).Select(b => b.BookId).FirstOrDefaultAsync();

            var nextUserId = lastUserId + 1;
            var nextBookId = lastBookId + 1;

            return Json(new { nextUserId, nextBookId });
        }
        // ✅ NEW: Method to save book to database
        private async Task<bool> SaveBookToDatabase(AIBookRequest request, AIBookResponse apiResponse, int? rawResponseId = null)
        {
            Console.WriteLine("✅ Trying to save Book to database");

            if (apiResponse?.Data == null || string.IsNullOrEmpty(apiResponse.Data.Content))
            {
                Console.WriteLine("❌ No content data in API response to save");
                return false;
            }


            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Convert UserId from string to int safely — session/claim is authoritative (never default to user 1).
                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (!sessionUserId.HasValue || sessionUserId.Value <= 0)
                {
                    Console.WriteLine("❌ SaveBookToDatabase: no session user id");
                    return false;
                }

                int userId = sessionUserId.Value;
                if (int.TryParse(request.UserId, out var parsedUserId) && parsedUserId > 0 && parsedUserId != userId)
                {
                    Console.WriteLine($"⚠️ DEBUG: USER ID MISMATCH! Session: {userId}, Request: {parsedUserId}; using session.");
                }

                Console.WriteLine($"💾 Saving book to database - UserId: {userId}");

                // Create new Book
                var book = new Books
                {
                    Title = apiResponse.Title ?? $"Book - {request.UserInput}",
                    UserId = userId,
                    Status = "Generated",
                    CreatedAt = DateTime.UtcNow
                };
                Console.WriteLine($"🔍 Adding book to context: {book.Title}");
                _context.Books.Add(book);
                await _context.SaveChangesAsync();
                Console.WriteLine($"🔍 Book saved with ID: {book.BookId}");

                // Update raw response with the parsed book ID
                if (rawResponseId.HasValue)
                {
                    var rawResponse = await _context.APIRawResponse.FindAsync(rawResponseId.Value);
                    if (rawResponse != null)
                    {
                        rawResponse.ParsedBookId = book.BookId.ToString();
                        await _context.SaveChangesAsync();
                        Console.WriteLine($"🔍 Updated raw response {rawResponseId} with book ID: {book.BookId}");
                    }
                }
                // Save the raw content as a single chapter (for now)
                var chapter = new Chapters
                {
                    BookId = book.BookId,
                    Title = "Full Book Content",
                    Content = apiResponse.Data.Content, // Save the entire raw content
                    ChapterNumber = 1
                };

                _context.Chapters.Add(chapter);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                Console.WriteLine($"✅ SUCCESS: Book saved to database with ID: {book.BookId}");
                if (userId > 0)
                    HttpContext.Session.SetString("HasGeneratedBook", "1");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"❌ Error saving book to database: {ex.Message}");
                return false;
            }
        }
        //
        // ✅ NEW: Method to save book to database
        private async Task<bool> SaveEditBookToDatabase(AIBookRequestEdit request, AIBookResponse apiResponse, int? rawResponseId = null)
        {
            Console.WriteLine("✅ Trying to save Book to database");

            if (apiResponse?.Data == null || string.IsNullOrEmpty(apiResponse.Data.Content))
            {
                Console.WriteLine("❌ No content data in API response to save");
                return false;
            }


            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (!sessionUserId.HasValue || sessionUserId.Value <= 0)
                {
                    Console.WriteLine("❌ SaveEditBookToDatabase: no session user id");
                    return false;
                }

                int userId = sessionUserId.Value;
                if (int.TryParse(request.UserId, out var parsedUserId) && parsedUserId > 0 && parsedUserId != userId)
                {
                    Console.WriteLine($"⚠️ DEBUG: USER ID MISMATCH! Session: {userId}, Request: {parsedUserId}; using session.");
                }

                Console.WriteLine($"💾 Saving book to database - UserId: {userId}");
                Console.WriteLine($"💾 Saving book to database - UserId: {userId} (converted from: {request.UserId})");

                // Create new Book
                var book = new Books
                {
                    Title = apiResponse.Title ?? $"Book - {request.Changes}",
                    UserId = userId,
                    Status = "Generated",
                    CreatedAt = DateTime.UtcNow
                };
                Console.WriteLine($"🔍 Adding book to context: {book.Title}");
                _context.Books.Add(book);
                await _context.SaveChangesAsync();
                Console.WriteLine($"🔍 Book saved with ID: {book.BookId}");

                // Update raw response with the parsed book ID
                if (rawResponseId.HasValue)
                {
                    var rawResponse = await _context.APIRawResponse.FindAsync(rawResponseId.Value);
                    if (rawResponse != null)
                    {
                        rawResponse.ParsedBookId = book.BookId.ToString();
                        await _context.SaveChangesAsync();
                        Console.WriteLine($"🔍 Updated raw response {rawResponseId} with book ID: {book.BookId}");
                    }
                }
                // Save the raw content as a single chapter (for now)
                var chapter = new Chapters
                {
                    BookId = book.BookId,
                    Title = "Full Book Content",
                    Content = apiResponse.Data.Content, // Save the entire raw content
                    ChapterNumber = 1
                };

                _context.Chapters.Add(chapter);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                Console.WriteLine($"✅ SUCCESS: Book saved to database with ID: {book.BookId}");
                if (userId > 0)
                    HttpContext.Session.SetString("HasGeneratedBook", "1");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"❌ Error saving book to database: {ex.Message}");
                return false;
            }
        }

        // ✅ 3️⃣ — POST: Save book data from API response (called from frontend). Create new book or update existing when BookId provided.
        [HttpPost]
        [Route("Books/SaveBookFromAPI")]
        public async Task<IActionResult> SaveBookFromAPI([FromBody] APISaveBookRequest model)
        {
            try
            {
                if (model == null || string.IsNullOrEmpty(model.ApiRaw))
                    return BadRequest("No API response data found.");

                int userId = int.TryParse(model.UserId, out int uid) ? uid : 0;
                if (userId <= 0)
                    return BadRequest("Valid UserId is required.");

                // Parse JSON: support both AIBookResponse (generate) and edit response (may have data.content or content)
                var apiResponse = JsonConvert.DeserializeObject<AIBookResponse>(model.ApiRaw);
                string contentToSave = apiResponse?.Data?.Content;
                string bookTitle = apiResponse?.Title;
                if (string.IsNullOrEmpty(contentToSave) || string.IsNullOrEmpty(bookTitle))
                {
                    var jo = JObject.Parse(model.ApiRaw);
                    if (string.IsNullOrEmpty(contentToSave))
                        contentToSave = jo["data"]?["content"]?.ToString() ?? jo["content"]?.ToString() ?? jo["changes"]?.ToString();
                    if (string.IsNullOrEmpty(bookTitle))
                        bookTitle = jo["title"]?.ToString();
                }
                if (string.IsNullOrEmpty(contentToSave))
                    return BadRequest("Invalid or empty book data.");
                if (string.IsNullOrEmpty(bookTitle)) bookTitle = "Untitled Book";

                var chapters = ParseChaptersFromHtml(contentToSave);
                int existingBookId = int.TryParse(model.BookId, out int bid) ? bid : 0;

                if (existingBookId > 0)
                {
                    // Update existing book: replace chapters
                    var book = await _context.Books
                        .FirstOrDefaultAsync(b => b.BookId == existingBookId && b.UserId == userId);
                    if (book == null)
                        return NotFound("Book not found or access denied.");

                    book.Title = bookTitle ?? book.Title;
                    book.UpdatedAt = DateTime.UtcNow;
                    book.Status = "Saved";

                    var existingChapters = await _context.Chapters.Where(c => c.BookId == existingBookId).ToListAsync();
                    _context.Chapters.RemoveRange(existingChapters);
                    await _context.SaveChangesAsync();

                    int chapterNo = 1;
                    foreach (var ch in chapters)
                    {
                        _context.Chapters.Add(new Chapters
                        {
                            BookId = book.BookId,
                            Title = ch.Title,
                            Content = ch.Content,
                            ChapterNumber = chapterNo++,
                            LanguageId = 1,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            Status = "Saved"
                        });
                    }
                    await _context.SaveChangesAsync();
                    if (userId > 0)
                        HttpContext.Session.SetString("HasGeneratedBook", "1");
                    return Ok(new { success = true, message = $"Book '{book.Title}' updated with {chapters.Count} chapters.", bookId = book.BookId });
                }

                // Create new book
                var newBook = new Books
                {
                    Title = bookTitle,
                    UserId = userId,
                    Status = "Saved",
                    CreatedAt = DateTime.UtcNow
                };
                _context.Books.Add(newBook);
                await _context.SaveChangesAsync();

                int chapterNoNew = 1;
                foreach (var ch in chapters)
                {
                    _context.Chapters.Add(new Chapters
                    {
                        BookId = newBook.BookId,
                        Title = ch.Title,
                        Content = ch.Content,
                        ChapterNumber = chapterNoNew++,
                        LanguageId = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Status = "Saved"
                    });
                }
                await _context.SaveChangesAsync();
                if (userId > 0)
                    HttpContext.Session.SetString("HasGeneratedBook", "1");
                return Ok(new { success = true, message = $"Book '{newBook.Title}' saved with {chapters.Count} chapters.", bookId = newBook.BookId });
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ SaveBookFromAPI failed: " + ex.Message);
                return StatusCode(500, ex.Message);
            }
        }
        // ✅ Helper method to parse chapters from HTML content
        private List<(string Title, string Content)> ParseChaptersFromHtml(string htmlContent)
        {
            var chapters = new List<(string Title, string Content)>();

            // Split content based on <h2> tags (assuming each chapter starts with <h2>)
            var parts = System.Text.RegularExpressions.Regex.Split(htmlContent, @"<h2>|</h2>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            string currentTitle = null;
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                if (currentTitle == null)
                {
                    currentTitle = System.Net.WebUtility.HtmlDecode(part.Trim());
                }
                else
                {
                    chapters.Add((currentTitle, System.Net.WebUtility.HtmlDecode(part.Trim())));
                    currentTitle = null;
                }
            }

            return chapters;
        }
        /// <summary>
        /// Get list of saved responses for a given UserId and BookId
        /// </summary>
   /*     [HttpGet]
        public async Task<IActionResult> GetSavedResponsesByBook(int userId, int bookId)
        {
            var responses = await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    responseId = r.ResponseId,
                    createdAt = r.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                })
                .ToListAsync();

            return Json(responses);
        }*/
        /// <summary>
        /// Get a single saved Book by its ResponseId
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetResponseById(int id)
        {
            var response = await _context.APIRawResponse
                .FirstOrDefaultAsync(r => r.ResponseId == id);

            if (response == null)
                return NotFound(new { message = "Response not found" });

            return Json(new
            {
                responseId = response.ResponseId,
                userId = response.UserId ?? 0,
                bookId = response.BookId ?? 0,
                apiRaw = response.ResponseData ?? "{}",
                createdAt = response.CreatedAt.ToString("yyyy-MM-dd HH:mm") ?? "(no date)"
            });
        }
        // Load all saved Books for a user
        [HttpGet]
        public async Task<IActionResult> GetAllResponses(int userId)
        {
            var responses = await _context.APIRawResponse
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new
                {
                    r.ResponseId,
                    r.BookId,
                    r.UserId,
                    r.CreatedAt,
                    Title = _context.Books
                        .Where(b => b.BookId == r.BookId)
                        .Select(b => b.Title)
                        .FirstOrDefault() ?? "(Untitled Book)"
                })
                .ToListAsync();

            if (responses == null || !responses.Any())
                return Json(new { success = false, message = "No saved responses found." });

            return Json(new { success = true, data = responses });
        }
        //=====================================================
        //    Load Last Book Data for a User
        //=====================================================
        [HttpGet]
        public async Task<IActionResult> GetLastBookrawResponseData(int userId, int bookId, int responseId)
        {
            try
            {
                Console.WriteLine($"🔍 Loading book data for user {userId}, book {bookId}, response {responseId}");

                // First try to get data from Books table
                var bookDetails = await _bookService.GetBookDetailsAsync(userId, bookId);
                if (bookDetails != null)
                {
                    //Console.WriteLine($"✅ Found book in Books table: {bookDetails.Title}");
                    return Json(new
                    {
                        success = true,
                        bookId = bookDetails.BookId,
                        title = bookDetails.BookTitle,
                        description = bookDetails.Description,
                        genre = bookDetails.Genre,
                        chapters = bookDetails.Chapters,
                        data = bookDetails.Chapters?.FirstOrDefault()?.Content,
                        source = "BooksTable"
                    });
                }
                // // Get raw response data for the latest book
                // var latestBookData = await _rawResponseService.GetRawResponsesByUserAndBookAsync(userId, bookId, responseId);

                // If not found in Books table, try APIRawResponse
                Console.WriteLine($"📚 Book not found in Books table, checking APIRawResponse...");
                var rawResponses = await _rawResponseService.GetRawResponsesByUserAndBookAsync(userId, bookId, responseId);

                if (rawResponses != null && rawResponses.Any())
                {
                    var latestResponse = rawResponses.OrderByDescending(r => r.CreatedAt).First();
                    Console.WriteLine($"✅ Found {rawResponses.Count()} responses in APIRawResponse");

                    return Json(new
                    {
                        success = true,
                        bookId = bookId,
                        responseId = latestResponse.ResponseId,
                        title = latestResponse.Title,
                        data = latestResponse.ResponseData,
                        chapter = latestResponse.Chapter,
                        endpoint = latestResponse.Endpoint,
                        createdAt = latestResponse.CreatedAt,
                        rawResponses = rawResponses,
                        source = "APIRawResponse"
                    });
                }

                Console.WriteLine($"🔍 No book data found for user {userId}, book {bookId}");
                return Json(new
                {
                    success = false,
                    message = "No book data found for the selected book."
                });

            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error fetching book data for user {userId}: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }  
            
        }
        //==========================================================
        //============ Step 1: Load Books in DropDown ==============
        //==========================================================
        [HttpGet]
        public async Task<IActionResult> GetSavedResponses(int userId, int bookId)
        {
            if (userId==0)
                return BadRequest("UserId is required.");

            try
            {
                var savedBooks = await _bookService.GetSavedBooksForDropdownAsync(userId, bookId);

                // Transform to the expected format with formatted date
                var result = savedBooks.Select(b => new
                {
                    userId = b.UserId,
                    bookId = b.BookId,
                    bookTitle = b.BookTitle
                }).ToList();

                Console.WriteLine($"🔍 GetSavedResponses: Found {result.Count} books for user {userId}");
                return Json(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GetSavedResponses error: {ex.Message}");
                return Json(new { error = ex.Message });
            }
        }
        //==========================================================
        //========= Step 1: Load Chapter Nos in DropDown ===========
        //===       Load data from Books + APIRawResponse        ===
        //==========================================================  Done
        [HttpGet]
        public async Task<IActionResult> GetSavedChapterNos(int userId, int bookId)
        {
            if(bookId == 0)
                return BadRequest("BookId is required.");

            var savedBooks = await _bookService.GetSavedBooksChaptersAsync(userId,bookId);

            // Transform to the expected format with formatted date
            var result = savedBooks.Select(b => new
            {
                // Make sure this matches JS expectation
                //ResponseId=b.ResponseId,
                chapterNo = b.ChapterNumber,
               chapterTitle= b.Title,
               content= b.Content,
               chapterStatus=b.StatusCode,
               createdAt= b.CreatedAt
            }).ToList();

           return Json(result);
        }

        /// <summary>Chapter finalize progress for AI writer workflow (enables Book formatting when all are locked).</summary>
        [HttpGet]
        public async Task<IActionResult> GetBookWritingProgress(int userId, int bookId)
        {
            if (userId <= 0 || bookId <= 0)
                return Json(new { success = false, message = "Valid userId and bookId are required." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId && b.UserId == userId);
            if (!owns)
                return Json(new { success = false, message = "Book not found." });

            var saved = (await _bookService.GetSavedBooksChaptersAsync(userId, bookId)).ToList();
            var chapters = saved.Select(c =>
            {
                var status = (c.StatusCode ?? string.Empty).Trim();
                var finalized = string.Equals(status, "ReadOnly", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Finalized", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(status, "Final", StringComparison.OrdinalIgnoreCase);
                return new
                {
                    number = c.ChapterNumber,
                    title = c.Title ?? string.Empty,
                    topic = ChapterPromptComposer.ParseChapterTopicFromRequestData(c.RequestData),
                    isFinalized = finalized,
                    status = finalized ? "Finalized" : (string.IsNullOrEmpty(status) ? "Draft" : status),
                    displayLabel = ChapterPromptComposer.FormatChapterLabel(c.ChapterNumber, c.Title)
                };
            }).OrderBy(c => c.number).ToList();

            var total = chapters.Count;
            var finalizedCount = chapters.Count(c => c.isFinalized);
            return Json(new
            {
                success = true,
                bookId,
                totalChapters = total,
                finalizedChapters = finalizedCount,
                allFinalized = total > 0 && finalizedCount >= total,
                chapters
            });
        }

        /// <summary>
        /// Get book details with chapters
        /// </summary>
        //============================================== 100% OK
        // Load Selected Book from Drop-Down from Books table
        //==============================================
        [HttpGet]
        public async Task<IActionResult> GetBookDetails(int userId, int bookId)
        {
            try
            {
                Console.WriteLine($"🔍 Loading book details for User: {userId}, Book: {bookId}");

                if (userId == 0 || bookId == 0)
                {
                    return Json(new { success = false, message = "User ID and Book ID are required" });
                }

                // Get chapters from APIRawResponse with user filtering

                var result = await _bookService.GetBookDetailsAsync(userId, bookId);
                if (result == null)
                {
                    Console.WriteLine($"❌ [Controller] Book {bookId} not found for user {userId}");
                    return Json(new { success = false, message = "Book not found" });
                }
                if (!result.Success)
                {
                    Console.WriteLine($"❌ [Controller] Error loading book details: {result.Message}");
                    return Json(new { success = false, message = result.Message });
                }
                Console.WriteLine($"✅ [Controller] Successfully loaded book: {result.BookTitle} with {result.TotalChapters} chapters");
                // Return the result in the expected JSON format
                return Json(new
                {
                    success = true,
                    bookId = result.BookId,
                    bookTitle = result.BookTitle,
                    subtitle = result.Subtitle ?? "",
                    description = result.Description ?? "",
                    genre = result.Genre ?? "",
                    authorName = result.AuthorName ?? "",
                    coverImagePath = result.CoverImagePath ?? "",
                    totalChapters = result.TotalChapters,
                    chapters = result.Chapters.Select(c => new
                    {
                        responseId = c.ResponseId,
                        chapterNo = c.ChapterNumber,
                        chapterTitle = c.Title,
                        chapterTopic = ChapterPromptComposer.ParseChapterTopicFromRequestData(c.RequestData),
                        requestData = c.RequestData,
                        content = c.Content,
                        statusCode = c.StatusCode
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Controller] Error in GetBookDetails: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
        //============================================= 100% OK
        //   Get Specific Chapter Data
        //=============================================
        [HttpGet]
        public async Task<IActionResult> GetChapterData(int userId, int bookId, int chapterNo, int? responseId = null)
        {
            try
            {
                Console.WriteLine($"🔍 Loading book details for User: {userId}, Book: {bookId}");

                if (userId == 0 || bookId == 0)
                {
                    return Json(new { success = false, message = "User ID and Book ID are required" });
                }

                // Get chapters from APIRawResponse with user filtering

                var result = await _bookService.GetBookDetailsAsync2(userId, bookId, chapterNo, responseId);
                if (result == null)
                {
                    Console.WriteLine($"❌ [Controller] Book {bookId} not found for user {userId}");
                    return Json(new { success = false, message = "Book not found" });
                }
                if (!result.Success)
                {
                    Console.WriteLine($"❌ [Controller] Error loading book details: {result.Message}");
                    return Json(new { success = false, message = result.Message });
                }
                Console.WriteLine($"✅ [Controller] Successfully loaded book: {result.BookTitle} with {result.TotalChapters} chapters");
                // Return the result in the expected JSON format
                return Json(new
                {
                    success = true,
                    bookId = result.BookId,
                    bookTitle = result.BookTitle,
                    description = result.Description,
                    genre = result.Genre,
                    totalChapters = result.TotalChapters,
                    chapters = result.Chapters.Select(c => new
                    {
                        responseId = c.ResponseId,
                        chapterNo = c.ChapterNumber,
                        chapterTitle = c.Title,
                        chapterTopic = ChapterPromptComposer.ParseChapterTopicFromRequestData(c.RequestData),
                        requestData = c.RequestData,
                        content = c.Content,
                        statusCode = c.StatusCode
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Controller] Error in GetBookDetails: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Lists all draft/final iterations for one chapter (separate DB rows). Used by AI Writer version dropdown.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetChapterIterations(int userId, int bookId, int chapterNo)
        {
            if (userId <= 0 || bookId <= 0 || chapterNo <= 0)
                return Json(new { success = false, message = "Valid userId, bookId, and chapterNo are required." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId && b.UserId == userId);
            if (!owns)
                return Json(new { success = false, message = "Book not found." });

            try
            {
                var iterations = await _chapterIterationService.ListIterationsAsync(userId, bookId, chapterNo, HttpContext.RequestAborted);
                return Json(new { success = true, iterations });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetChapterIterations failed");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Promotes one chapter iteration (version) to the chapter's current/finalized version.
        /// Used by the AI Writer "Set as current version" action in Version history.
        /// </summary>
        [HttpPost]
        [Route("Books/PromoteChapterVersion")]
        public async Task<IActionResult> PromoteChapterVersion([FromBody] PromoteChapterVersionRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0 || req.ChapterNo <= 0 || req.ResponseId <= 0)
                return BadRequest(new { success = false, message = "BookId, ChapterNo, and ResponseId are required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Unauthorized(new { success = false, message = "Please sign in." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == req.BookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (!owns)
                return NotFound(new { success = false, message = "Book not found." });

            try
            {
                var ok = await _chapterIterationService.FinalizeByResponseIdAsync(
                    sessionUserId.Value, req.BookId, req.ChapterNo, req.ResponseId, cancellationToken);
                if (!ok)
                    return Json(new { success = false, message = "Could not finalize this version. Generate or save the version first." });

                HttpContext.Session.SetString("HasGeneratedBook", "1");
                return Json(new { success = true, message = "Version finalized as current." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PromoteChapterVersion failed for book {BookId} chapter {Chapter}", req.BookId, req.ChapterNo);
                return Json(new { success = false, message = "Server error while finalizing the version." });
            }
        }

        /// <summary>Deletes one chapter and all related drafts, iterations, and finalized rows from AI Writer.</summary>
        [HttpPost]
        [Route("Books/DeleteChapter")]
        public async Task<IActionResult> DeleteWriterChapter([FromBody] DeleteWriterChapterRequest req, CancellationToken cancellationToken)
        {
            if (req == null || req.BookId <= 0 || req.ChapterNumber <= 0)
                return BadRequest(new { success = false, message = "BookId and ChapterNumber are required." });

            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Unauthorized(new { success = false, message = "Please sign in." });

            try
            {
                var (ok, message) = await _bookService.DeleteWriterChapterAsync(
                    sessionUserId.Value, req.BookId, req.ChapterNumber, cancellationToken);
                if (!ok)
                    return Json(new { success = false, message });

                return Json(new { success = true, message, chapterNumber = req.ChapterNumber });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeleteWriterChapter failed for book {BookId} chapter {Chapter}", req.BookId, req.ChapterNumber);
                return Json(new { success = false, message = "Could not delete chapter. Try again." });
            }
        }

        /// <summary>
        /// PDF built only from iterations marked finalized; includes generation/finalization timestamps per chapter.
        /// </summary>
        [HttpPost]
        [Route("Books/DownloadFinalizedChaptersPdf")]
        public async Task<IActionResult> DownloadFinalizedChaptersPdf([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
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

            var details = await _chapterIterationService.BuildPdfReadyFromFinalizedAsync(sessionUserId.Value, req.BookId, cancellationToken);
            if (details == null || !details.Success)
                return BadRequest(new { success = false, message = "No finalized chapters yet. On Book formatting, pick a draft version per chapter and use Finalize, then try again." });

            var orderedChapters = details.Chapters.OrderBy(c => c.ChapterNumber).ToList();
            if (orderedChapters.Count == 0 || !orderedChapters.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
                return BadRequest(new { success = false, message = "Finalized chapters have no exportable body text." });

            try
            {
                var exportOpt = await LoadExportOptionsForBookAsync(sessionUserId.Value, req.BookId, cancellationToken);
                exportOpt.ApplyRequestOverrides(req);

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
                var fileName = $"{safe}-finalized-{req.BookId}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DownloadFinalizedChaptersPdf failed for book {BookId}", req.BookId);
                return StatusCode(500, new { success = false, message = "PDF generation failed." });
            }
        }

        //==============================================
        // Get Next Chapter Number for a Book
        //==============================================
        [HttpGet]
        public async Task<IActionResult> GetNextChapterNumber(int userId, int bookId)
        {
            try
            {
                Console.WriteLine($"🔍 GetNextChapterNumber called for User: {userId}, Book: {bookId}");

                if (userId <= 0)
                {
                    Console.WriteLine($"❌ Invalid parameters: UserId={userId}, BookId={bookId}");
                    return Json(new { success = false, message = "Valid UserId is required." });
                }

                // bookId 0 = new book / no manuscript yet — next chapter is 1. Previously bookId<=0 failed validation and broke the new-draft flow in AIGenerateBook.cshtml.
                if (bookId <= 0)
                {
                    return Json(new
                    {
                        success = true,
                        nextChapterNumber = 1,
                        userId = userId,
                        bookId = bookId
                    });
                }

                var nextChapterNumber = await _bookService.GetNextChapterNumberAsync(userId, bookId);

                Console.WriteLine($"✅ Next chapter number: {nextChapterNumber} for User: {userId}, Book: {bookId}");

                return Json(new
                {
                    success = true,
                    nextChapterNumber = nextChapterNumber,
                    userId = userId,
                    bookId = bookId
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in GetNextChapterNumber: {ex.Message}");
                return Json(new
                {
                    success = false,
                    message = ex.Message,
                    nextChapterNumber = 1 // Fallback to 1
                });
            }
        }
        // GET: Books
        public async Task<IActionResult> Index()
        {
            var books = await _context.Books
                .Include(b => b.Chapters)
                .Include(b => b.BookPrice)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
            return View(books);
        }

        /// <summary>
        /// EBook Hub: list all books from Books table with author names from Users table.
        /// </summary>
        [HttpGet]
        [Route("Books/EBookHub")]
        public async Task<IActionResult> EBookHub()
        {
            var fallbackCovers = new[]
            {
                "/images/books/the-bird.png",
                "/images/books/good-things-are-up-ahead.png",
                "/images/books/fairy-tale.png",
                "/images/books/the-wizarding-chronicles.png",
                "/images/books/the-cambers-of-secrets.png",
                "https://images.unsplash.com/photo-1541963463532-d68292c34b19?q=80&w=1200&auto=format&fit=crop",
                "https://images.unsplash.com/photo-1512820790803-83ca734da794?q=80&w=1200&auto=format&fit=crop"
            };

            // Discovery catalog: all generated titles (non-empty title). Reading remains in My Books for owners.
            var rows = await (from b in _context.Books
                              join u in _context.Users on b.UserId equals u.UserId
                              where b.Title != null && b.Title != ""
                              orderby b.UpdatedAt descending, b.Title
                              select new
                              {
                                  b.BookId,
                                  b.UserId,
                                  b.Title,
                                  b.Description,
                                  b.WordCount,
                                  AuthorName = u.FullName,
                                  b.CoverImagePath,
                                  b.Genre
                              })
                .ToListAsync();

            var list = new List<EBookHubItemViewModel>(rows.Count);
            foreach (var r in rows)
            {
                var wordCount = r.WordCount;
                var description = (r.Description ?? "").Trim();

                if (wordCount <= 0 || string.IsNullOrWhiteSpace(description))
                {
                    try
                    {
                        var summary = await _bookService.GetManuscriptSummaryAsync(r.UserId, r.BookId);
                        if (wordCount <= 0 && summary.WordCount > 0)
                            wordCount = summary.WordCount;
                        if (string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(summary.Description))
                            description = summary.Description;

                        if ((r.WordCount <= 0 && wordCount > 0) || (string.IsNullOrWhiteSpace(r.Description) && !string.IsNullOrWhiteSpace(description)))
                            await _bookService.SyncBookMetadataFromManuscriptAsync(r.UserId, r.BookId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "EBookHub: manuscript summary for book {BookId}", r.BookId);
                    }
                }

                var resolvedCover = await ResolveBookCoverAsync(
                    r.CoverImagePath,
                    r.Title,
                    fallbackCovers[(r.BookId % fallbackCovers.Length + fallbackCovers.Length) % fallbackCovers.Length]);

                list.Add(new EBookHubItemViewModel
                {
                    BookId = r.BookId,
                    Title = r.Title,
                    Description = description,
                    WordCount = wordCount,
                    FullName = r.AuthorName,
                    CoverImagePath = resolvedCover,
                    Genre = r.Genre ?? ""
                });
            }
            return View(list);
        }

        /// <summary>Discovery modal: live description and word count from manuscript when DB fields are empty.</summary>
        [HttpGet]
        [Route("Books/BookHubDetails")]
        public async Task<IActionResult> BookHubDetails(int bookId, CancellationToken cancellationToken = default)
        {
            if (bookId <= 0)
                return Json(new { success = false, message = "Invalid book." });

            var book = await _context.Books.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == bookId, cancellationToken);
            if (book == null)
                return Json(new { success = false, message = "Book not found." });

            var author = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == book.UserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(cancellationToken) ?? "";

            var summary = await _bookService.GetManuscriptSummaryAsync(book.UserId, bookId);
            var description = summary.Description;
            var wordCount = summary.WordCount;

            if ((book.WordCount <= 0 && wordCount > 0) || (string.IsNullOrWhiteSpace(book.Description) && !string.IsNullOrWhiteSpace(description)))
                await _bookService.SyncBookMetadataFromManuscriptAsync(book.UserId, bookId);

            var cover = await ResolveBookCoverAsync(
                book.CoverImagePath,
                book.Title,
                "/images/books/the-bird.png");

            return Json(new
            {
                success = true,
                bookId,
                title = book.Title ?? "",
                author,
                description,
                wordCount,
                genre = book.Genre ?? "",
                cover
            });
        }

        private async Task<BookPdfExportOptions> LoadExportOptionsForBookAsync(int userId, int bookId, CancellationToken cancellationToken)
        {
            var draftRow = await _context.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:formattingDraft", cancellationToken);
            var fmtRow = await _context.BookFormatting.AsNoTracking()
                .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == userId, cancellationToken);
            return BookPdfExportOptions.LoadFromPersistence(fmtRow, draftRow?.Value);
        }

        private async Task<string> ResolveBookCoverAsync(string? existingCoverPath, string? title, string fallbackUrl)
        {
            if (!string.IsNullOrWhiteSpace(existingCoverPath))
                return existingCoverPath;

            var pixabay = await TryGetPixabayImageAsync(title);
            if (!string.IsNullOrWhiteSpace(pixabay))
                return pixabay;

            if (!string.IsNullOrWhiteSpace(title))
            {
                var t = WebUtility.UrlEncode(title + " book cover");
                return $"https://source.unsplash.com/600x900/?{t}";
            }

            return fallbackUrl;
        }

        private async Task<string?> TryGetPixabayImageAsync(string? title)
        {
            var apiKey = _configuration["Pixabay:ApiKey"];
            if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(title))
                return null;

            try
            {
                var q = WebUtility.UrlEncode(title + " book cover");
                var apiUrl = $"https://pixabay.com/api/?key={apiKey}&q={q}&image_type=photo&orientation=vertical&category=backgrounds&safesearch=true&per_page=3";
                var response = await _httpClient.GetAsync(apiUrl);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);
                var firstHit = obj["hits"]?.FirstOrDefault();
                var imageUrl = firstHit?["largeImageURL"]?.ToString()
                               ?? firstHit?["webformatURL"]?.ToString()
                               ?? firstHit?["previewURL"]?.ToString();

                return string.IsNullOrWhiteSpace(imageUrl) ? null : imageUrl;
            }
            catch
            {
                return null;
            }
        }

        // GET: Books/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var book = await _context.Books
                .Include(b => b.Chapters)
                .Include(b => b.BookPrice)
                .FirstOrDefaultAsync(b => b.BookId == id);
            if (book == null) return NotFound();
            var author = await _context.Users
                .Where(u => u.UserId == book.UserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync();
            ViewBag.AuthorName = author ?? "Unknown Author";
            return View(book);
        }

        // GET: Books/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Books/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Books book)
        {
            if (ModelState.IsValid)
            {
                book.CreatedAt = DateTime.UtcNow;
                book.Status = "Draft";
                _context.Books.Add(book);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(book);
        }

        // GET: Books/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null) return NotFound();
            // Set this book as active (Currently Working On) for the user
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId.HasValue)
            {
                await SetActiveBookAsync(userId.Value, id);
                HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, id);
            }
            return RedirectToAction("AIGenerateBook", "Books", new { bookId = id });
        }

        // POST: Books/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Books book)
        {
            if (id != book.BookId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    book.UpdatedAt = DateTime.UtcNow;
                    _context.Update(book);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BookExists(book.BookId))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(book);
        }

        // GET: Books/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var book = await _context.Books
                .Include(b => b.Chapters)
                .Include(b => b.BookPrice)
                .FirstOrDefaultAsync(b => b.BookId == id);
            if (book == null) return NotFound();
            return View(book);
        }

        // POST: Books/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return RedirectToAction("UserLogin", "Account");

            var wasNearEmptyUntitled = await BookDraftGuard.IsNearEmptyBookAsync(_context, sessionUserId.Value, id);

            var deleted = await _bookService.DeleteBookForUserAsync(id, sessionUserId.Value);
            if (!deleted)
                return NotFound();

            if (wasNearEmptyUntitled)
                await BookDraftGuard.PurgeAllNearEmptyUntitledAsync(_context, sessionUserId.Value);

            if (HttpContext.Session.GetInt32("LastSelectedBookId") == id)
                HttpContext.Session.Remove("LastSelectedBookId");
            if (HttpContext.Session.GetInt32(BookFlowStateService.SessionEntryBookIdKey) == id)
                HttpContext.Session.Remove(BookFlowStateService.SessionEntryBookIdKey);

            var lastBookKey = $"user:{sessionUserId.Value}:lastBookId";
            var lastUrlKey = $"user:{sessionUserId.Value}:lastBookWorkUrl";
            var lastBookRows = await _context.Settings
                .Where(s => s.Key == lastBookKey || s.Key == lastUrlKey)
                .ToListAsync();
            foreach (var row in lastBookRows)
            {
                if (row.Key == lastBookKey && int.TryParse(row.Value, out var savedId) && savedId == id)
                    _context.Settings.Remove(row);
                else if (row.Key == lastUrlKey
                         && BookResumeUrlHelper.TryParseBookIdFromWorkUrl(row.Value ?? "") == id)
                    _context.Settings.Remove(row);
            }
            if (lastBookRows.Count > 0)
                await _context.SaveChangesAsync();

            return RedirectToAction("Index", "Dashboard");
        }

        // POST: Books/Publish/5 — legacy; real publish happens on Dashboard Publish after export download.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (!sessionUserId.HasValue)
                return RedirectToAction("UserLogin", "Account");

            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == id && b.UserId == sessionUserId.Value);
            if (book != null)
            {
                return RedirectToAction("Publish", "Dashboard", new { bookId = id });
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task SetActiveBookAsync(int userId, int bookId)
        {
            // 1. Set all user's books to isActive=0 (clear previous active)
            await _context.Books
                .Where(b => b.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.isActive, 0));
            // 2. Set the selected book to isActive=1
            await _context.Books
                .Where(b => b.UserId == userId && b.BookId == bookId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.isActive, 1));
        }

        // POST: Books/Archive/5 
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book != null)
            {
                book.Status = "Archived";
                book.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool BookExists(int id)
        {
            return _context.Books.Any(e => e.BookId == id);
        }
        // Additional actions for managing chapters can be added here
        [AllowAnonymous]
        [HttpPost]
        [DisableRequestTimeout]
        [Route("Books/EditChapter")]
        public async Task<IActionResult> EditChapter([FromBody] APIEditChapterRequest model)
        {
            if (model == null)
                return BadRequest(new { success = false, error = true, message = "Invalid request payload." });
            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.EditUrl, "/api/edit").Trim();
            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out _))
            {
                _logger.LogError("EditChapter (body): invalid absolute upstream URL: {Url}", apiUrl);
                return BadRequest(new { success = false, message = "Server misconfiguration: ExternalApi edit URL is not a valid absolute URL.", detail = apiUrl });
            }
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);

            string responseData = string.Empty;
            int? rawResponseId = null;

            try
            {
                if (string.IsNullOrEmpty(apiKey))
                    return BadRequest(new { success = false, message = ExternalApiKeyResolver.MissingKeyUserMessage });

                var json = JsonConvert.SerializeObject(model);
                using var httpRequestEdit = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                Console.WriteLine($"📤Forwarding edit request to API: {json}");

                using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
                using var response = await _bookApiClient.SendAsync(httpRequestEdit, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
                responseData = await response.Content.ReadAsStringAsync(upstreamCts.Token);

                // Save raw response for audit (do not fail the client if this throws)
                try
                {
                    rawResponseId = await _rawResponseService.SaveRawResponseAsync(
                        // reuse AIBookRequest-like object for logging; create minimal AIBookRequest
                        new AIBookRequest
                        {
                            UserId = model.UserId,
                            BookId = model.BookId,
                            Chapter = int.TryParse(model.Chapter, out var c) ? c : 0,
                            UserInput = model.Changes
                        },
                        responseData,
                        apiUrl,
                        response.StatusCode.ToString()
                    );
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "EditChapter: SaveRawResponseAsync failed; continuing with upstream body.");
                }

                Console.WriteLine($"📥 Edit API response status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var statusMessage = $"Edit API error: {(int)response.StatusCode}";
                    return StatusCode((int)response.StatusCode, new
                    {
                        success = false,
                        error = true,
                        message = statusMessage,
                        detail = responseData?.Length > 300 ? responseData.Substring(0, 300) + "..." : responseData
                    });
                }
                // Optionally parse and persist edited content into Chapters table
                string newContent = string.Empty;
                JObject? parsedJson = null;
                try
                {
                    parsedJson = JsonConvert.DeserializeObject<JObject>(responseData);
                    newContent = parsedJson?["data"]?["content"]?.ToString() ?? parsedJson?["content"]?.ToString() ?? string.Empty;

                    if (!string.IsNullOrEmpty(newContent) && int.TryParse(model.BookId, out int bookId))
                    {
                        int chapterNum = int.TryParse(model.Chapter, out var ch) ? ch : 0;
                        var chapter = await _context.Chapters.FirstOrDefaultAsync(c => c.BookId == bookId && c.ChapterNumber == chapterNum);

                        if (chapter != null)
                        {
                            chapter.Content = System.Net.WebUtility.HtmlDecode(newContent);
                            chapter.UpdatedAt = DateTime.UtcNow;
                            _context.Chapters.Update(chapter);
                            await _context.SaveChangesAsync();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log parsing/persistence error but still return api response
                    Console.WriteLine($"⚠️ Unable to persist edited chapter: {ex.Message}");
                }
                return Json(new
                {
                    success = true,
                    responseId = rawResponseId,
                    content = newContent,
                    data = parsedJson ?? new JObject()
                });
            }
            catch (Exception ex)
            {
                // Save error (if not saved already)
                if (rawResponseId == null)
                {
                    await _rawResponseService.SaveRawResponseAsync(
                        new AIBookRequest { UserId = model.UserId, BookId = model.BookId, Chapter = int.TryParse(model.Chapter, out var c) ? c : 0, UserInput = model.Changes },
                        responseData,
                        apiUrl,
                        "500",
                        $"Forwarding error: {ex.Message}"
                    );
                }

                Console.WriteLine($"❌ EditChapter Exception: {ex.Message}");
                return StatusCode(500, new { success = false, error = true, message = "Editing failed on server.", detail = ex.Message });
            }
        }

        //====================== Change Chapter Content ======================
        [HttpPost]
        public async Task<IActionResult> ChangeChapterContent([FromBody] APIChangeChapterModel model)
        {
            if (model == null)
                return BadRequest("Invalid data.");

            // Step 1: Send user request to your external API
            // ✅ Read from appsettings.json
            //var apiUrl = "http://162.229.248.26:8001/api/changecontent";

            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.EditUrl, "/api/edit").Trim();
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, message = ExternalApiKeyResolver.MissingKeyUserMessage });

            var payload = new
            {
                user_id = model.UserId,
                book_id = model.BookId,
                chapter = model.Chapter,
                user_input = model.NewContent
            };

            var json = JsonConvert.SerializeObject(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
            using var response = await _bookApiClient.SendAsync(req, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
            var responseData = await response.Content.ReadAsStringAsync();

            // Step 2: Parse response JSON
            var parsedJson = JsonConvert.DeserializeObject<JObject>(responseData);

            // ✅ Step 3: Insert your code snippet HERE
            var newContent = parsedJson?["data"]?["content"]?.ToString() ?? parsedJson?["content"]?.ToString();

            int bid = int.TryParse(model.BookId, out var bookIdVal) ? bookIdVal : 0;
            int chapterNum = int.TryParse(model.Chapter, out var chVal) ? chVal : 0;

            var chapter = await _context.Chapters.FirstOrDefaultAsync(c =>
                    c.BookId == bid && c.ChapterNumber == chapterNum);

            if (chapter != null)
            {
                chapter.Content = newContent;
                chapter.UpdatedAt = DateTime.UtcNow;
                _context.Chapters.Update(chapter);
                await _context.SaveChangesAsync();
            }

            // Step 4: Return success response to UI
            return Json(new { success = true, data = parsedJson });
        }

        /// <summary>
        /// Saves chapter content directly to the database (no external API).
        /// Used when user finalizes edits from the chapter preview.
        /// </summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveChapterContent([FromBody] APIChangeChapterModel model)
        {
            static string TruncateTitle(string? t, int max = 200)
            {
                if (string.IsNullOrWhiteSpace(t)) return string.Empty;
                var s = t.Trim();
                return s.Length <= max ? s : s.Substring(0, max);
            }

            try
            {
                if (model == null || string.IsNullOrEmpty(model.BookId) || string.IsNullOrEmpty(model.Chapter))
                    return BadRequest(new { success = false, message = "Invalid data — missing book or chapter." });

                if (!int.TryParse(model.UserId, out int userId) || !int.TryParse(model.BookId, out int bookId) || !int.TryParse(model.Chapter, out int chapterNum))
                    return BadRequest(new { success = false, message = "Invalid UserId, BookId, or Chapter." });

                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (sessionUserId.HasValue && sessionUserId.Value != userId)
                    return Unauthorized(new { success = false, message = "Session user does not match request. Refresh the page and sign in again." });

                var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
                if (book == null)
                    return NotFound(new { success = false, message = "Book not found or access denied." });

                if (model.BookTitleOnly)
                {
                    if (!string.IsNullOrWhiteSpace(model.BookTitle))
                        await BookTitleResolver.SyncBookTitleAsync(_context, userId, bookId, model.BookTitle);
                    await _context.Entry(book).ReloadAsync();
                    return Json(new
                    {
                        success = true,
                        message = "Book title saved.",
                        bookTitle = book.Title,
                        bookTitleOnly = true
                    });
                }

                if (!string.IsNullOrWhiteSpace(model.BookTitle))
                    await BookTitleResolver.SyncBookTitleAsync(_context, userId, bookId, model.BookTitle);

                var titleTrim = TruncateTitle(model.ChapterTitle);
                if (string.IsNullOrEmpty(titleTrim))
                    titleTrim = $"Chapter {chapterNum}";

                var chapter = await _context.Chapters.FirstOrDefaultAsync(c => c.BookId == bookId && c.ChapterNumber == chapterNum);

                var isReadOnly = chapter != null &&
                    string.Equals(chapter.Status, "ReadOnly", StringComparison.OrdinalIgnoreCase);
                if (!isReadOnly)
                {
                    var latestRaw = await _context.APIRawResponse
                        .Where(r => r.UserId == userId && r.BookId == bookId && r.Chapter == chapterNum)
                        .OrderByDescending(r => r.CreatedAt)
                        .FirstOrDefaultAsync();
                    isReadOnly = latestRaw != null &&
                        string.Equals(latestRaw.StatusCode, "ReadOnly", StringComparison.OrdinalIgnoreCase);
                }

                if (isReadOnly)
                {
                    if (!model.TitleOnly)
                        return BadRequest(new { success = false, message = "Finalized chapters cannot be edited. You can rename the chapter title only." });

                    if (chapter != null)
                    {
                        chapter.Title = titleTrim;
                        chapter.UpdatedAt = DateTime.UtcNow;
                        chapter.UpdatedByUserId = userId;
                        _context.Chapters.Update(chapter);
                    }

                    var rawResponses = await _context.APIRawResponse
                        .Where(r => r.UserId == userId && r.BookId == bookId && r.Chapter == chapterNum)
                        .ToListAsync();
                    var titleForRaw = TruncateTitle(model.ChapterTitle, 500);
                    foreach (var raw in rawResponses)
                    {
                        raw.Title = titleForRaw;
                        raw.UpdatedAt = DateTime.UtcNow;
                    }

                    var finalizedIter = await _context.ChapterIterations
                        .Where(i => i.UserId == userId && i.BookId == bookId && i.ChapterNumber == chapterNum && i.IsFinalized)
                        .FirstOrDefaultAsync();
                    if (finalizedIter != null)
                        finalizedIter.Title = TruncateTitle(model.ChapterTitle, 500);

                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Chapter title updated.", titleOnly = true });
                }

                if (chapter != null)
                {
                    chapter.Content = model.NewContent ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(model.ChapterTitle))
                        chapter.Title = titleTrim;
                    chapter.UpdatedAt = DateTime.UtcNow;
                    chapter.UpdatedByUserId = userId;
                    _context.Chapters.Update(chapter);
                }
                else
                {
                    _context.Chapters.Add(new Chapters
                    {
                        BookId = bookId,
                        ChapterNumber = chapterNum,
                        SrNo = chapterNum,
                        OrderIndex = chapterNum,
                        Title = titleTrim,
                        Content = model.NewContent ?? string.Empty,
                        LanguageId = 1,
                        Status = "Draft",
                        UpdatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await _context.SaveChangesAsync();

                int? newResponseId = null;
                if (model.RecordVersion)
                {
                    try
                    {
                        var titleForVersion = !string.IsNullOrWhiteSpace(model.ChapterTitle)
                            ? TruncateTitle(model.ChapterTitle, 500)
                            : (chapter?.Title ?? titleTrim);
                        newResponseId = await _chapterIterationService.RecordUserContentVersionAsync(
                            userId,
                            bookId,
                            chapterNum,
                            titleForVersion,
                            model.NewContent ?? string.Empty,
                            model.Topic,
                            "manual-save",
                            HttpContext.RequestAborted);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "RecordUserContentVersionAsync after SaveChapterContent failed");
                    }
                }

                await _context.Entry(book).ReloadAsync();
                return Json(new
                {
                    success = true,
                    message = "Chapter updated successfully.",
                    responseId = newResponseId,
                    bookTitle = book.Title
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveChapterContent failed");
                return StatusCode(500, new { success = false, message = "Could not save chapter: " + ex.Message });
            }
        }

        /// <summary>Rename a chapter title and persist to MySQL (chapters + related metadata).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateChapterName([FromBody] UpdateChapterNameRequest request)
        {
            static string TruncateTitle(string? t, int max = 200)
            {
                if (string.IsNullOrWhiteSpace(t)) return string.Empty;
                var s = t.Trim();
                return s.Length <= max ? s : s.Substring(0, max);
            }

            try
            {
                if (request == null || request.BookId <= 0 || request.ChapterNumber <= 0)
                    return BadRequest(new { success = false, message = "Invalid book or chapter." });

                var titleTrim = TruncateTitle(request.Title);
                if (string.IsNullOrWhiteSpace(titleTrim))
                    return BadRequest(new { success = false, message = "Chapter name cannot be empty." });

                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (!sessionUserId.HasValue || sessionUserId.Value <= 0)
                    return Unauthorized(new { success = false, message = "Please sign in." });

                var userId = sessionUserId.Value;
                var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == request.BookId && b.UserId == userId);
                if (book == null)
                    return NotFound(new { success = false, message = "Book not found or access denied." });

                var chapterNum = request.ChapterNumber;
                var chapter = await _context.Chapters.FirstOrDefaultAsync(c => c.BookId == request.BookId && c.ChapterNumber == chapterNum);
                if (chapter != null)
                {
                    chapter.Title = titleTrim;
                    chapter.UpdatedAt = DateTime.UtcNow;
                    chapter.UpdatedByUserId = userId;
                    _context.Chapters.Update(chapter);
                }
                else
                {
                    _context.Chapters.Add(new Chapters
                    {
                        BookId = request.BookId,
                        ChapterNumber = chapterNum,
                        SrNo = chapterNum,
                        OrderIndex = chapterNum,
                        Title = titleTrim,
                        Content = string.Empty,
                        LanguageId = 1,
                        Status = "Draft",
                        UpdatedByUserId = userId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                var rawResponses = await _context.APIRawResponse
                    .Where(r => r.UserId == userId && r.BookId == request.BookId && r.Chapter == chapterNum)
                    .ToListAsync();
                var titleForRaw = TruncateTitle(request.Title, 500);
                foreach (var raw in rawResponses)
                {
                    raw.Title = titleForRaw;
                    raw.UpdatedAt = DateTime.UtcNow;
                }

                var titleForIter = TruncateTitle(request.Title, 500);
                var chapterIters = await _context.ChapterIterations
                    .Where(i => i.UserId == userId && i.BookId == request.BookId && i.ChapterNumber == chapterNum)
                    .ToListAsync();
                foreach (var iter in chapterIters)
                    iter.Title = titleForIter;

                await _context.SaveChangesAsync();
                return Json(new { success = true, title = titleTrim, message = "Chapter name updated." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UpdateChapterName failed for book {BookId} chapter {Chapter}", request?.BookId, request?.ChapterNumber);
                return StatusCode(500, new { success = false, message = "Could not update chapter name: " + ex.Message });
            }
        }

        /// <summary>Export book as valid EPUB (cover first, then chapters). For KDP / Ebook platforms.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Books/ExportEpub")]
        public async Task<IActionResult> ExportEpub([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
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
            if (details == null || !details.Success || details.Chapters == null || !details.Chapters.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
                return BadRequest(new { success = false, message = "No chapter content to export." });

            await _bookService.SyncBookMetadataFromManuscriptAsync(sessionUserId.Value, req.BookId);
            var exportSummary = await _bookService.GetManuscriptSummaryAsync(sessionUserId.Value, req.BookId);
            if (!string.IsNullOrWhiteSpace(exportSummary.Description))
                details.Description = exportSummary.Description;

            try
            {
                var coverKeys = new[]
                {
                    $"book:{req.BookId}:printReadyCoverFront",
                    $"book:{req.BookId}:aiCoverLastPreview",
                    $"book:{req.BookId}:printReadyCoverWrap",
                    $"book:{req.BookId}:printReadyPageCount",
                    $"book:{req.BookId}:printReadyTrimSize"
                };
                var coverRows = await _context.Settings.AsNoTracking()
                    .Where(s => coverKeys.Contains(s.Key))
                    .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);
                var bookRow = await _context.Books.AsNoTracking()
                    .Where(b => b.BookId == req.BookId)
                    .Select(b => new { b.CoverImagePath })
                    .FirstOrDefaultAsync(cancellationToken);

                var wrapRef = coverRows.GetValueOrDefault($"book:{req.BookId}:printReadyCoverWrap", "").Trim();
                var frontRef = BookCoverRefResolver.ResolveEbookFrontCoverRef(
                    coverRows.GetValueOrDefault($"book:{req.BookId}:printReadyCoverFront"),
                    coverRows.GetValueOrDefault($"book:{req.BookId}:aiCoverLastPreview"),
                    bookRow?.CoverImagePath,
                    wrapRef);
                var cover = !string.IsNullOrEmpty(frontRef)
                    ? BookCoverRefResolver.NormalizeCoverUrlRef(frontRef)
                    : (!string.IsNullOrEmpty(wrapRef)
                        ? BookCoverRefResolver.NormalizeCoverUrlRef(wrapRef)
                        : BookCoverRefResolver.NormalizeCoverUrlRef(req.CoverImageDataUrl ?? details.CoverImagePath));

                var exportOpt = await LoadExportOptionsForBookAsync(sessionUserId.Value, req.BookId, cancellationToken);
                exportOpt.ApplyRequestOverrides(req);
                exportOpt.Format = "Ebook";
                exportOpt.IncludeCoverPage = true;
                if (exportOpt.PublishingPlatform.Equals("Just Print Ready File", StringComparison.OrdinalIgnoreCase))
                    exportOpt.PublishingPlatform = "";

                var pageCountForCover = 0;
                if (int.TryParse(coverRows.GetValueOrDefault($"book:{req.BookId}:printReadyPageCount"), out var savedPages)
                    && savedPages > 0)
                    pageCountForCover = savedPages;
                else
                    pageCountForCover = _bookPageMetricsService.Estimate(details, exportOpt).PageCount;

                var trimSizeForCover = coverRows.GetValueOrDefault($"book:{req.BookId}:printReadyTrimSize", "").Trim();
                if (string.IsNullOrWhiteSpace(trimSizeForCover)) trimSizeForCover = "6 x 9 in";

                var bytes = await _epubExportService.BuildEpubAsync(
                    details,
                    cover,
                    (req.DisplayTitle ?? details.BookTitle ?? "").Trim(),
                    (req.DisplayAuthor ?? details.AuthorName ?? "").Trim(),
                    exportOpt,
                    pageCountForCover,
                    trimSizeForCover,
                    cancellationToken);

                var rawName = (req.DisplayTitle ?? details.BookTitle ?? "book").Trim();
                var safe = Regex.Replace(rawName, @"[^\w\-\s]", "");
                safe = Regex.Replace(safe, @"\s+", "-").Trim('-');
                if (string.IsNullOrEmpty(safe)) safe = "book";
                var epubFileName = $"{safe}-{req.BookId}.epub";

                try
                {
                    await SavePublishedEpubAsync(sessionUserId.Value, req.BookId, epubFileName, bytes, cancellationToken);
                    await _bookService.MarkPublishedAsync(req.BookId, sessionUserId.Value, cancellationToken);
                    await _bookFlow.SaveStepAsync(req.BookId, BookFlowStateService.StepPublish, exportOpt.Format?.Equals("Paperback", StringComparison.OrdinalIgnoreCase) == true ? "print" : "ebook", cancellationToken);
                }
                catch (Exception sideEffectEx)
                {
                    _logger.LogWarning(sideEffectEx, "Post-export bookkeeping failed for book {BookId}; EPUB download still succeeded.", req.BookId);
                }

                return File(bytes, "application/epub+zip", epubFileName);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExportEpub failed for book {BookId}", req.BookId);
                return StatusCode(500, new { success = false, message = "EPUB export failed." });
            }
        }

        /// <summary>Export book as Word document (.docx) for easy editing and EPUB conversion.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Books/ExportDocx")]
        public async Task<IActionResult> ExportDocx([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
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
            if (details == null || !details.Success || details.Chapters == null || !details.Chapters.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
                return BadRequest(new { success = false, message = "No chapter content to export." });

            try
            {
                var bytes = _docxExportService.BuildDocx(
                    details,
                    (req.DisplayTitle ?? details.BookTitle ?? "").Trim(),
                    (req.DisplayAuthor ?? details.AuthorName ?? "").Trim());

                var rawName = (req.DisplayTitle ?? details.BookTitle ?? "book").Trim();
                var safe = Regex.Replace(rawName, @"[^\w\-\s]", "");
                safe = Regex.Replace(safe, @"\s+", "-").Trim('-');
                if (string.IsNullOrEmpty(safe)) safe = "book";
                await _bookService.MarkPublishedAsync(req.BookId, sessionUserId.Value, cancellationToken);
                await _bookFlow.SaveStepAsync(req.BookId, BookFlowStateService.StepPublish, "ebook", cancellationToken);
                return File(bytes, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", $"{safe}-{req.BookId}.docx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExportDocx failed for book {BookId}", req.BookId);
                return StatusCode(500, new { success = false, message = "Word export failed." });
            }
        }

        /// <summary>Print-ready bundle: interior PDF (6×9) + full cover wrap ZIP in one download.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Books/ExportPrintReadyBundle")]
        public async Task<IActionResult> ExportPrintReadyBundle([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
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
                return BadRequest(new { success = false, message = "Could not load book content." });

            try
            {
                var draftRow = await _context.Settings.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == $"book:{req.BookId}:formattingDraft", cancellationToken);
                var fmtRow = await _context.BookFormatting.AsNoTracking()
                    .FirstOrDefaultAsync(f => f.BookId == req.BookId && f.UserId == sessionUserId.Value, cancellationToken);
                var exportOpt = BookPdfExportOptions.LoadFromPersistence(fmtRow, draftRow?.Value);
                exportOpt.ApplyRequestOverrides(req);
                exportOpt.Format = "Paperback";
                exportOpt.IncludeCoverPage = false;

                var userRow = await _context.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value, cancellationToken);
                var publisherLabel = userRow?.FullName;
                if (string.IsNullOrWhiteSpace(publisherLabel)) publisherLabel = userRow?.UserEmail;

                // Interior PDF only — cover ships as separate full-wrap PNG in the ZIP
                var pdfBytes = await _bookPdfService.RenderFullBookPdfAsync(
                    details,
                    null,
                    (req.DisplayTitle ?? details.BookTitle ?? "").Trim(),
                    (req.DisplayAuthor ?? details.AuthorName ?? "").Trim(),
                    (req.DisplayGenre ?? details.Genre ?? "").Trim(),
                    exportOpt,
                    publisherLabel,
                    cancellationToken);

                var wrapKey = $"book:{req.BookId}:printReadyCoverWrap";
                var wrapApiKey = $"book:{req.BookId}:printReadyCoverWrapApi";
                var wrapRows = await _context.Settings.AsNoTracking()
                    .Where(s => s.Key == wrapKey || s.Key == wrapApiKey)
                    .ToListAsync(cancellationToken);
                byte[]? wrapBytes = null;
                var wrapVal = (wrapRows.FirstOrDefault(s => s.Key == wrapKey)?.Value
                    ?? wrapRows.FirstOrDefault(s => s.Key == wrapApiKey)?.Value
                    ?? "").Trim();
                if (wrapVal.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                {
                    var ix = wrapVal.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                    if (ix >= 0) wrapBytes = Convert.FromBase64String(wrapVal[(ix + 7)..]);
                }
                else if (wrapVal.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || wrapVal.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var client = _httpClientFactory.CreateClient();
                        wrapBytes = await client.GetByteArrayAsync(wrapVal, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "ExportPrintReadyBundle wrap fetch failed for book {BookId}", req.BookId);
                    }
                }
                else if (wrapVal.StartsWith("/"))
                {
                    var physical = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", wrapVal.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(physical))
                        wrapBytes = await System.IO.File.ReadAllBytesAsync(physical, cancellationToken);
                }

                // "Both" (ebook + paperback): also ship the standalone ebook front cover image,
                // since the EPUB only embeds it and ebook stores (e.g. KDP) need it as a separate file.
                byte[]? frontBytes = null;
                var frontExt = "png";
                byte[]? epubBytes = null;
                var isBoth = string.Equals((req.BookFormat ?? "").Trim(), "Both", StringComparison.OrdinalIgnoreCase);
                if (isBoth)
                {
                    var frontKeys = new[]
                    {
                        $"book:{req.BookId}:printReadyCoverFront",
                        $"book:{req.BookId}:aiCoverLastPreview"
                    };
                    var frontRows = await _context.Settings.AsNoTracking()
                        .Where(s => frontKeys.Contains(s.Key))
                        .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);
                    var bookCoverRow = await _context.Books.AsNoTracking()
                        .Where(b => b.BookId == req.BookId)
                        .Select(b => new { b.CoverImagePath })
                        .FirstOrDefaultAsync(cancellationToken);
                    var frontRef = (BookCoverRefResolver.ResolveEbookFrontCoverRef(
                        frontRows.GetValueOrDefault($"book:{req.BookId}:printReadyCoverFront"),
                        frontRows.GetValueOrDefault($"book:{req.BookId}:aiCoverLastPreview"),
                        bookCoverRow?.CoverImagePath,
                        wrapVal) ?? "").Trim();
                    if (frontRef.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                    {
                        if (frontRef.Contains("image/jpeg", StringComparison.OrdinalIgnoreCase) || frontRef.Contains("image/jpg", StringComparison.OrdinalIgnoreCase))
                            frontExt = "jpg";
                        var fix = frontRef.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                        if (fix >= 0) frontBytes = Convert.FromBase64String(frontRef[(fix + 7)..]);
                    }
                    else if (frontRef.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || frontRef.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var frontClient = _httpClientFactory.CreateClient();
                            frontBytes = await frontClient.GetByteArrayAsync(frontRef, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "ExportPrintReadyBundle front fetch failed for book {BookId}", req.BookId);
                        }
                        if (frontRef.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || frontRef.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                            frontExt = "jpg";
                    }
                    else if (frontRef.StartsWith("/"))
                    {
                        var physicalFront = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", frontRef.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(physicalFront))
                            frontBytes = await System.IO.File.ReadAllBytesAsync(physicalFront, cancellationToken);
                        if (frontRef.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || frontRef.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                            frontExt = "jpg";
                    }

                    // Build the EPUB too, so a single "Both" download yields every deliverable:
                    // paperback interior PDF + full wrap cover + ebook front cover + reflowable EPUB.
                    try
                    {
                        var epubOpt = BookPdfExportOptions.LoadFromPersistence(fmtRow, draftRow?.Value);
                        epubOpt.ApplyRequestOverrides(req);
                        epubOpt.Format = "Ebook";
                        epubOpt.IncludeCoverPage = true;
                        if (epubOpt.PublishingPlatform.Equals("Just Print Ready File", StringComparison.OrdinalIgnoreCase))
                            epubOpt.PublishingPlatform = "";
                        var epubCoverRef = !string.IsNullOrEmpty(frontRef)
                            ? BookCoverRefResolver.NormalizeCoverUrlRef(frontRef)
                            : BookCoverRefResolver.NormalizeCoverUrlRef(wrapVal);
                        var epubPageCount = _bookPageMetricsService.Estimate(details, epubOpt).PageCount;
                        epubBytes = await _epubExportService.BuildEpubAsync(
                            details,
                            epubCoverRef,
                            (req.DisplayTitle ?? details.BookTitle ?? "").Trim(),
                            (req.DisplayAuthor ?? details.AuthorName ?? "").Trim(),
                            epubOpt,
                            epubPageCount,
                            "6 x 9 in",
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "ExportPrintReadyBundle EPUB build failed for book {BookId}; ZIP will omit the EPUB.", req.BookId);
                    }
                }

                using var zipMs = new MemoryStream();
                using (var zip = new System.IO.Compression.ZipArchive(zipMs, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
                {
                    var pdfEntry = zip.CreateEntry("interior-6x9.pdf");
                    await using (var es = pdfEntry.Open())
                        await es.WriteAsync(pdfBytes, cancellationToken);

                    if (wrapBytes != null && wrapBytes.Length > 0)
                    {
                        var coverEntry = zip.CreateEntry("cover-wrap-full.png");
                        await using (var es = coverEntry.Open())
                            await es.WriteAsync(wrapBytes, cancellationToken);
                    }
                    else
                    {
                        return BadRequest(new { success = false, message = "Print cover wrap not found. Generate and save your full wrap (front + spine + back) in Cover Design first." });
                    }

                    if (frontBytes != null && frontBytes.Length > 0)
                    {
                        var frontEntry = zip.CreateEntry("cover-front." + frontExt);
                        await using (var es = frontEntry.Open())
                            await es.WriteAsync(frontBytes, cancellationToken);
                    }

                    if (epubBytes != null && epubBytes.Length > 0)
                    {
                        var epubEntry = zip.CreateEntry("ebook.epub");
                        await using (var es = epubEntry.Open())
                            await es.WriteAsync(epubBytes, cancellationToken);
                    }
                }

                var title = (req.DisplayTitle ?? details.BookTitle ?? "book").Trim();
                var safe = Regex.Replace(title, @"[^\w\-\s]", "");
                safe = Regex.Replace(safe, @"\s+", "-").Trim('-');
                if (string.IsNullOrEmpty(safe)) safe = "book";
                await _bookService.MarkPublishedAsync(req.BookId, sessionUserId.Value, cancellationToken);
                await _bookFlow.SaveStepAsync(req.BookId, BookFlowStateService.StepPublish, "print", cancellationToken);
                return File(zipMs.ToArray(), "application/zip", $"{safe}-print-ready-{req.BookId}.zip");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ExportPrintReadyBundle failed for book {BookId}", req.BookId);
                return StatusCode(500, new { success = false, message = "Print export failed." });
            }
        }

        /// <summary>Print-ready full wrap cover only (PNG) — second file for dual export.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Route("Books/ExportPrintReadyCoverImage")]
        public async Task<IActionResult> ExportPrintReadyCoverImage([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
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

            var wrapKey = $"book:{req.BookId}:printReadyCoverWrap";
            var wrapRow = await _context.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == wrapKey, cancellationToken);
            var wrapVal = (wrapRow?.Value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(wrapVal))
            {
                var aiKey = $"book:{req.BookId}:aiCoverLastPreview";
                var aiRow = await _context.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == aiKey, cancellationToken);
                wrapVal = (aiRow?.Value ?? "").Trim();
            }

            byte[]? wrapBytes = null;
            var fileName = "cover-wrap.png";
            if (wrapVal.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                var ix = wrapVal.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
                if (ix >= 0) wrapBytes = Convert.FromBase64String(wrapVal[(ix + 7)..]);
            }
            else if (wrapVal.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || wrapVal.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var client = _httpClientFactory.CreateClient();
                    wrapBytes = await client.GetByteArrayAsync(wrapVal, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ExportPrintReadyCoverImage fetch failed for book {BookId}", req.BookId);
                }
            }
            else if (wrapVal.StartsWith("/"))
            {
                var physical = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", wrapVal.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(physical))
                    wrapBytes = await System.IO.File.ReadAllBytesAsync(physical, cancellationToken);
            }

            if (wrapBytes == null || wrapBytes.Length == 0)
                return BadRequest(new { success = false, message = "No print wrap cover found. Generate it in Cover Design (print-ready flow) first." });

            var book = await _context.Books.AsNoTracking().FirstOrDefaultAsync(b => b.BookId == req.BookId, cancellationToken);
            var rawName = (book?.Title ?? "book").Trim();
            var safe = Regex.Replace(rawName, @"[^\w\-\s]", "");
            safe = Regex.Replace(safe, @"\s+", "-").Trim('-');
            if (string.IsNullOrEmpty(safe)) safe = "book";
            fileName = $"{safe}-cover-wrap-{req.BookId}.png";
            await _bookService.MarkPublishedAsync(req.BookId, sessionUserId.Value, cancellationToken);
            await _bookFlow.SaveStepAsync(req.BookId, BookFlowStateService.StepPublish, "print", cancellationToken);
            return File(wrapBytes, "image/png", fileName);
        }

        /// <summary>Extract plain text from an uploaded .txt / .md or .pdf (first pass) for chapter import.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [RequestSizeLimit(52_428_800)]
        [Route("Books/ImportChapterFile")]
        public async Task<IActionResult> ImportChapterFile(CancellationToken cancellationToken)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null)
                return Json(new { success = false, message = "Please sign in." });

            if (!Request.HasFormContentType || !Request.ContentType!.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Use the Import button so the file is sent correctly." });

            var file = Request.Form.Files["file"];
            if (file == null || file.Length == 0)
            {
                if (Request.Form.Files.Count > 0)
                    file = Request.Form.Files[0];
            }

            if (file == null || file.Length == 0)
                return Json(new { success = false, message = "Select a PDF, Word, or text file." });

            byte[] bytes;
            await using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, cancellationToken);
                bytes = ms.ToArray();
            }

            if (bytes.Length == 0)
                return Json(new { success = false, message = "This file appears empty." });

            var ext = ChapterDocumentImportService.ResolveExtension(file.FileName, file.ContentType, bytes);
            if (ext == ".doc")
                return Json(new { success = false, message = "Old Word .doc files are not supported. Save as .docx and upload again." });

            if (!ChapterDocumentImportService.IsSupportedExtension(ext))
                return Json(new { success = false, message = ChapterDocumentImportService.SupportedFormatsMessage() });

            try
            {
                string text;
                List<ChapterDocumentImportService.ImportedChapter> splitChapters;

                // .docx → rich path that preserves embedded images inline (HTML chapter bodies).
                if (ext == ".docx")
                {
                    var docxChapters = ChapterDocumentImportService.ExtractDocxChapters(bytes, out var docxPlain);
                    text = ChapterDocumentImportService.SanitizeImportedText(docxPlain);
                    var docxHasImages = docxChapters.Any(c => c.Body.Contains("<img", StringComparison.OrdinalIgnoreCase));
                    if (docxChapters.Count > 0 && (docxHasImages || docxChapters.Count > 1))
                        splitChapters = docxChapters; // keep structured HTML (images + headings)
                    else
                        splitChapters = ChapterDocumentImportService.SplitIntoChapters(
                            string.IsNullOrWhiteSpace(text)
                                ? ChapterDocumentImportService.ExtractDocxTextAsPlain(bytes)
                                : text);
                }
                else
                {
                    text = ChapterDocumentImportService.ExtractText(bytes, ext, cancellationToken);
                    text = ChapterDocumentImportService.SanitizeImportedText(text);
                    splitChapters = ChapterDocumentImportService.SplitIntoChapters(text);
                }

                if (string.IsNullOrWhiteSpace(text) && splitChapters.Count == 0)
                    return Json(new { success = false, message = "No readable text found (scanned PDFs need OCR). Paste the text instead." });

                var suggestedBookTitle = ChapterDocumentImportService.ResolveSuggestedBookTitle(bytes, ext, file.FileName, text);
                var (suggestedChapterNo, suggestedChapterTitle) = ChapterDocumentImportService.SuggestChapterFromBodyText(
                    string.IsNullOrWhiteSpace(text) ? (splitChapters.FirstOrDefault()?.Title ?? "Imported chapter") : text);
                var chapters = splitChapters
                    .Select(c => new { chapterNo = c.ChapterNo, title = c.Title, text = c.Body, characterCount = c.Body.Length })
                    .ToList();
                var hasImages = chapters.Any(c => c.text.Contains("<img", StringComparison.OrdinalIgnoreCase));

                return Json(new
                {
                    success = true,
                    fileName = file.FileName,
                    text,
                    characterCount = text.Length,
                    suggestedBookTitle,
                    suggestedChapterNo,
                    suggestedChapterTitle,
                    chapters,
                    chapterCount = chapters.Count,
                    hasImages
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ImportChapterFile failed for {Name}", file.FileName);
                return Json(new { success = false, message = ChapterDocumentImportService.MapImportExceptionMessage(ex) });
            }
        }

        //================== Load Books on Dropdown ==================
       

        [HttpGet]
        public async Task<IActionResult> DebugBooks(int userId)
        {
            var books = await _context.Books
                .Where(b => b.UserId == userId)
                .ToListAsync();

            return Json(new
            {
                userId = userId,
                bookCount = books.Count,
                books = books
            });
        }
        //============================================================== 1
        //====================== Finalize Chapter ======================
        //==============================================================
        [HttpPost]
        public async Task<IActionResult> FinalizeChapter([FromBody] FinalizeChapterRequest model)
        {
            try
            {
                Console.WriteLine($"🟢 Received model: ResponseId={model?.ResponseId}, UserId={model?.UserId}, BookId={model?.BookId}, Chapter={model?.Chapter}");

                if (model == null)
                {
                    Console.WriteLine("❌ Model is null");
                    return Json(new { success = false, message = "Invalid data received." });
                }
                // Validate required fields
                if (model.ResponseId == 0 || model.UserId == 0 || model.BookId == 0 || model.Chapter == 0)
                {
                    Console.WriteLine("❌ Missing required fields");
                    return BadRequest(new { success = false, message = "Missing required fields" });
                }

                var finalize = new FinalizeChapters
                {
                    ResponseId = model.ResponseId,
                    UserId = model.UserId,
                    BookId = model.BookId,
                    Chapter = model.Chapter,  // Note: Changed from chapterNo to ChapterNo
                    StatusCode = "ReadOnly",
                    CreatedAt = DateTime.Now
                };

                bool result = await _bookService.SetRecordReadOnlyAsync(finalize);

                if (result)
                {
                    if (model.UserId > 0)
                        HttpContext.Session.SetString("HasGeneratedBook", "1");
                    return Json(new { success = true, message = "Chapter finalized successfully." });
                }
                else
                    return Json(new { success = false, message = "Could not finalize the chapter." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Exception in FinalizeChapter: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Internal server error: " + ex.Message
                });
            }
        }


        //====================== Finalize Chapter ======================
        [HttpPost]
        [Route("Books/FinalizeChapterAPI")]
        public async Task<IActionResult> FinalizeChapterAPI([FromBody] APIFinalizeChapterRequest model)
        {
            if (model == null)
                return BadRequest("Invalid request payload.");

            // ✅ Load from appsettings.json
           

            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.ApproveUrl, "/api/approve").Trim();
            if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out _))
            {
                _logger.LogError("FinalizeChapterAPI: invalid absolute upstream URL: {Url}", apiUrl);
                return BadRequest(new { success = false, message = "Server misconfiguration: ExternalApi approve URL is not a valid absolute URL.", detail = apiUrl });
            }
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);

            string responseData = string.Empty;
            int? rawResponseId = null;

            try
            {
                if (string.IsNullOrEmpty(apiKey))
                    return BadRequest(new { success = false, message = ExternalApiKeyResolver.MissingKeyUserMessage });

                var json = JsonConvert.SerializeObject(model);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"📤 Sending finalize request to API: {json}");

                using var httpReq = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = content };
                using var response = await _bookApiClient.SendAsync(httpReq, BookApiCallTimeoutKind.Standard, HttpContext.RequestAborted);
                responseData = await response.Content.ReadAsStringAsync();

                // Audit log must not fail the client after upstream approve succeeded.
                try
                {
                    rawResponseId = await _rawResponseService.SaveRawResponseAsync(
                        new AIBookRequest
                        {
                            UserId = model.UserId,
                            BookId = model.BookId,
                            Chapter = int.TryParse(model.Chapter, out var ch) ? ch : 0,
                            UserInput = "Finalize"
                        },
                        responseData,
                        apiUrl,
                        response.StatusCode.ToString()
                    );
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "FinalizeChapterAPI: SaveRawResponseAsync failed; still returning upstream result.");
                }

                Console.WriteLine($"📥 Finalize API Response Status: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, responseData);
                }
                // If success, update DB: Book.Status = "Final", Chapter.Status = "Final"
                // ✅ Update DB if success
                try
                {
                    if (int.TryParse(model.BookId, out int bookId))
                    {
                        var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId);
                        int chapterNum = int.TryParse(model.Chapter, out var c) ? c : 0;
                        var chapter = await _context.Chapters.FirstOrDefaultAsync(ch => ch.BookId == bookId && ch.ChapterNumber == chapterNum);

                        if (chapter != null)
                        {
                            chapter.Status = "Finalized";
                            chapter.UpdatedAt = DateTime.UtcNow;
                            _context.Chapters.Update(chapter);
                        }
                        else
                        {
                            _context.Chapters.Add(new Chapters
                            {
                                BookId = bookId,
                                ChapterNumber = chapterNum,
                                Title = $"Chapter {chapterNum}",
                                Content = "",
                                Status = "Finalized",
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            });
                        }

                        await _context.SaveChangesAsync();

                        if (int.TryParse(model.UserId, out int uid) && uid > 0)
                            await _publishReadiness.TryPromoteBookToFinalizedAsync(uid, bookId);
                    }
                    if (int.TryParse(model.UserId, out int uid2) && uid2 > 0)
                        HttpContext.Session.SetString("HasGeneratedBook", "1");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ DB update after finalize failed: {ex.Message}");
                }

                return Content(responseData, "application/json");
            }
            catch (Exception ex)
            {
                if (rawResponseId == null)
                {
                    _ = await _rawResponseService.SaveRawResponseAsync(
                        new AIBookRequest
                        {
                            UserId = model.UserId,
                            BookId = model.BookId,
                            Chapter = int.TryParse(model.Chapter, out var c) ? c : 0,
                            UserInput = "Finalize"
                        },
                        responseData,
                        apiUrl,
                        "500",
                        $"Finalize error: {ex.Message}"
                    );
                }

                Console.WriteLine($"❌ FinalizeChapter Exception: {ex.Message}");
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }
        //==================================
        //    Book Design Action Method
        //==================================
        public IActionResult SelectDesign()
        {
            var model = new BookDesignViewModel
            {
                Designs = new List<BookDesign>
            {
                new BookDesign
                {
                    DesignId = 1,
                    DesignName = "Classic A4",
                    SizeName = "A4",
                    Orientation = "Portrait",
                    ImagePath = "/images/books/a4-portrait.png"
                },
                new BookDesign
                {
                    DesignId = 2,
                    DesignName = "Modern A5",
                    SizeName = "A5",
                    Orientation = "Portrait",
                    ImagePath = "/images/books/a5-portrait.png"
                },
                new BookDesign
                {
                    DesignId = 3,
                    DesignName = "6 x 9 Landscape",
                    SizeName = "6 x 9",
                    Orientation = "Landscape",
                    ImagePath = "/images/books/6x9-landscape.png"
                }
            }
            };

            return View(model);
        }

        [HttpPost]
        public IActionResult SelectDesign(BookDesignViewModel model)
        {
            if (model.SelectedDesignId > 0)
            {
                return RedirectToAction("FormatBook", new { designId = model.SelectedDesignId });
            }

            ModelState.AddModelError("", "Please select a book design.");
            return View(model);
        }

        public IActionResult FormatBook(int designId)
        {
            // Continue book formatting process here
            return View();
        }

        /// <summary>Advance book flow to Book Formatting before opening the formatter screen.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("Books/EnterBookFormatting")]
        public async Task<IActionResult> EnterBookFormatting([FromBody] EnterBookFormattingRequest? body)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue || userId.Value <= 0)
                return Json(new { success = false, message = "Please sign in." });

            var bookId = body?.BookId ?? 0;
            if (bookId <= 0)
                return Json(new { success = false, message = "BookId is required." });

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId && b.UserId == userId.Value);
            if (!owns)
                return Json(new { success = false, message = "Book not found." });

            var fmt = (body?.Format ?? "Ebook").Trim();
            var formatPath = fmt.Contains("paper", StringComparison.OrdinalIgnoreCase)
                || fmt.Contains("print", StringComparison.OrdinalIgnoreCase)
                || fmt.Equals("Both", StringComparison.OrdinalIgnoreCase)
                ? "print"
                : "ebook";

            await _bookFlow.SaveStepAsync(bookId, BookFlowStateService.StepFormat, formatPath);
            HttpContext.Session.SetString("HasGeneratedBook", "1");
            HttpContext.Session.SetInt32("LastSelectedBookId", bookId);
            HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);
            if (!string.IsNullOrWhiteSpace(fmt))
                HttpContext.Session.SetString("LastSelectedFormat", fmt);

            var redirectUrl = _bookFlow.BuildResumeUrl(bookId, BookFlowStateService.StepFormat, formatPath);
            return Json(new { success = true, redirectUrl });
        }

        /// <summary>Redirects to the live Book Formatting workspace (interior + trim) for the selected book.</summary>
        [HttpGet]
        public IActionResult AIGenerateBookFormat(int? bookId = null)
        {
            var bid = bookId ?? HttpContext.Session.GetInt32("LastSelectedBookId") ?? 0;
            return RedirectToAction("CoverDesignCalculatorFixing", "BookDesign", new { bookId = bid });
        }

        /// <summary>Marks linear publishing pipeline steps in session (demo: unlocks next UI stages).</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public IActionResult SetPipelineStep([FromBody] PipelineStepDto body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Step)) return BadRequest();
            var bid = body.BookId;
            if (bid > 0) HttpContext.Session.SetInt32("LastSelectedBookId", bid);
            switch (body.Step.Trim().ToLowerInvariant())
            {
                case "writer":
                case "generate":
                    HttpContext.Session.SetString("HasGeneratedBook", "1");
                    break;
                case "format":
                case "formatting":
                    HttpContext.Session.SetString("FormattingDone", "1");
                    break;
                case "cover":
                    HttpContext.Session.SetString("CoverFinalized", "1");
                    break;
                default:
                    return BadRequest("Unknown step");
            }
            return Ok(new { success = true });
        }

        public class PipelineStepDto
        {
            public string Step { get; set; } = "";
            public int BookId { get; set; }
        }

        public class EnterBookFormattingRequest
        {
            public int BookId { get; set; }
            public string? Format { get; set; }
        }

        /// <summary>Generate whole-book HTML via AI prompt template and save to <c>BookContentHtml</c>.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [RequestTimeout("AiGeneration")]
        [Route("Books/GenerateBookHtml")]
        public async Task<IActionResult> GenerateBookHtml([FromBody] GenerateBookHtmlRequest? body, CancellationToken cancellationToken)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue || userId.Value <= 0)
                return Json(new { success = false, message = "Please sign in." });

            if (body == null || body.BookId <= 0)
                return Json(new { success = false, message = "BookId is required." });

            var request = new BookRequest
            {
                BookTitle = body.BookTitle ?? "",
                AuthorName = body.AuthorName ?? "",
                Language = body.Language ?? "English",
                ChaptersCount = body.ChaptersCount > 0 ? body.ChaptersCount : 5,
                MinWordsPerChapter = body.MinWordsPerChapter > 0 ? body.MinWordsPerChapter : 800,
                ToneDescription = body.ToneDescription ?? "descriptive"
            };

            if (string.IsNullOrWhiteSpace(request.BookTitle))
            {
                var bookRow = await _context.Books.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BookId == body.BookId && b.UserId == userId.Value, cancellationToken);
                if (bookRow != null)
                    request.BookTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                        _context, userId.Value, bookRow.BookId, bookRow.Title);
            }

            if (string.IsNullOrWhiteSpace(request.AuthorName))
            {
                var userRow = await _context.Users.AsNoTracking()
                    .Where(u => u.UserId == userId.Value)
                    .Select(u => new { u.FullName, u.UserEmail })
                    .FirstOrDefaultAsync(cancellationToken);
                request.AuthorName = !string.IsNullOrWhiteSpace(userRow?.FullName)
                    ? userRow!.FullName!.Trim()
                    : (userRow?.UserEmail ?? "").Trim();
            }

            var result = await _bookGeneratorService.GenerateAndSaveHtmlAsync(
                userId.Value, body.BookId, request, cancellationToken);

            if (!result.Success)
                return Json(new { success = false, message = result.Message ?? "Generation failed." });

            return Json(new
            {
                success = true,
                message = result.Message,
                bookId = body.BookId,
                chapterCount = result.ChapterCount,
                htmlLength = result.Html?.Length ?? 0
            });
        }

        /// <summary>PDF export from stored <c>BookContentHtml</c> (falls back to chapter pipeline when empty).</summary>
        [HttpGet]
        [Route("Books/{bookId:int}/Pdf")]
        public async Task<IActionResult> DownloadBookHtmlPdf(int bookId, CancellationToken cancellationToken)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue || userId.Value <= 0)
                return Unauthorized();

            var book = await _context.Books.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId.Value, cancellationToken);
            if (book == null)
                return NotFound();

            var displayTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                _context, userId.Value, book.BookId, book.Title);
            var authorRow = await _context.Users.AsNoTracking()
                .Where(u => u.UserId == userId.Value)
                .Select(u => new { u.FullName, u.UserEmail })
                .FirstOrDefaultAsync(cancellationToken);
            var author = !string.IsNullOrWhiteSpace(authorRow?.FullName)
                ? authorRow!.FullName!.Trim()
                : (authorRow?.UserEmail ?? "").Trim();

            byte[] pdfBytes;
            var safeFileName = string.IsNullOrWhiteSpace(displayTitle) ? "book" : displayTitle.Trim();

            if (!string.IsNullOrWhiteSpace(book.BookContentHtml))
            {
                pdfBytes = await _bookPdfService.RenderStoredBookHtmlPdfAsync(
                    book.BookContentHtml, displayTitle, author, bookId, cancellationToken);
            }
            else
            {
                var details = await _bookService.GetBookDetailsForPreviewAsync(userId.Value, bookId);
                if (details is not { Success: true } || details.Chapters.Count == 0)
                    return BadRequest("No book content to export. Generate chapters or run whole-book HTML generation first.");

                pdfBytes = await _bookPdfService.RenderFullBookPdfAsync(
                    details, null, displayTitle, author, details.Genre, new BookPdfExportOptions(), null, cancellationToken);
            }

            return File(pdfBytes, "application/pdf", $"{safeFileName}.pdf");
        }

        public sealed class GenerateBookHtmlRequest
        {
            public int BookId { get; set; }
            public string? BookTitle { get; set; }
            public string? AuthorName { get; set; }
            public string? Language { get; set; }
            public int ChaptersCount { get; set; }
            public int MinWordsPerChapter { get; set; }
            public string? ToneDescription { get; set; }
        }

        // ========================== MANUSCRIPT UPLOAD ===========================
        // Accept .docx, .pdf, .txt; save file path to DB tied to logged-in user
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [RequestSizeLimit(1024L * 1024L * 100L)] // 100 MB
        public async Task<IActionResult> UploadManuscript(int bookId, IFormFile file)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

            var allowed = new[] { ".docx", ".pdf", ".txt" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext)) return BadRequest("Only .docx, .pdf, .txt are allowed.");

            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");

            var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", sessionUserId.Value.ToString(), "books", bookId.ToString());
            Directory.CreateDirectory(uploadsRoot);
            var fileName = $"manuscript_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            var savePath = Path.Combine(uploadsRoot, fileName);
            using (var stream = new FileStream(savePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Save relative path into DB
            var relativePath = $"/uploads/{sessionUserId}/books/{bookId}/{fileName}";
            book.ManuscriptPath = relativePath;
            book.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, path = relativePath });
        }

        // ========================== ANALYZE MANUSCRIPT ===========================
        // Extract text, split into chapters/sections, save structure into DB
        [HttpPost]
        public async Task<IActionResult> AnalyzeManuscript(int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");
            if (string.IsNullOrWhiteSpace(book.ManuscriptPath)) return BadRequest("Please upload a manuscript first.");

            // Resolve physical path
            var physicalPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", book.ManuscriptPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
            if (!System.IO.File.Exists(physicalPath)) return NotFound("Manuscript file missing.");

            string text = string.Empty;
            var ext = Path.GetExtension(physicalPath).ToLowerInvariant();
            try
            {
                if (ext == ".txt")
                {
                    text = await System.IO.File.ReadAllTextAsync(physicalPath);
                }
                else
                {
                    // Placeholder extraction for .pdf/.docx to avoid new dependencies
                    // In production, integrate a proper parser (e.g., iText7, DocX)
                    text = $"[Placeholder extraction of {ext}] \n" + await System.IO.File.ReadAllTextAsync(physicalPath);
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Failed to read manuscript: {ex.Message}");
            }

            // Naive split into chapters by headings or "Chapter"
            var chapters = new List<(string title, string content)>();
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var buffer = new StringBuilder();
            string currentTitle = "Chapter 1";
            int chapterCounter = 1;
            foreach (var ln in lines)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(ln.Trim(), @"^(Chapter\s+\d+|CHAPTER\s+\d+|#\s+|##\s+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    if (buffer.Length > 0)
                    {
                        chapters.Add((currentTitle, buffer.ToString().Trim()));
                        buffer.Clear();
                        chapterCounter++;
                    }
                    currentTitle = ln.Trim();
                }
                else
                {
                    buffer.AppendLine(ln);
                }
            }
            if (buffer.Length > 0) chapters.Add((currentTitle, buffer.ToString().Trim()));
            if (chapters.Count == 0) chapters.Add(("Chapter 1", text));

            // Persist chapters (replace existing analyzed chapters)
            using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                var existing = await _context.Chapters.Where(c => c.BookId == bookId).ToListAsync();
                if (existing.Any())
                {
                    _context.Chapters.RemoveRange(existing);
                    await _context.SaveChangesAsync();
                }
                int i = 1;
                foreach (var ch in chapters)
                {
                    _context.Chapters.Add(new Chapters
                    {
                        BookId = bookId,
                        ChapterNumber = i++,
                        Title = ch.title,
                        Content = ch.content,
                        LanguageId = 1,
                        Status = "Analyzed",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                await _context.SaveChangesAsync();

                // Save TOC into Settings
                var toc = chapters.Select((c, idx) => new { number = idx + 1, title = c.title }).ToList();
                var tocJson = JsonConvert.SerializeObject(toc);
                await UpsertSettingAsync($"book:{bookId}:toc", tocJson, "Book");

                await tx.CommitAsync();
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, $"Analyze failed: {ex.Message}");
            }

            return Ok(new { success = true, chapters = chapters.Count });
        }

        // ========================== TITLE PAGE ===========================
        // Save Title Page data to Settings to avoid schema changes
        [HttpPost]
        public async Task<IActionResult> SaveTitlePage([FromBody] object payload, int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();

            // Store raw JSON under book-scoped key
            await UpsertSettingAsync($"book:{bookId}:titlePage", payload?.ToString() ?? "{}", "Book");
            return Ok(new { success = true });
        }

        // ========================== TABLE OF CONTENTS ===========================
        [HttpPost]
        public async Task<IActionResult> SaveToc([FromBody] object payload, int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();

            await UpsertSettingAsync($"book:{bookId}:toc", payload?.ToString() ?? "[]", "Book");
            return Ok(new { success = true });
        }
        //=======================================
        //   Get Chapter Number
        //======================================
        [HttpGet]
        public async Task<IActionResult> GetLastChapter(int userId, int bookId)
        {
            var lastChapter = await _bookService.GetLastChapterAsync(userId, bookId);
            return Json(new { lastChapter = lastChapter });
        }
        // ========================== ADDITIONAL ELEMENTS ===========================
        // Upsert each element into Chapters with ChapterNumber = 0 and unique Title
        [HttpPost]
        public async Task<IActionResult> SaveAdditionalElements([FromBody] IDictionary<string, string> elements, int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            if (elements == null || elements.Count == 0) return BadRequest("No elements supplied.");

            var allowedTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Proofreading notes","Editing notes","Copyright page content","Ghostwriting notes","Dedication","Epigraph","Preface","Foreword","Epilogue","Afterword","Other back matter"
            };

            foreach (var kv in elements)
            {
                var title = kv.Key?.Trim() ?? "";
                if (!allowedTitles.Contains(title)) continue;
                var content = kv.Value ?? "";

                var chapter = await _context.Chapters.FirstOrDefaultAsync(c => c.BookId == bookId && c.ChapterNumber == 0 && c.Title == title);
                if (chapter == null)
                {
                    _context.Chapters.Add(new Chapters
                    {
                        BookId = bookId,
                        ChapterNumber = 0,
                        Title = title,
                        Content = content,
                        Status = "Notes",
                        LanguageId = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    chapter.Content = content;
                    chapter.UpdatedAt = DateTime.UtcNow;
                    _context.Chapters.Update(chapter);
                }
            }
            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        // ========================== COVER ===========================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> UploadCover(int bookId, IFormFile file)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

            var allowed = new[] { ".png", ".jpg", ".jpeg", ".webp" };
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext)) return BadRequest("Only image files are allowed.");

            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");

            var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", sessionUserId.Value.ToString(), "books", bookId.ToString());
            Directory.CreateDirectory(uploadsRoot);
            var fileName = $"cover_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
            var savePath = Path.Combine(uploadsRoot, fileName);
            using (var stream = new FileStream(savePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/{sessionUserId}/books/{bookId}/{fileName}";
            book.CoverImagePath = relativePath;
            book.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, path = relativePath });
        }

        [HttpPost]
        [DisableRequestTimeout]
        public async Task<IActionResult> GenerateAICover(int bookId, [FromBody] IDictionary<string, string> body)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            var prompt = body != null && body.TryGetValue("prompt", out var p) ? p : "";

            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");

            // Placeholder AI cover generation: Use a stock placeholder and save the prompt in Settings
            var placeholder = "/images/ai-cover-placeholder.png";
            book.CoverImagePath = placeholder;
            book.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            await UpsertSettingAsync($"book:{bookId}:aiCoverPrompt", prompt ?? "", "Book");

            return Ok(new { success = true, path = placeholder });
        }

        /// <summary>Get user's books for AI Cover Design screen (cover, title, description, genre, aiCoverPrompt).</summary>
        [HttpGet]
        public async Task<IActionResult> GetUserBooksForCoverDesign()
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();

            var books = await _context.Books
                .AsNoTracking()
                .Where(b => b.UserId == sessionUserId.Value)
                .OrderByDescending(b => b.isActive)
                .ThenByDescending(b => b.UpdatedAt ?? b.CreatedAt)
                .Select(b => new { b.BookId, b.Title, b.CoverImagePath, b.Description, b.Genre, b.Subtitle })
                .ToListAsync();

            var bookIds = books.Select(b => b.BookId).ToList();
            var promptKeys = bookIds.SelectMany(id => new[]
            {
                $"book:{id}:aiCoverPrompt",
                $"book:{id}:aiCoverLastPreview",
                $"book:{id}:printReadyCoverFront"
            }).ToList();
            var promptRows = await _context.Settings
                .AsNoTracking()
                .Where(s => promptKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");

            var coverUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value);
            // Display name for cover only — do not use email as default author text (wrong UX on preview).
            var authorDisplayName = (coverUser?.FullName ?? "").Trim();

            var titleByBook = await BookTitleResolver.ResolveDisplayTitlesBatchAsync(
                _context,
                sessionUserId.Value,
                books.Select(b => (b.BookId, (string?)b.Title)).ToList());

            var result = new List<object>();
            foreach (var b in books)
            {
                var pKey = $"book:{b.BookId}:aiCoverPrompt";
                var lastKey = $"book:{b.BookId}:aiCoverLastPreview";
                var frontKey = $"book:{b.BookId}:printReadyCoverFront";
                var frontPreview = (promptRows.GetValueOrDefault(frontKey, "") ?? "").Trim();
                var lastPreview = (promptRows.GetValueOrDefault(lastKey, "") ?? "").Trim();
                var resolvedPreview = BookCoverRefResolver.ForListPayloadCoverRef(
                    BookCoverRefResolver.ResolveEbookFrontCoverRef(frontPreview, lastPreview, b.CoverImagePath, wrap: ""));
                result.Add(new
                {
                    bookId = b.BookId,
                    title = titleByBook.GetValueOrDefault(b.BookId, b.Title ?? "Untitled Book"),
                    subtitle = b.Subtitle ?? "",
                    coverImagePath = b.CoverImagePath ?? "",
                    description = b.Description ?? "",
                    genre = b.Genre ?? "",
                    aiCoverPrompt = promptRows.GetValueOrDefault(pKey, ""),
                    aiCoverLastPreview = resolvedPreview,
                    authorName = authorDisplayName
                });
            }

            return Json(new { success = true, books = result });
        }

        /// <summary>Latest book row + cover settings for one book (Cover Design page deep link / refresh).</summary>
        [HttpGet]
        public async Task<IActionResult> GetBookForCoverDesign(int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();

            if (bookId <= 0)
                return Json(new { success = false, message = "bookId is required." });

            var b = await _context.Books
                .AsNoTracking()
                .Where(x => x.BookId == bookId && x.UserId == sessionUserId.Value)
                .Select(x => new { x.BookId, x.Title, x.CoverImagePath, x.Description, x.Genre, x.Subtitle })
                .FirstOrDefaultAsync();

            if (b == null)
                return Json(new { success = false, message = "Book not found." });

            var pKey = $"book:{b.BookId}:aiCoverPrompt";
            var lastKey = $"book:{b.BookId}:aiCoverLastPreview";
            var frontKey = $"book:{b.BookId}:printReadyCoverFront";
            var wrapKey = $"book:{b.BookId}:printReadyCoverWrap";
            var promptRows = await _context.Settings
                .Where(s => s.Key == pKey || s.Key == lastKey || s.Key == frontKey || s.Key == wrapKey)
                .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");

            var coverUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value);
            var authorDisplayName = (coverUser?.FullName ?? "").Trim();
            var frontPreview = (promptRows.GetValueOrDefault(frontKey, "") ?? "").Trim();
            var lastPreview = (promptRows.GetValueOrDefault(lastKey, "") ?? "").Trim();
            var wrapPreview = (promptRows.GetValueOrDefault(wrapKey, "") ?? "").Trim();
            var resolvedPreview = BookCoverRefResolver.NormalizeCoverUrlRef(
                BookCoverRefResolver.ResolveEbookFrontCoverRef(frontPreview, lastPreview, b.CoverImagePath, wrapPreview));

            var displayTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                _context, sessionUserId.Value, b.BookId, b.Title);

            var book = new
            {
                bookId = b.BookId,
                title = displayTitle,
                subtitle = b.Subtitle ?? "",
                coverImagePath = b.CoverImagePath ?? "",
                description = b.Description ?? "",
                genre = b.Genre ?? "",
                aiCoverPrompt = promptRows.GetValueOrDefault(pKey, ""),
                aiCoverLastPreview = resolvedPreview,
                authorName = authorDisplayName
            };

            return Json(new { success = true, book });
        }

        /// <summary>Generate AI cover preview via external POST /api/generate-cover. Returns { success, options[] }.</summary>
        [HttpPost]
        [DisableRequestTimeout]
        public async Task<IActionResult> GenerateAICoverPreview(int bookId, [FromBody] IDictionary<string, string> body)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Json(new { success = false, message = "Please sign in." });

            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return Json(new { success = false, message = "Book not found." });

            var prompt = body != null && body.TryGetValue("prompt", out var p) ? (p ?? "").Trim() : "";
            var title = body != null && body.TryGetValue("title", out var t) ? (t ?? "").Trim() : (book.Title ?? "").Trim();
            var styleKey = body != null && body.TryGetValue("style", out var s) ? (s ?? "").Trim() : "modern";
            if (string.IsNullOrEmpty(title)) title = "My Book";

            await UpsertSettingAsync($"book:{bookId}:aiCoverPrompt", prompt ?? "", "Book");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == book.UserId);
            var authorName = user?.FullName ?? user?.UserEmail ?? book.UserId.ToString();
            if (body != null && body.TryGetValue("author", out var authIn) && !string.IsNullOrWhiteSpace(authIn))
                authorName = authIn.Trim();

            var category = !string.IsNullOrWhiteSpace(book.Genre) ? book.Genre.Trim() : "General";
            if (body != null && body.TryGetValue("category", out var catIn) && !string.IsNullOrWhiteSpace(catIn))
                category = catIn.Trim();

            var coverStyleLabel = MapCoverStyleForExternalApi(styleKey, prompt);
            var size = BookApiInputValidation.NormalizeSize(
                (_configuration["ExternalApi:CoverGenerateSize"] ?? "1024x1536").Trim(),
                "1024x1536");
            var quality = BookApiInputValidation.NormalizeQuality(
                (_configuration["ExternalApi:CoverGenerateQuality"] ?? "medium").Trim(),
                "medium");
            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.GenerateCoverUrl, "/api/generate-cover").Trim();
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, message = ExternalApiKeyResolver.MissingKeyUserMessage });

            // Payload must match external API (no undocumented "prompt" key — direction folded into cover_style)
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
            _logger.LogInformation("Generate cover request bookId={BookId} url={Url} jsonChars={Chars}", bookId, apiUrl, json.Length);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
                using var response = await _bookApiClient.SendAsync(request, BookApiCallTimeoutKind.LongRunning, cts.Token);
                var responseData = await response.Content.ReadAsStringAsync(cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Cover API HTTP {Code}: {Body}", (int)response.StatusCode,
                        responseData?.Length > 800 ? responseData.Substring(0, 800) + "…" : responseData);
                    var errMsg = $"Cover service returned {(int)response.StatusCode}. ";
                    try
                    {
                        var ej = JObject.Parse(responseData ?? "{}");
                        var detail = ej["detail"]?.ToString() ?? ej["message"]?.ToString() ?? ej["error"]?.ToString();
                        if (!string.IsNullOrEmpty(detail)) errMsg += detail;
                        else errMsg += "Check API key and request body.";
                    }
                    catch { errMsg += "Check API key, network, and server logs."; }
                    return Json(new { success = false, message = errMsg });
                }

                var urls = ExtractCoverImageUrlsFromApiResponse(responseData);
                if (urls.Count > 0)
                    return Json(new { success = true, options = urls.ToArray() });

                _logger.LogWarning("Cover API success but no image URLs parsed. Body preview: {Body}",
                    responseData?.Length > 600 ? responseData.Substring(0, 600) + "…" : responseData);
                return Json(new
                {
                    success = false,
                    message = "Cover API returned OK but no image URLs were found. Ask admin to verify response JSON shape.",
                });
            }
            catch (TaskCanceledException)
            {
                return Json(new { success = false, message = "Cover generation timed out. Try again or use a smaller size." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generate cover API failed for bookId={BookId}", bookId);
                return Json(new { success = false, message = "Could not reach cover service: " + ex.Message });
            }
        }

        /// <summary>Edit an existing cover image via POST /api/edit-cover (base64 + direction).</summary>
        [HttpPost]
        [DisableRequestTimeout]
        public async Task<IActionResult> EditAICoverPreview([FromBody] JObject body)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Json(new { success = false, message = "Please sign in." });

            var encoded = body?["encoded_image"]?.ToString();
            var direction = body?["image_direction"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(encoded))
                return Json(new { success = false, message = "encoded_image is required." });

            var size = body?["size"]?.ToString();
            if (string.IsNullOrWhiteSpace(size)) size = _configuration["ExternalApi:CoverGenerateSize"] ?? "1024x1536";
            size = BookApiInputValidation.NormalizeSize(size, "1024x1536");

            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.EditCoverUrl, "/api/edit-cover").Trim();
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, message = ExternalApiKeyResolver.MissingKeyUserMessage });

            var payload = new JObject
            {
                ["encoded_image"] = encoded.Trim(),
                ["image_direction"] = direction.Trim(),
                ["size"] = size.Trim()
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                using var cts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
                using var response = await _bookApiClient.SendAsync(request, BookApiCallTimeoutKind.LongRunning, cts.Token);
                var responseData = await response.Content.ReadAsStringAsync(cts.Token);
                if (!response.IsSuccessStatusCode)
                    return Json(new { success = false, message = $"Edit cover HTTP {(int)response.StatusCode}" });
                var urls = ExtractCoverImageUrlsFromApiResponse(responseData);
                if (urls.Count > 0) return Json(new { success = true, options = urls.ToArray() });
                try
                {
                    var jo = JObject.Parse(responseData ?? "{}");
                    var b64 = jo["encoded_image"]?.ToString() ?? jo["image"]?.ToString();
                    if (!string.IsNullOrEmpty(b64))
                    {
                        var dataUrl = b64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? b64 : "data:image/png;base64," + b64;
                        return Json(new { success = true, options = new[] { dataUrl } });
                    }
                }
                catch { /* ignore */ }
                return Json(new { success = false, message = "Edit cover response had no usable image." });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Edit cover API failed");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>Save the selected cover image path to the book. Call after user clicks Finalize Cover.</summary>
        [HttpPost]
        public async Task<IActionResult> FinalizeCover(int bookId, [FromBody] IDictionary<string, string> body)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();

            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");

            var path = body != null && body.TryGetValue("coverPath", out var p) ? p : "";
            if (string.IsNullOrWhiteSpace(path)) return BadRequest("coverPath is required.");
            path = path.Trim();

            if (path.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    path = await SaveDataUrlCoverToUploadsAsync(sessionUserId.Value, bookId, path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not persist data URL cover for book {BookId}", bookId);
                    return BadRequest("Could not save cover image. Try generating again or use a file URL.");
                }
            }

            book.CoverImagePath = path;
            book.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            HttpContext.Session.SetString("CoverFinalized", "1");
            return Ok(new { success = true, path = book.CoverImagePath, paymentRequired = false, bookId, nextUrl = $"/publish?bookId={bookId}" });
        }

        /// <summary>Sidebar/detail payload for My Books — fresh cover URL and metadata.</summary>
        [HttpGet]
        public async Task<IActionResult> GetBookSidebarDetail(int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();

            var row = await (from b in _context.Books
                             join u in _context.Users on b.UserId equals u.UserId
                             where b.BookId == bookId && b.UserId == sessionUserId.Value
                             select new
                             {
                                 b.BookId,
                                 b.Title,
                                 b.Description,
                                 b.Status,
                                 b.Genre,
                                 b.WordCount,
                                 b.CoverImagePath,
                                 b.UpdatedAt,
                                 AuthorName = u.FullName
                             }).FirstOrDefaultAsync();

            if (row == null) return NotFound();

            var chCount = await _context.APIRawResponse
                .Where(r => r.UserId == sessionUserId.Value && r.BookId == bookId)
                .CountAsync();

            string cover = row.CoverImagePath ?? "";
            if (string.IsNullOrWhiteSpace(cover))
            {
                var fb = new[]
                {
                    "/images/books/the-bird.png",
                    "/images/books/good-things-are-up-ahead.png",
                    "/images/books/fairy-tale.png",
                    "/images/books/the-wizarding-chronicles.png",
                    "/images/books/the-cambers-of-secrets.png"
                };
                cover = fb[(row.BookId % fb.Length + fb.Length) % fb.Length];
            }
            if (!cover.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !cover.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                cover = cover.StartsWith("/") ? cover : "/" + cover;

            return Json(new
            {
                bookId = row.BookId,
                title = row.Title ?? "Untitled",
                description = row.Description ?? "",
                status = row.Status ?? "Draft",
                genre = row.Genre ?? "",
                wordCount = row.WordCount,
                chapterCount = chCount,
                coverUrl = cover,
                author = row.AuthorName ?? "",
                updatedAt = row.UpdatedAt
            });
        }

        /// <summary>Returns whether the book has been paid (downloads unlocked).</summary>
        [HttpGet]
        public async Task<IActionResult> GetBookPaymentStatus(int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound();
            return Json(new { paid = true });
        }

        /// <summary>Download hub for a book.</summary>
        [HttpGet]
        public async Task<IActionResult> BookDownloads(int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return RedirectToAction("UserLogin", "Account");
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");
            ViewBag.BookId = bookId;
            ViewBag.BookTitle = book.Title ?? "Your Book";
            return View();
        }

        /// <summary>Full API catalog (upstream + BFF). Markdown: docs/EXTERNAL_API.md</summary>
        [AllowAnonymous]
        [HttpGet]
        [Route("Books/ApiDocumentation")]
        public IActionResult ApiDocumentation()
        {
            var opt = _externalApiOptions.Value;
            var baseUrl = string.IsNullOrWhiteSpace(opt.BaseUrl)
                ? BookApiConstants.DefaultUpstreamBaseUrl
                : opt.BaseUrl.TrimEnd('/');
            var bff = (_configuration["App:PublicBaseUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
            return Json(ApiDocumentationCatalog.Build(baseUrl, bff));
        }

        /// <summary>Diagnostics: upstream base URL, key configured, queue probe (no secret returned).</summary>
        [AllowAnonymous]
        [HttpGet]
        [Route("Books/ExternalApiStatus")]
        public async Task<IActionResult> ExternalApiStatus()
        {
            var key = ExternalApiKeyResolver.Resolve(_configuration);
            var opt = _externalApiOptions.Value;
            var queueUrl = _bookApiClient.ResolveUrl(opt.QueueDataUrl, "/api/queue-data");
            string queueStatus = "not_tested";
            int? queueHttp = null;
            UpstreamQueueSnapshot? queueSnapshot = null;
            string? queueBlockReason = null;

            if (!string.IsNullOrEmpty(key))
            {
                queueSnapshot = await _queueProbe.TryGetSnapshotAsync(HttpContext.RequestAborted);
                if (queueSnapshot != null)
                {
                    queueStatus = "ok";
                    queueHttp = 200;
                    queueBlockReason = UpstreamQueueGuard.GetBlockReason(queueSnapshot, _configuration);
                }
                else
                {
                    queueStatus = "error";
                }
            }
            else
            {
                queueStatus = "skipped_no_key";
            }

            return Json(new
            {
                success = true,
                baseUrl = opt.BaseUrl,
                keyConfigured = !string.IsNullOrEmpty(key),
                keyLength = key?.Length ?? 0,
                queueUrl,
                queueProbe = queueStatus,
                queueHttpStatus = queueHttp,
                queue = queueSnapshot == null ? null : new
                {
                    running = queueSnapshot.Running,
                    waiting = queueSnapshot.Waiting,
                    maxConcurrent = queueSnapshot.MaxConcurrent,
                    totalRequests = queueSnapshot.TotalRequests,
                    isStuck = queueSnapshot.IsStuck,
                    isSaturated = queueSnapshot.IsSaturated,
                    blockReason = queueBlockReason
                },
                endpoints = new
                {
                    generateChapter = opt.GenerateUrl,
                    edit = opt.EditUrl,
                    approve = opt.ApproveUrl,
                    audio = opt.AudioUrl,
                    queueData = opt.QueueDataUrl,
                    generateCover = opt.GenerateCoverUrl,
                    editCover = opt.EditCoverUrl,
                    bookChaptersName = opt.BookChaptersNameUrl,
                    refineCoverPrompt = opt.RefineCoverPromptUrl,
                    suggestCoverPrompt = opt.SuggestCoverPromptFromHighlightsUrl
                }
            });
        }

        /// <summary>Live vs local parity: DB, uploads dir, OAuth, public URL, content root (no secrets).</summary>
        [AllowAnonymous]
        [HttpGet]
        [Route("Books/DeploymentStatus")]
        public async Task<IActionResult> DeploymentStatus()
        {
            var env = _hostEnvironment.EnvironmentName;
            var contentRoot = _hostEnvironment.ContentRootPath;
            var uploadsPath = Path.Combine(contentRoot, "wwwroot", "uploads");

            var uploadsWritable = false;
            var uploadsFileCount = 0;
            try
            {
                Directory.CreateDirectory(uploadsPath);
                var probe = Path.Combine(uploadsPath, ".write_probe");
                await System.IO.File.WriteAllTextAsync(probe, "ok", HttpContext.RequestAborted);
                System.IO.File.Delete(probe);
                uploadsWritable = true;
                uploadsFileCount = Directory.Exists(uploadsPath)
                    ? Directory.EnumerateFiles(uploadsPath, "*", SearchOption.AllDirectories).Count()
                    : 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DeploymentStatus uploads probe failed");
            }

            var dbOk = false;
            string? dbError = null;
            int bookCount = 0;
            try
            {
                dbOk = await _context.Database.CanConnectAsync(HttpContext.RequestAborted);
                if (dbOk)
                    bookCount = await _context.Books.CountAsync(HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                dbError = ex.Message;
            }

            var googleOk = !string.IsNullOrWhiteSpace(OAuthCredentialResolver.GoogleClientId(_configuration))
                           && !string.IsNullOrWhiteSpace(OAuthCredentialResolver.GoogleClientSecret(_configuration));
            var facebookOk = !string.IsNullOrWhiteSpace(OAuthCredentialResolver.FacebookAppId(_configuration))
                             && !string.IsNullOrWhiteSpace(OAuthCredentialResolver.FacebookAppSecret(_configuration));
            var apiKeyOk = !string.IsNullOrEmpty(ExternalApiKeyResolver.Resolve(_configuration));
            var publicBase = (_configuration["App:PublicBaseUrl"] ?? "").Trim();

            return Json(new
            {
                success = true,
                environment = env,
                contentRoot,
                publicBaseUrl = string.IsNullOrEmpty(publicBase) ? null : publicBase,
                database = new { connected = dbOk, bookCount, error = dbError },
                externalApi = new { keyConfigured = apiKeyOk },
                oauth = new { google = googleOk, facebook = facebookOk },
                uploads = new
                {
                    path = uploadsPath,
                    writable = uploadsWritable,
                    fileCount = uploadsFileCount,
                    note = "Cover images must live here; redeploy without persistent/uploads symlink breaks saved covers."
                },
                hints = new[]
                {
                    "Local and live use different MySQL unless you import the same database dump.",
                    "Set ConnectionStrings__DefaultConnection (or DATABASE_URL / MYSQL_URL) and App__PublicBaseUrl in /etc/default/ebookai.",
                    "Run: bash deploy/fix-live-parity.sh then bash deploy/do-deploy.sh on the server."
                }
            });
        }

        /// <summary>Pre-flight before chapter generate: API key + upstream queue (fast, &lt;10s).</summary>
        [HttpGet]
        [Route("Books/ChapterGenerationReady")]
        public async Task<IActionResult> ChapterGenerationReady()
        {
            if (HttpContext.Session.GetInt32("UserId") is not > 0)
                return Json(new { ok = false, canGenerate = false, message = "Please sign in again." });

            var key = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(key))
            {
                return Json(new
                {
                    ok = false,
                    canGenerate = false,
                    apiKeyConfigured = false,
                    message = ExternalApiKeyResolver.MissingKeyUserMessage
                });
            }

            var snapshot = await _queueProbe.TryGetSnapshotAsync(HttpContext.RequestAborted);
            var blockReason = UpstreamQueueGuard.GetBlockReason(snapshot, _configuration);
            if (!string.IsNullOrEmpty(blockReason))
            {
                return Json(new
                {
                    ok = false,
                    canGenerate = false,
                    apiKeyConfigured = true,
                    message = blockReason,
                    queue = snapshot == null ? null : new
                    {
                        running = snapshot.Running,
                        waiting = snapshot.Waiting,
                        isStuck = snapshot.IsStuck
                    }
                });
            }

            if (snapshot?.IsStuck == true)
                _logger.LogWarning("ChapterGenerationReady: upstream queue backed up (waiting={Waiting}, running={Running}).", snapshot.Waiting, snapshot.Running);
            else if (snapshot != null && snapshot.Waiting >= 5)
                _logger.LogInformation("ChapterGenerationReady: upstream queue depth {Waiting}.", snapshot.Waiting);

            return Json(new
            {
                ok = true,
                canGenerate = true,
                apiKeyConfigured = true,
                queue = snapshot == null ? null : new
                {
                    running = snapshot.Running,
                    waiting = snapshot.Waiting,
                    isStuck = snapshot.IsStuck
                }
            });
        }

        /// <summary>GET queue status from external API (running, waiting, max concurrent, total).</summary>
        [HttpGet]
        public async Task<IActionResult> GetQueueData()
        {
            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.QueueDataUrl, "/api/queue-data");
            try
            {
                using var httpReq = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                using var response = await _bookApiClient.SendAsync(httpReq, BookApiCallTimeoutKind.QueueProbe, HttpContext.RequestAborted);
                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return Content(json, "application/json");
                return StatusCode((int)response.StatusCode, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Queue data API failed.");
                return StatusCode(503, new { success = false, message = "Upstream queue API unreachable. Check ExternalApi__ApiKey and network to 162.229.248.26:8001.", detail = ex.Message });
            }
        }

        /// <summary>POST to external API to get 5 suggested chapter names. Returns suggest_chapter_name for UI.</summary>
        [HttpPost]
        public async Task<IActionResult> BookChaptersName([FromBody] JObject body)
        {
            var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.BookChaptersNameUrl, "/api/book_chapters_name");
            try
            {
                var content = new StringContent(body?.ToString() ?? "{}", Encoding.UTF8, "application/json");
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = content };
                using var response = await _bookApiClient.SendAsync(httpReq, BookApiCallTimeoutKind.Standard, HttpContext.RequestAborted);
                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return Content(json, "application/json");
                return StatusCode((int)response.StatusCode, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Book chapters name API failed.");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>Proxies POST /api/refine_cover_prompt to the FastAPI upstream.</summary>
        [HttpPost]
        public async Task<IActionResult> RefineCoverPrompt([FromBody] JObject? body)
        {
            if (HttpContext.Session.GetInt32("UserId") == null) return Unauthorized();
            if (body == null) return BadRequest(new { success = false, message = "Body required." });
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, message = ExternalApiKeyResolver.MissingKeyUserMessage });
            var url = _bookApiClient.ResolveUrl(_externalApiOptions.Value.RefineCoverPromptUrl, "/api/refine_cover_prompt");
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };
            using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
            using var resp = await _bookApiClient.SendAsync(req, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
            var json = await resp.Content.ReadAsStringAsync(upstreamCts.Token);
            if (resp.IsSuccessStatusCode)
                return Content(json, "application/json");
            return StatusCode((int)resp.StatusCode, json);
        }

        /// <summary>Proxies POST /api/suggest-cover-prompt-from-highlights to the FastAPI upstream.</summary>
        [HttpPost]
        public async Task<IActionResult> SuggestCoverPromptFromHighlights([FromBody] JObject? body)
        {
            if (HttpContext.Session.GetInt32("UserId") == null) return Unauthorized();
            if (body == null) return BadRequest(new { success = false, message = "Body required." });
            var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, message = ExternalApiKeyResolver.MissingKeyUserMessage });
            var url = _bookApiClient.ResolveUrl(_externalApiOptions.Value.SuggestCoverPromptFromHighlightsUrl, "/api/suggest-cover-prompt-from-highlights");
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json")
            };
            using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
            using var resp = await _bookApiClient.SendAsync(req, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
            var json = await resp.Content.ReadAsStringAsync(upstreamCts.Token);
            if (resp.IsSuccessStatusCode)
                return Content(json, "application/json");
            return StatusCode((int)resp.StatusCode, json);
        }

        // ========================== STYLING (requires purchased style feature) ===========================
        [HttpPost]
        public async Task<IActionResult> SaveStylePreferences(int bookId, [FromBody] object payload)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();

            // Minimal entitlement check: ensure user has any feature containing "style"
            var userIdString = sessionUserId.ToString();
            var hasStyle = await _context.Set<UserFeatures>()
                .Include(uf => uf.Feature)
                .Where(uf => uf.UserId == userIdString)
                .AnyAsync(uf => uf.Feature != null && 
                                (uf.Feature.Name.ToLower().Contains("style") ||
                                 uf.Feature.Key.ToLower().Contains("style")));
            if (!hasStyle)
            {
                return StatusCode(402, "Styling is a premium feature. Please purchase to continue.");
            }

            await UpsertSettingAsync($"book:{bookId}:style", payload?.ToString() ?? "{}", "Style");
            return Ok(new { success = true });
        }

        // ========================== PREVIEW (eBook/Print) ===========================
        [HttpGet]
        public async Task<IActionResult> GetPreviewHtml(int bookId, string mode = "ebook", string device = "kindle")
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");

            var chapters = await _context.Chapters
                .Where(c => c.BookId == bookId && c.ChapterNumber >= 0)
                .OrderBy(c => c.ChapterNumber)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.Append($"<div data-mode='{mode}' data-device='{device}'>");
            sb.Append($"<h1>{System.Net.WebUtility.HtmlEncode(book.Title)}</h1>");
            foreach (var ch in chapters)
            {
                var title = System.Net.WebUtility.HtmlEncode(ch.Title ?? $"Chapter {ch.ChapterNumber}");
                sb.Append($"<h2>{title}</h2>");
                sb.Append($"<div class='chapter'>{ch.Content}</div>");
            }
            sb.Append("</div>");
            return Content(sb.ToString(), "text/html");
        }

        // ========================== GENERATION (EPUB/PDF — real export bytes) ===========================
        [HttpPost]
        public async Task<IActionResult> GenerateFormats(int bookId, bool epub, bool pdf, CancellationToken cancellationToken)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value, cancellationToken);
            if (book == null) return NotFound("Book not found.");

            var details = await _bookService.GetBookDetailsForPreviewAsync(sessionUserId.Value, bookId);
            if (details == null || !details.Success || details.Chapters == null
                || !details.Chapters.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
                return BadRequest(new { success = false, message = "Add chapter content in AI Writer before exporting." });

            var exportOpt = await LoadExportOptionsForBookAsync(sessionUserId.Value, bookId, cancellationToken);
            var baseOut = $"/uploads/{sessionUserId}/books/{bookId}/output";
            var outRoot = Path.Combine(_hostEnvironment.WebRootPath ?? "wwwroot", "uploads", sessionUserId.Value.ToString(), "books", bookId.ToString(), "output");
            Directory.CreateDirectory(outRoot);

            string? epubPath = null;
            string? pdfPath = null;
            var title = (details.BookTitle ?? "book").Trim();
            var safe = System.Text.RegularExpressions.Regex.Replace(title, @"[^\w\-\s]", "");
            safe = System.Text.RegularExpressions.Regex.Replace(safe, @"\s+", "-").Trim('-');
            if (string.IsNullOrEmpty(safe)) safe = "book";

            try
            {
                if (epub)
                {
                    var epubBytes = await _epubExportService.BuildEpubAsync(
                        details, details.CoverImagePath, title, details.AuthorName ?? "", exportOpt, 0, "6 x 9 in", cancellationToken);
                    if (epubBytes == null || epubBytes.Length < 80)
                        return StatusCode(500, new { success = false, message = "EPUB generation produced an empty file." });
                    var epubFile = $"{safe}-{bookId}.epub";
                    epubPath = $"{baseOut}/{epubFile}";
                    await System.IO.File.WriteAllBytesAsync(Path.Combine(outRoot, epubFile), epubBytes, cancellationToken);
                    await UpsertSettingAsync($"book:{bookId}:output:epub", epubPath, "Output");
                }
                if (pdf)
                {
                    var pdfBytes = await _bookPdfService.RenderFullBookPdfAsync(
                        details, null, title, details.AuthorName, details.Genre, exportOpt, null, cancellationToken);
                    if (pdfBytes == null || pdfBytes.Length < 128 || pdfBytes[0] != (byte)'%')
                        return StatusCode(500, new { success = false, message = "PDF generation failed." });
                    var pdfFile = $"{safe}-{bookId}.pdf";
                    pdfPath = $"{baseOut}/{pdfFile}";
                    await System.IO.File.WriteAllBytesAsync(Path.Combine(outRoot, pdfFile), pdfBytes, cancellationToken);
                    await UpsertSettingAsync($"book:{bookId}:output:pdf", pdfPath, "Output");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GenerateFormats failed for book {BookId}", bookId);
                return StatusCode(500, new { success = false, message = "Export failed. Use Publish for the latest formatting." });
            }

            return Ok(new { success = true, epubPath, pdfPath });
        }

        // ========================== LOCK SCREEN: VERIFY PASSWORD ===========================
        [HttpPost]
        public async Task<IActionResult> VerifyPassword([FromBody] IDictionary<string, string> body)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            var pwd = body != null && body.TryGetValue("password", out var p) ? p : "";
            if (string.IsNullOrWhiteSpace(pwd)) return BadRequest("Password required.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value);
            if (user == null) return Unauthorized();

            // NOTE: Passwords are stored as plain text in this project.
            var ok = string.Equals(user.Password, pwd);
            return ok ? Ok(new { success = true }) : Unauthorized(new { success = false, message = "Invalid password." });
        }

        // ========================== HELPERS ===========================
        /// <summary>Persist a data:image/...;base64,... cover to wwwroot/uploads and return relative URL.</summary>
        private async Task<string> SaveDataUrlCoverToUploadsAsync(int userId, int bookId, string dataUrl)
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
            await System.IO.File.WriteAllBytesAsync(fullPath, bytes);
            return $"/uploads/{userId}/books/{bookId}/{fileName}";
        }

        private async Task UpsertSettingAsync(string key, string value, string category)
        {
            // Prevent MySQL "Data too long" when Value exceeds model MaxLength (1000), e.g. long AI cover directions
            if (key.Contains("aiCoverPrompt", StringComparison.OrdinalIgnoreCase))
                value = Settings.ClampValueLength(value, Settings.DbCompatMaxValueLength) ?? "";
            else if (key.Contains("aiCoverLastPreview", StringComparison.OrdinalIgnoreCase)
                     && (value ?? string.Empty).Length > Settings.DbCompatMaxValueLength)
                return; /* skip save — exceeds VARCHAR(1000); run ALTER TABLE `Settings` MODIFY COLUMN `Value` LONGTEXT NULL */

            var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key);
            if (setting == null)
            {
                var nextId = await _context.NextSettingIdAsync(CancellationToken.None);
                _context.Settings.Add(new Settings
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
            await _context.SaveChangesAsync();
        }

        /// <summary>Persist EPUB under uploads/{userId}/books/{bookId}/epub for the published carousel.</summary>
        private async Task SavePublishedEpubAsync(int userId, int bookId, string fileName, byte[] bytes, CancellationToken ct)
        {
            var webRoot = _hostEnvironment.WebRootPath ?? "";
            if (string.IsNullOrEmpty(webRoot) || bytes.Length == 0) return;

            var epubDir = Path.Combine(webRoot, "uploads", userId.ToString(), "books", bookId.ToString(), "epub");
            Directory.CreateDirectory(epubDir);

            foreach (var old in Directory.EnumerateFiles(epubDir, "*.epub"))
            {
                try { System.IO.File.Delete(old); } catch { /* replace prior export */ }
            }

            var fullPath = Path.Combine(epubDir, fileName);
            await System.IO.File.WriteAllBytesAsync(fullPath, bytes, ct);

            var rel = $"/uploads/{userId}/books/{bookId}/epub/{fileName}";
            await UpsertSettingAsync($"book:{bookId}:epubFilePath", rel, "Book");
        }

    }
}
