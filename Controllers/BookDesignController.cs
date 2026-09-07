using EBookDashboard.Configuration;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.ViewModels;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CognitiveServices.Speech.Transcription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace EBookDashboard.Controllers
{
    //[ApiController]
    public class BookDesignController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IBookDesignService _bookDesignService;
        private readonly IBookService _bookService;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _configuration;
        private readonly BookFlowStateService _bookFlow;
        private readonly ILogger<BookDesignController> _logger;
        private readonly IBookRenderService _bookRenderService;
        private readonly IPrintWrapPregenerationQueue _printWrapPregenerationQueue;

        public BookDesignController(
            ApplicationDbContext context,
            IBookDesignService bookDesignService,
            IBookService bookService,
            IWebHostEnvironment env,
            IConfiguration configuration,
            BookFlowStateService bookFlow,
            ILogger<BookDesignController> logger,
            IBookRenderService bookRenderService,
            IPrintWrapPregenerationQueue printWrapPregenerationQueue)
        {
            _context = context;
            _bookDesignService = bookDesignService ?? throw new ArgumentNullException(nameof(bookDesignService));
            _bookService = bookService ?? throw new ArgumentNullException(nameof(bookService));
            _env = env;
            _configuration = configuration;
            _bookFlow = bookFlow;
            _logger = logger;
            _bookRenderService = bookRenderService;
            _printWrapPregenerationQueue = printWrapPregenerationQueue;
        }
        // GET: /BookDesign/CoverDesignCalculator
        public IActionResult Index(int bookId = 0)
        {
            int userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);

            if (userId == 0)
            {
                return RedirectToAction("UserLogin", "Account");
            }

            ViewBag.UserId = userId;
            ViewBag.SelectedBookId = bookId;

            return RedirectToAction(nameof(CoverDesignCalculatorFixing), new { bookId });
        }        
        // GET: Returns the Cover Design Calculator View
        public IActionResult CoverDesignCalculatorView(int bookId = 0)
        {
            try
            {
                int userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId"));
                if (userId == 0)
                {
                    return RedirectToAction("UserLogin", "Account");
                }

                // Soft empty studio: open classic CoverDesignCalculatorFixing (bookId=0).
                ViewBag.UserId = userId;
                ViewBag.SelectedBookId = bookId;
                return RedirectToAction(nameof(CoverDesignCalculatorFixing), new { bookId });
            }
            catch (Exception)
            {
                return View("Error");
            }
        }
        //==========================================================
        //============ Step 1: Load Books in DropDown ==============
        //==========================================================
        [HttpGet]
        public async Task<IActionResult> GetSavedCoverTemplates(int userId)
        {
            if (userId == 0)
                return BadRequest("UserId is required.");

            try
            {
                var savedBooksCovers = await _bookDesignService.GetSavedBooksCoversForDropdownAsync();

                // Transform to the expected format with formatted date
                //var result = savedBooksCovers.Select(b => new
                //{
                //    //userId = b.UserId,
                //    //bookId = b.BookId,
                //    Title = b.Title,
                //    Description = b.Description,
                //    BookCoverPagePath = b.BookCoverPagePath
                //}).ToList();

                Console.WriteLine($"🔍 GetSavedResponses: Found books for user {userId}");
                return Json(savedBooksCovers);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ GetSavedResponses error: {ex.Message}");
                return Json(new { error = ex.Message });
            }
        }

        //=====================================================
        // Get user's books for formatting dropdown
        //=====================================================
        [HttpGet]
        public async Task<IActionResult> GetUserBooksForFormatting()
        {
            try
            {
                var userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
                if (userId == 0)
                    return Json(new { success = false, message = "Not logged in" });

                var books = await _context.Books
                    .Where(b => b.UserId == userId)
                    .OrderByDescending(b => b.CreatedAt)
                    .Select(b => new { bookId = b.BookId, title = b.Title })
                    .ToListAsync();

                return Json(new { success = true, books });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// GET: List of current user's books for dropdown (uses session UserId).
        /// Use this from Book Formatter page so userId is never wrong.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetMyBooks()
        {
            try
            {
                var userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
                if (userId == 0)
                    return Json(new List<object>());

                var books = await _context.Books
                    .Where(b => b.UserId == userId)
                    .OrderByDescending(b => b.CreatedAt)
                    .Select(b => new
                    {
                        userId = b.UserId,
                        bookId = b.BookId,
                        bookTitle = b.Title ?? "Untitled"
                    })
                    .ToListAsync();
                return Json(books);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetMyBooks error: " + ex.Message);
                return Json(new List<object>());
            }
        }

        /// <summary>Loads full book chapters for the Book Formatter preview (session-scoped).</summary>
        [HttpGet]
        public async Task<IActionResult> GetFormatterBookContent(int bookId)
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                if (!userId.HasValue || userId.Value <= 0)
                    return Unauthorized(new { success = false, message = "Please sign in." });

                if (bookId <= 0)
                    return Json(new { success = false, message = "Book ID is required." });

                var result = await _bookService.GetBookDetailsForPreviewAsync(userId.Value, bookId);
                if (result == null || !result.Success)
                    return Json(new { success = false, message = result?.Message ?? "No book found." });

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
                    bookContentHtml = result.BookContentHtml ?? "",
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
                _logger.LogError(ex, "GetFormatterBookContent failed for book {BookId}", bookId);
                return Json(new { success = false, message = "Could not load book content." });
            }
        }

        //=====================================================
        // GET: Load BookFormatting for a book
        //=====================================================
        [HttpGet]
        public async Task<IActionResult> GetBookFormatting(int bookId)
        {
            try
            {
                var userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
                if (userId == 0)
                    return Json(new { success = false, message = "Not logged in" });

                var fmt = await _context.BookFormatting
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == userId);
                var draftKey = $"book:{bookId}:formattingDraft";
                var draft = await _context.Settings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == draftKey);
                string lineSpacing = fmt?.LineSpacing ?? "1.6";
                string publishingPlatformDraft = "";
                if (!string.IsNullOrWhiteSpace(draft?.Value))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(draft.Value);
                        if (string.IsNullOrWhiteSpace(lineSpacing) && doc.RootElement.TryGetProperty("lineSpacing", out var lsProp))
                        {
                            var v = lsProp.GetString();
                            if (!string.IsNullOrWhiteSpace(v)) lineSpacing = v!;
                        }
                        if (doc.RootElement.TryGetProperty("publishingPlatform", out var ppProp))
                        {
                            var pv = ppProp.GetString();
                            if (!string.IsNullOrWhiteSpace(pv)) publishingPlatformDraft = pv.Trim();
                        }
                    }
                    catch
                    {
                        // ignore malformed draft state; UI falls back to defaults
                    }
                }

                if (string.IsNullOrWhiteSpace(lineSpacing)) lineSpacing = "1.6";

                var publishingPlatform = fmt?.PublishingPlatform ?? "";
                if (string.IsNullOrWhiteSpace(publishingPlatform) && !string.IsNullOrWhiteSpace(fmt?.PublishingPlatforms))
                    publishingPlatform = fmt.PublishingPlatforms.Split(',')[0].Trim();
                if (string.IsNullOrWhiteSpace(publishingPlatform)) publishingPlatform = publishingPlatformDraft;

                if (fmt == null)
                {
                    return Json(new
                    {
                        success = true,
                        data = new
                        {
                            format = "Ebook",
                            interiorStyle = "Novel",
                            textSize = "Medium",
                            publishingPlatforms = "",
                            publishingPlatform = "",
                            lineSpacing,
                            draftStateJson = draft?.Value ?? ""
                        }
                    });
                }

                return Json(new
                {
                    success = true,
                    data = new
                    {
                        format = fmt.Format,
                        interiorStyle = fmt.InteriorStyle,
                        textSize = fmt.TextSize,
                        publishingPlatforms = fmt.PublishingPlatforms ?? "",
                        publishingPlatform,
                        lineSpacing,
                        draftStateJson = draft?.Value ?? ""
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        //=====================================================
        // GET: Canonical export HTML (same document as PDF download)
        //=====================================================
        [HttpGet]
        [Route("BookDesign/PreviewBookHtml")]
        public async Task<IActionResult> PreviewBookHtml(int bookId, CancellationToken cancellationToken)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized("Please sign in.");

            if (bookId <= 0) return BadRequest("BookId is required.");

            var owns = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId && b.UserId == userId.Value, cancellationToken);
            if (!owns) return NotFound("Book not found.");

            var details = await _bookService.GetBookDetailsForPreviewAsync(userId.Value, bookId);
            if (details == null || !details.Success)
                return BadRequest(details?.Message ?? "Could not load book.");

            var draftRow = await _context.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:formattingDraft", cancellationToken);
            var fmtRow = await _context.BookFormatting.AsNoTracking()
                .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == userId.Value, cancellationToken);
            var exportOpt = BookPdfExportOptions.LoadFromPersistence(fmtRow, draftRow?.Value);

            // Published read mode always opens with the front cover (full reader experience).
            // KDP interior PDF export omits the cover separately via DownloadBookInteriorPdf.
            exportOpt.IncludeCoverPage = true;

            var userRow = await _context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);
            var publisher = userRow?.FullName;
            if (string.IsNullOrWhiteSpace(publisher)) publisher = userRow?.UserEmail;

            try
            {
                var render = await _bookRenderService.BuildBookHtmlAsync(new BookRenderRequest
                {
                    Details = details,
                    ExportOptions = exportOpt,
                    DisplayTitle = details.BookTitle,
                    DisplayAuthor = details.AuthorName,
                    DisplayGenre = details.Genre,
                    PublisherDisplayName = publisher
                }, cancellationToken);

                Response.Headers["Cache-Control"] = "no-store";
                return Content(render.Html, "text/html; charset=utf-8");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PreviewBookHtml failed for book {BookId}", bookId);
                return StatusCode(500, "Could not build preview.");
            }
        }

        //=====================================================
        // POST: Save BookFormatting
        //=====================================================
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SaveBookFormatting([FromBody] SaveBookFormattingRequest req)
        {
            try
            {
                var userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
                if (userId == 0)
                    return Json(new { success = false, message = "Not logged in" });

                if (req == null || req.BookId <= 0)
                    return Json(new { success = false, message = "Invalid request" });

                if (!string.IsNullOrWhiteSpace(req.BookTitle))
                    await BookTitleResolver.SyncBookTitleAsync(_context, userId, req.BookId, req.BookTitle);

                var existing = await _context.BookFormatting
                    .FirstOrDefaultAsync(f => f.BookId == req.BookId && f.UserId == userId);

                if (existing == null)
                {
                    existing = new BookFormatting
                    {
                        BookId = req.BookId,
                        UserId = userId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.BookFormatting.Add(existing);
                }

                var draftOpt = BookPdfExportOptions.FromDraftJson(req.DraftStateJson);

                if (!string.IsNullOrWhiteSpace(req.InteriorStyle))
                    existing.InteriorStyle = InteriorExportTheme.NormalizeInteriorStyle(req.InteriorStyle);
                else if (!string.IsNullOrWhiteSpace(draftOpt.InteriorStyle))
                    existing.InteriorStyle = draftOpt.InteriorStyle;
                else if (string.IsNullOrWhiteSpace(existing.InteriorStyle))
                    existing.InteriorStyle = "Novel";

                if (!string.IsNullOrWhiteSpace(req.TextSize))
                    existing.TextSize = InteriorExportTheme.NormalizeTextSize(req.TextSize);
                else if (!string.IsNullOrWhiteSpace(draftOpt.TextSize))
                    existing.TextSize = draftOpt.TextSize;
                else if (string.IsNullOrWhiteSpace(existing.TextSize))
                    existing.TextSize = "Medium";

                if (!string.IsNullOrWhiteSpace(req.LineSpacing))
                    existing.LineSpacing = InteriorExportTheme.NormalizeLineSpacing(req.LineSpacing.Trim());
                else if (!string.IsNullOrWhiteSpace(draftOpt.LineSpacing))
                    existing.LineSpacing = draftOpt.LineSpacing;
                else if (string.IsNullOrWhiteSpace(existing.LineSpacing))
                    existing.LineSpacing = "1.6";

                if (!string.IsNullOrWhiteSpace(req.Format))
                    existing.Format = req.Format;
                else if (!string.IsNullOrWhiteSpace(draftOpt.Format))
                    existing.Format = draftOpt.Format;
                else if (string.IsNullOrWhiteSpace(existing.Format))
                    existing.Format = "Ebook";

                existing.PublishingPlatforms = req.PublishingPlatforms ?? existing.PublishingPlatforms ?? "";
                var primaryPlatform = string.IsNullOrWhiteSpace(req.PublishingPlatform)
                    ? (existing.PublishingPlatforms ?? "").Split(',')[0].Trim()
                    : req.PublishingPlatform.Trim();
                existing.PublishingPlatform = string.IsNullOrWhiteSpace(primaryPlatform) ? null : primaryPlatform;
                existing.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                var previewAccent = draftOpt.PreviewAccent;
                var pageBackgroundColor = draftOpt.PageBackgroundColor
                    ?? InteriorExportTheme.ResolveDefaultPageBackground(existing.InteriorStyle);

                var statePayload = JsonSerializer.Serialize(new
                {
                    format = existing.Format ?? "Ebook",
                    interiorStyle = existing.InteriorStyle ?? "Novel",
                    textSize = existing.TextSize ?? "Medium",
                    lineSpacing = existing.LineSpacing ?? "1.6",
                    publishingPlatforms = existing.PublishingPlatforms ?? "",
                    publishingPlatform = existing.PublishingPlatform ?? primaryPlatform ?? "",
                    previewAccent,
                    pageBackgroundColor,
                    previewPageCount = ResolvePreviewPageCount(req, req.DraftStateJson),
                    savedAtUtc = DateTime.UtcNow
                });
                var key = $"book:{req.BookId}:formattingDraft";
                var draftSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key);
                if (draftSetting == null)
                {
                    var nextId = await _context.NextSettingIdAsync();
                    _context.Settings.Add(new Settings
                    {
                        SettingId = nextId,
                        Key = key,
                        Value = Settings.ClampValueLength(statePayload, Settings.DbCompatMaxValueLength) ?? "",
                        Category = "Book",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    draftSetting.Value = Settings.ClampValueLength(statePayload, Settings.DbCompatMaxValueLength) ?? "";
                    draftSetting.Category = "Book";
                    draftSetting.UpdatedAt = DateTime.UtcNow;
                    _context.Settings.Update(draftSetting);
                }

                await _context.SaveChangesAsync();

                var previewPages = ResolvePreviewPageCount(req, statePayload);
                var maxPc = Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount;
                if (previewPages is >= 1 && previewPages.Value <= maxPc)
                {
                    var pageKey = $"book:{req.BookId}:printReadyPageCount";
                    var pageSetting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == pageKey);
                    var pageValue = previewPages.Value.ToString();
                    if (pageSetting == null)
                    {
                        var nextId = await _context.NextSettingIdAsync();
                        _context.Settings.Add(new Settings
                        {
                            SettingId = nextId,
                            Key = pageKey,
                            Value = pageValue,
                            Category = "Book",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        pageSetting.Value = pageValue;
                        pageSetting.Category = "Book";
                        pageSetting.UpdatedAt = DateTime.UtcNow;
                        _context.Settings.Update(pageSetting);
                    }
                    await _context.SaveChangesAsync();
                }

                HttpContext.Session.SetString("FormattingDone", "1");
                HttpContext.Session.SetString("HasGeneratedBook", "1");
                HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, req.BookId);
                var fmt = existing.Format ?? "Ebook";
                HttpContext.Session.SetString("LastSelectedFormat", fmt);
                HttpContext.Session.SetString("BookFormatPremiumBoth",
                    string.Equals(fmt, "Both", StringComparison.OrdinalIgnoreCase) ? "1" : "0");
                var formatPath = ResolveFormatPath(fmt, existing.PublishingPlatform, existing.PublishingPlatforms);
                await _bookFlow.SaveStepAsync(req.BookId, BookFlowStateService.StepFormat, formatPath);

                if (fmt.Equals("Paperback", StringComparison.OrdinalIgnoreCase)
                    || fmt.Equals("Both", StringComparison.OrdinalIgnoreCase))
                {
                    var frontKeys = new[]
                    {
                        $"book:{req.BookId}:printReadyCoverFront",
                        $"book:{req.BookId}:aiCoverLastPreview"
                    };
                    var hasFront = await _context.Settings.AsNoTracking()
                        .AnyAsync(s => frontKeys.Contains(s.Key) && s.Value != null && s.Value.Trim() != "", cancellationToken: default);
                    if (!hasFront)
                    {
                        hasFront = await _context.Books.AsNoTracking()
                            .AnyAsync(b => b.BookId == req.BookId && b.UserId == userId
                                && b.CoverImagePath != null && b.CoverImagePath.Trim() != "", cancellationToken: default);
                    }
                    if (hasFront)
                        _printWrapPregenerationQueue.QueueAfterFrontCoverSaved(userId, req.BookId);
                }

                return Json(new { success = true, message = "Formatting saved.", previewPageCount = previewPages });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        //=====================================================
        // GET: Load cover design data for a specific book
        //=====================================================
        [HttpGet]
        public async Task<IActionResult> GetCoverDesignByBook(int bookId)
        {
            var userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId"));

            if (userId == 0)
            {
                return Json(new { success = false, message = "User not logged in" });
            }
            if (bookId == 0)
            {
                bookId = 12;
            }
            try
            {
               var response = await _bookDesignService.GetCoverDesignCalculator(userId, bookId);
                if (response == null)
                    return Json(new { success = false, message = "No response found." });

                return Json(new
                {
                    success = true,
                    bookId = response.BookId,
                    coverId = response.CoverId,
                    bookDetails = response.BookDetails,
                    bindingType = response.BindingType,
                    interiorType = response.InteriorType,
                    paperType = response.PaperType,
                    pageTurnDirection = response.PageTurnDirection,
                    measurementUnits = response.MeasurementUnits,
                    trimSize = response.TrimSize,
                    pagesCount = response.PagesCount,
                    coverTemplate = response.CoverTemplate,
                    coverTemplateImagePath = response.CoverTemplateImagePath,
                    width = response.Width,
                    height = response.Height,
                    safeArea = response.SafeArea,
                    spineWidth = response.SpineWidth,
                    spineTitle = response.SpineTitle,
                    spineDescription = response.SpineDescription,
                    bookVolume = response.BookVolume,
                    createdAt = EBookDashboard.Services.FriendlyDateFormatter.Format(response.CreatedAt),
                    status = response.Status,
                    IsActive = response.isActive
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
    
        }
        //===========================================
        // POST: Save/Update cover design data
        //===========================================
        [HttpPost]
        public async Task<IActionResult> SaveCoverDesign([FromBody] CoverDesignSaveDto dto)
        {
            if (dto == null)
            {
                return Json(new { success = false, message = "Invalid data" });
            }
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;

            if (userId == 0)
            {
                return Json(new { success = false, message = "User not logged in" });
            }
            if (dto.BookId <= 0)
            {
                return Json(new { success = false, message = "Please select a book before saving." });
            }
            try
            {
               
              // Check if record already exists
                var existingRecord = await _context.coverDesignCalculator
                    .FirstOrDefaultAsync(c => c.UserId == userId && c.BookId == dto.BookId && c.isActive);

                if (existingRecord == null)
                {
                    existingRecord = new CoverDesignCalculator
                    {
                        UserId = userId,
                        BookId = dto.BookId,
                        CreatedAt = DateTime.UtcNow,
                        Status = "Updated",
                        isActive=true
                    };
                    _context.coverDesignCalculator.Add(existingRecord);
                }
                // Map DTO → Entity
                existingRecord.BindingType = dto.BindingType;
                    existingRecord.InteriorType = dto.InteriorType;
                    existingRecord.PaperType = dto.PaperType;
                    existingRecord.PageTurnDirection = dto.PageTurnDirection;
                    existingRecord.MeasurementUnits = dto.MeasurementUnits;
                    existingRecord.TrimSize = dto.TrimSize;
                    existingRecord.PagesCount = dto.PagesCount;
                    existingRecord.Width = dto.Width;
                    existingRecord.Height = dto.Height;
                    existingRecord.SafeArea = dto.SafeArea;
                    existingRecord.SpineWidth = dto.SpineWidth;
                    //existingRecord.SpineTitle = dto.SpineTitle;
                    //existingRecord.SpineDescription = dto.SpineDescription;
                    existingRecord.Status = "Updated";
                    existingRecord.CreatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Cover design saved successfully." });
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                if (ex.InnerException != null)
                    msg += " " + ex.InnerException.Message;
                return Json(new { success = false, message = msg });
            }
        }
        // POST: Calculate dimensions
        [HttpPost]
        public async Task<IActionResult> CalculateDimensions([FromBody] CalculationRequest request)
        {
            try
            {
                // Your calculation logic here
                var result = await CalculateCoverDimensions(request);

                return Json(new { success = true, data = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task<object> CalculateCoverDimensions(CalculationRequest request)
        {
            // Add your calculation logic here
            // This is a placeholder - implement your actual calculation
            return new
            {
                width = "13.5",
                height = "9.25",
                spineWidth = "0.5",
                safeArea = "0.125"
            };
        }
        //==============================================
        //       On Page Load
        // Cover Design Calculator1 for a user and book
        //==============================================
        [HttpGet]
        public async Task<IActionResult> CoverDesign1(int bookId = 12)    // Default value 12
        {

            // Create and populate ViewModel
            var viewModel = new CoverDesignCalculatorVM
            {
                BookId = bookId,
               
            };
            return View("CoverDesign1", viewModel);
        }
            //==============================================
            //       On Page Load
            // Cover Design Calculator1 for a user and book
            //==============================================
            [HttpGet]
        public async Task<IActionResult> CoverDesignCalculatorFixing(int bookId = 0, string? format = null, string? guided = null, string? entry = null)
        {
            try
            {
                int userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
                if (userId == 0)
                {
                    return RedirectToAction("UserLogin", "Account");
                }
                ViewBag.GuidedFlow = string.Equals(guided, "1", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(entry, "direct", StringComparison.OrdinalIgnoreCase);
                // Get bookId from TempData, query param, or last selected from session
                if (bookId == 0)
                    bookId = Convert.ToInt32(TempData["NextStepBookId"] ?? 0);
                if (bookId == 0)
                {
                    var q = Request.Query["bookId"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(q) && int.TryParse(q, out var qBookId))
                        bookId = qBookId;
                }
                // Soft modules: empty Formatting studio when no book in URL (upload creates book).
                // Never fall back to LastSelectedBookId — book comes from URL / Continue Editing only.
                if (bookId <= 0)
                {
                    var emptyBooks = await _context.Books
                        .Where(b => b.UserId == userId)
                        .OrderByDescending(b => b.CreatedAt)
                        .Select(b => new BookDropdownItem { BookId = b.BookId, Title = b.Title })
                        .ToListAsync();
                    var emptyFormat = string.IsNullOrWhiteSpace(format) ? "Paperback" : format.Trim();
                    if (emptyFormat.Equals("Print", StringComparison.OrdinalIgnoreCase)
                        || emptyFormat.Equals("Hardcover", StringComparison.OrdinalIgnoreCase)
                        || emptyFormat.Equals("Hardback", StringComparison.OrdinalIgnoreCase))
                        emptyFormat = "Paperback";
                    else if (!emptyFormat.Equals("Ebook", StringComparison.OrdinalIgnoreCase)
                             && !emptyFormat.Equals("Both", StringComparison.OrdinalIgnoreCase)
                             && !emptyFormat.Equals("Paperback", StringComparison.OrdinalIgnoreCase))
                        emptyFormat = "Paperback";

                    ViewBag.UserId = userId;
                    ViewBag.SelectedBookId = 0;
                    ViewBag.SelectedBookName = "";
                    ViewBag.SelectedFormat = emptyFormat;
                    ViewBag.FlowBookId = 0;
                    ViewBag.FlowStep = BookFlowStateService.StepFormat;
                    ViewBag.FlowPath = ResolveFormatPath(emptyFormat, null, null);
                    ViewBag.FlowBackUrl = "/Books/Writer";
                    var uEmpty = await _context.Users.AsNoTracking()
                        .Where(u => u.UserId == userId)
                        .Select(u => new { u.FullName, u.UserEmail })
                        .FirstOrDefaultAsync();
                    ViewBag.DisplayAuthorName = string.IsNullOrWhiteSpace(uEmpty?.FullName)
                        ? (uEmpty?.UserEmail ?? "")
                        : uEmpty!.FullName!.Trim();
                    ApplyPrintReadyKdpViewBag();

                    List<BookCoverPages> emptyCoverPages;
                    try
                    {
                        emptyCoverPages = await _context.BookCoverPages.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
                    }
                    catch
                    {
                        emptyCoverPages = new List<BookCoverPages>();
                    }
                    ViewBag.BookCoverPages = emptyCoverPages;

                    var emptyVm = new CoverDesignCalculatorVM
                    {
                        BookId = 0,
                        UserId = userId,
                        Title = "",
                        BindingType = "paperback",
                        InteriorType = "bw",
                        PaperType = "white",
                        MeasurementUnits = "inches",
                        TrimSize = "6x9",
                        Format = emptyFormat,
                        InteriorStyle = "Novel",
                        TextSize = "Medium",
                        LineSpacing = "1.6",
                        PublishingPlatforms = "",
                        UserBooks = emptyBooks,
                        BookCoverPages = emptyCoverPages
                    };
                    return View("CoverDesignCalculatorFixing", emptyVm);
                }

                var bookRow = await _context.Books.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
                if (bookRow == null)
                {
                    TempData["InfoMessage"] = "That book was not found. Choose a project from the Dashboard.";
                    return RedirectToAction("Index", "Dashboard");
                }

                var isReEditableBook = BookPublishReadinessService.IsPublishReadyBookStatus(bookRow.Status);
                var (savedFlowStep, savedFlowPath) = await _bookFlow.GetStepAsync(bookId);
                BookResumeUrlHelper.BootstrapOwnedBookSession(HttpContext, bookId, bookRow.Status, savedFlowStep);

                // Soft modules: never lock Formatting behind AI writing.
                // User opened Book Formatting explicitly ΓÇö do not bounce back to AI Writer when flow is still on generate.
                if (BookFlowStateService.StepRank(savedFlowStep) < BookFlowStateService.StepRank(BookFlowStateService.StepFormat))
                {
                    var earlyFormat = format;
                    if (string.IsNullOrWhiteSpace(earlyFormat))
                        earlyFormat = Request.Query["format"].FirstOrDefault();
                    var earlyPath = ResolveFormatPath(earlyFormat, null, null);
                    try
                    {
                        await _bookFlow.SaveStepAsync(bookId, BookFlowStateService.StepFormat, earlyPath);
                    }
                    catch (Exception flowEx)
                    {
                        _logger.LogWarning(flowEx, "Early SaveStepAsync failed for book {BookId}", bookId);
                    }
                    savedFlowStep = BookFlowStateService.StepFormat;
                    savedFlowPath = earlyPath;
                }

                HttpContext.Session.SetString("HasGeneratedBook", "1");
                HttpContext.Session.SetInt32("LastSelectedBookId", bookId);
                HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);
                // Get format from query (PDF/EPUB/MOBI from AIGenerateBook format buttons)
                var formatQuery = Request.Query["format"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(formatQuery))
                    format = formatQuery.Trim();
                if (!string.IsNullOrWhiteSpace(format))
                    HttpContext.Session.SetString("LastSelectedFormat", format);
                ViewBag.UserId = userId;
                ViewBag.SelectedBookId = bookId;
                ViewBag.SelectedFormat = format;
                var uRow = await _context.Users.AsNoTracking()
                    .Where(u => u.UserId == userId)
                    .Select(u => new { u.FullName, u.UserEmail })
                    .FirstOrDefaultAsync();
                ViewBag.DisplayAuthorName = string.IsNullOrWhiteSpace(uRow?.FullName)
                    ? (uRow?.UserEmail ?? "")
                    : uRow!.FullName!.Trim();

                // Normalize format from URL for dropdown: Ebook, Paperback, Both
                if (!string.IsNullOrWhiteSpace(format))
                {
                    if (format.Equals("Print", StringComparison.OrdinalIgnoreCase))
                        format = "Paperback";
                    else if (!format.Equals("Ebook", StringComparison.OrdinalIgnoreCase) && !format.Equals("Both", StringComparison.OrdinalIgnoreCase) && !format.Equals("Paperback", StringComparison.OrdinalIgnoreCase))
                        format = "Ebook"; // PDF/EPUB/MOBI -> Ebook
                }

                // Load user's books for dropdown (when no book passed or for switching)
                var userBooks = await _context.Books
                    .AsNoTracking()
                    .Where(b => b.UserId == userId)
                    .OrderByDescending(b => b.CreatedAt)
                    .Select(b => new BookDropdownItem { BookId = b.BookId, Title = b.Title })
                    .ToListAsync();

                // Get book title when we have a book. Do not force payment here:
                // this screen is for formatting/cover preparation and should remain accessible.
                var bookTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                    _context, userId, bookRow.BookId, bookRow.Title);
                if (string.IsNullOrWhiteSpace(bookTitle))
                    bookTitle = "No Book Selected";

                var previewDetails = await _bookService.GetBookDetailsForPreviewAsync(userId, bookId);
                if (previewDetails is { Success: true })
                {
                    ViewBag.InitialBookPayloadJson = JsonSerializer.Serialize(new
                    {
                        success = true,
                        bookId = previewDetails.BookId,
                        bookTitle = previewDetails.BookTitle,
                        subtitle = previewDetails.Subtitle ?? "",
                        description = previewDetails.Description ?? "",
                        genre = previewDetails.Genre ?? "",
                        authorName = previewDetails.AuthorName ?? "",
                        coverImagePath = previewDetails.CoverImagePath ?? "",
                        totalChapters = previewDetails.TotalChapters,
                        chapters = previewDetails.Chapters
                            .OrderBy(c => c.ChapterNumber)
                            .Select(c => new
                            {
                                chapterNo = c.ChapterNumber,
                                chapterTitle = c.Title,
                                content = c.Content ?? ""
                            })
                            .ToList()
                    });
                }

                // Get data from service (may be null if no record saved yet)
                var response = await _bookDesignService.GetCoverDesignCalculator(userId, bookId);

                // Load BookFormatting for this book (format, interior style, text size, platforms)
                // If table does not exist yet, use defaults so the page still loads
                BookFormatting formatting = null;
                try
                {
                    formatting = await _context.BookFormatting
                        .AsNoTracking()
                        .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == userId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("BookFormatting load failed (table may not exist): " + ex.Message);
                }

                // Preferred format: from query (Ebook/Paperback/Both) or saved formatting
                var preferredFormat = formatting?.Format ?? "Ebook";
                if (string.Equals(formatting?.Format, "Print", StringComparison.OrdinalIgnoreCase))
                    preferredFormat = "Paperback";
                if (!string.IsNullOrWhiteSpace(format))
                    preferredFormat = format; // URL wins: Ebook, Paperback, Both

                // Create and populate ViewModel - use defaults when response is null
                var viewModel = new CoverDesignCalculatorVM
                {
                    BookId = bookId,
                    UserId = userId,
                    Title = bookTitle,
                    CoverDesignData = response,

                    // Form properties
                    BindingType = response?.BindingType ?? "paperback",
                    InteriorType = response?.InteriorType ?? "bw",
                    PaperType = response?.PaperType ?? "white",
                    MeasurementUnits = response?.MeasurementUnits ?? "inches",
                    TrimSize = response?.TrimSize ?? "6x9",
                    PagesCount = response?.PagesCount ?? 0,
                    Width = response?.Width ?? 0m,
                    Height = response?.Height ?? 0m,
                    SafeArea = response?.SafeArea ?? 0m,
                    SpineWidth = response?.SpineWidth ?? 0m,
                    SpineTitle = response?.SpineTitle,
                    SpineDescription = response?.SpineDescription,

                    // BookFormatting fields (use preferred format from query when coming from format buttons)
                    Format = preferredFormat,
                    InteriorStyle = formatting?.InteriorStyle ?? "Novel",
                    TextSize = formatting?.TextSize ?? "Medium",
                    LineSpacing = string.IsNullOrWhiteSpace(formatting?.LineSpacing) ? "1.6" : formatting!.LineSpacing,
                    PublishingPlatform = formatting?.PublishingPlatform,
                    PublishingPlatforms = formatting?.PublishingPlatforms ?? "",
                    UserBooks = userBooks
                };

                // Load Book Cover Design Template options (from bookcoverpages table)
                try
                {
                    viewModel.BookCoverPages = await _context.BookCoverPages.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
                }
                catch (Exception ex)
                {
                    // Log so we can see why dropdown is empty (e.g. table/column name mismatch)
                    System.Diagnostics.Debug.WriteLine("BookCoverPages load failed: " + ex.Message);
                    viewModel.BookCoverPages = new List<BookCoverPages>();
                }

                // For sidebar state and JS
                ViewBag.UserId = userId;
                ViewBag.SelectedBookId = bookId;
                ViewBag.SelectedBookName = viewModel.Title;
                ViewBag.SelectedFormat = preferredFormat;
                ViewBag.BookCoverPages = viewModel.BookCoverPages;
                ApplyPrintReadyKdpViewBag();

                var formatPath = ResolveFormatPath(preferredFormat, viewModel.PublishingPlatform, viewModel.PublishingPlatforms);
                try
                {
                    await _bookFlow.SaveStepAsync(bookId, BookFlowStateService.StepFormat, formatPath);
                }
                catch (Exception flowEx)
                {
                    _logger.LogWarning(flowEx, "SaveStepAsync failed for book {BookId}; formatter page still loads.", bookId);
                }
                ViewBag.FlowBookId = bookId;
                ViewBag.FlowStep = BookFlowStateService.StepFormat;
                ViewBag.FlowPath = formatPath;
                ViewBag.FlowBackUrl = bookId > 0 ? $"/Books/Writer?bookId={bookId}" : "/Dashboard";

                return View("CoverDesignCalculatorFixing", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CoverDesignCalculatorFixing failed for bookId={BookId}", bookId);
                var uid = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
                if (uid == 0)
                    return RedirectToAction("UserLogin", "Account");

                var recoveredBookId = bookId;
                if (recoveredBookId <= 0)
                    recoveredBookId = HttpContext.Session.GetInt32("LastSelectedBookId") ?? 0;
                if (recoveredBookId <= 0)
                {
                    var q = Request.Query["bookId"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(q) && int.TryParse(q, out var qBookId))
                        recoveredBookId = qBookId;
                }

                ViewBag.Error = "Some formatter settings could not load. Your book preview will still load.";
                ViewBag.UserId = uid;
                ViewBag.SelectedBookId = recoveredBookId;
                ViewBag.FlowBookId = recoveredBookId;
                ViewBag.FlowStep = BookFlowStateService.StepFormat;
                ViewBag.FlowBackUrl = recoveredBookId > 0
                    ? $"/Books/AIGenerateBook?bookId={recoveredBookId}"
                    : "/Dashboard";

                var recoveredTitle = recoveredBookId > 0 ? $"Book #{recoveredBookId}" : "No Book Selected";
                if (recoveredBookId > 0)
                {
                    try
                    {
                        var bookRow = await _context.Books.AsNoTracking()
                            .FirstOrDefaultAsync(b => b.BookId == recoveredBookId && b.UserId == uid);
                        if (bookRow != null)
                        {
                            recoveredTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                                _context, uid, recoveredBookId, bookRow.Title);
                        }
                    }
                    catch (Exception titleEx)
                    {
                        _logger.LogWarning(titleEx, "Could not resolve title for book {BookId}", recoveredBookId);
                    }
                }

                if (recoveredBookId > 0)
                {
                    try
                    {
                        var previewDetails = await _bookService.GetBookDetailsForPreviewAsync(uid, recoveredBookId);
                        if (previewDetails is { Success: true })
                        {
                            ViewBag.InitialBookPayloadJson = JsonSerializer.Serialize(new
                            {
                                success = true,
                                bookId = previewDetails.BookId,
                                bookTitle = previewDetails.BookTitle,
                                subtitle = previewDetails.Subtitle ?? "",
                                description = previewDetails.Description ?? "",
                                genre = previewDetails.Genre ?? "",
                                authorName = previewDetails.AuthorName ?? "",
                                coverImagePath = previewDetails.CoverImagePath ?? "",
                                totalChapters = previewDetails.TotalChapters,
                                chapters = previewDetails.Chapters
                                    .OrderBy(c => c.ChapterNumber)
                                    .Select(c => new
                                    {
                                        chapterNo = c.ChapterNumber,
                                        chapterTitle = c.Title,
                                        content = c.Content ?? ""
                                    })
                                    .ToList()
                            });
                        }
                    }
                    catch (Exception previewEx)
                    {
                        _logger.LogWarning(previewEx, "Could not preload book payload for book {BookId}", recoveredBookId);
                    }
                }

                var fallbackModel = new CoverDesignCalculatorVM
                {
                    UserId = uid,
                    BookId = recoveredBookId,
                    Title = recoveredTitle,
                    BookCoverPages = new List<BookCoverPages>()
                };
                return View("CoverDesignCalculatorFixing", fallbackModel);
            }
        }

        //==============================================
        // GET: /BookDesign/InteriorPreview
        // Self-contained interactive "Book Interior Preview" (real templates).
        // Standalone page so it never interferes with the production formatter.
        //==============================================
        [HttpGet]
        [Route("BookDesign/InteriorPreview")]
        public async Task<IActionResult> InteriorPreview(int bookId = 0)
        {
            int userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
            if (userId == 0)
                return RedirectToAction("UserLogin", "Account");

            // Proof-of-concept title binding: use the real book title when a bookId
            // is supplied, otherwise fall back to the sample manuscript title.
            string bookTitle = "Verglas Season";
            if (bookId > 0)
            {
                try
                {
                    var bookRow = await _context.Books.AsNoTracking()
                        .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
                    if (bookRow != null)
                    {
                        var resolved = await BookTitleResolver.ResolveDisplayTitleAsync(
                            _context, userId, bookRow.BookId, bookRow.Title);
                        if (!string.IsNullOrWhiteSpace(resolved))
                            bookTitle = resolved;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "InteriorPreview title resolve failed for book {BookId}", bookId);
                }
            }

            ViewBag.BookTitle = bookTitle;
            ViewBag.SelectedBookId = bookId;
            return View("InteriorPreview");
        }

        /// <summary>Bind print-ready cover ViewBag multipliers from KdpSpecs (single source of truth).</summary>
        private void ApplyPrintReadyKdpViewBag()
        {
            var specs = KdpSpecsAccessor.Current;
            var defaultTrim = specs.TrimPresets.FirstOrDefault(t => t.IsDefault)
                ?? specs.TrimPresets.FirstOrDefault()
                ?? new TrimSizeOption { WidthIn = 6, HeightIn = 9 };
            ViewBag.PrintReadyWhitePaperMultiplier = specs.PaperThicknessInPerPage.White;
            ViewBag.PrintReadyCreamPaperMultiplier = specs.PaperThicknessInPerPage.Cream;
            ViewBag.PrintReadyColorPaperMultiplier = specs.PaperThicknessInPerPage.PremiumColor;
            ViewBag.PrintReadyBleedInches = specs.BleedIn;
            ViewBag.PrintReadyDefaultTrimWidthInches = defaultTrim.WidthIn;
            ViewBag.PrintReadyDefaultTrimHeightInches = defaultTrim.HeightIn;
        }

        private double ParseDoubleSetting(string key, double fallback)
        {
            var raw = (_configuration[key] ?? "").Trim();
            if (double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
                return value;
            return fallback;
        }

        private static string ResolveFormatPath(string? format, string? publishingPlatform, string? publishingPlatforms)
        {
            var fmt = (format ?? "").Trim();
            if (fmt.Contains("paper", StringComparison.OrdinalIgnoreCase)
                || fmt.Contains("print", StringComparison.OrdinalIgnoreCase)
                || fmt.Equals("Both", StringComparison.OrdinalIgnoreCase))
                return "print";
            var platforms = $"{publishingPlatform},{publishingPlatforms}";
            if (platforms.Contains("Just Print Ready File", StringComparison.OrdinalIgnoreCase)
                || platforms.Contains("Publishable Book", StringComparison.OrdinalIgnoreCase))
                return "print";
            return "ebook";
        }

        /// <summary>
        /// Returns list of image URLs for the given template (folder from BookCoverPagePath).
        /// Images are read from the folder path stored in bookcoverpages.BookCoverPagePath.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetBookCoverImages(int templateId)
        {
            var template = await _context.BookCoverPages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == templateId);
            if (template == null || string.IsNullOrWhiteSpace(template?.BookCoverPagePath))
                return Json(new { title = "", imageUrls = new string[0] });

            // Path from table: e.g. "Images\book_covers\Fantasy" or "Images/book_covers/Fantasy"
            string folderPath = template.BookCoverPagePath.Trim();
            if (!Path.IsPathRooted(folderPath))
            {
                folderPath = folderPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string combined = Path.Combine(_env.ContentRootPath, folderPath);
                // Fallback: try under wwwroot if not under ContentRoot (e.g. "Images/book_covers/Fantasy")
                if (!Directory.Exists(combined) && _env.WebRootPath != null)
                {
                    string webCombined = Path.Combine(_env.WebRootPath, folderPath);
                    if (Directory.Exists(webCombined))
                        combined = webCombined;
                }
                folderPath = combined;
            }

            if (!Directory.Exists(folderPath))
                return Json(new { title = template.Title ?? "", imageUrls = Array.Empty<string>() });

            var extensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            var files = Directory.GetFiles(folderPath)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(f => Url.Action("ServeBookCoverImage", "BookDesign", new { templateId, fileName = Path.GetFileName(f) }))
                .ToList();

            return Json(new { title = template.Title ?? "", imageUrls = files });
        }

        /// <summary>
        /// Serves a single image file from the template's BookCoverPagePath folder.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ServeBookCoverImage(int templateId, string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOf("..", StringComparison.Ordinal) >= 0)
                return NotFound();

            var template = await _context.BookCoverPages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == templateId);
            if (template == null || string.IsNullOrWhiteSpace(template?.BookCoverPagePath))
                return NotFound();

            string folderPath = template.BookCoverPagePath.Trim();
            if (!Path.IsPathRooted(folderPath))
            {
                folderPath = folderPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                string combined = Path.Combine(_env.ContentRootPath, folderPath);
                if (!Directory.Exists(combined) && _env.WebRootPath != null)
                {
                    string webCombined = Path.Combine(_env.WebRootPath, folderPath);
                    if (Directory.Exists(webCombined))
                        combined = webCombined;
                }
                folderPath = combined;
            }

            string fullPath = Path.Combine(folderPath, Path.GetFileName(fileName));
            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            var fullPathNormalized = Path.GetFullPath(fullPath);
            var rootNormalized = Path.GetFullPath(_env.ContentRootPath);
            var webRootNormalized = _env.WebRootPath != null ? Path.GetFullPath(_env.WebRootPath) : null;
            bool underRoot = fullPathNormalized.StartsWith(rootNormalized, StringComparison.OrdinalIgnoreCase);
            bool underWeb = webRootNormalized != null && fullPathNormalized.StartsWith(webRootNormalized, StringComparison.OrdinalIgnoreCase);
            if (!underRoot && !underWeb)
                return NotFound();

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };

            return PhysicalFile(fullPath, contentType);
        }

        //==============================================
        //       On Page Load
        // Cover Design Calculator1 for a user and book
        //==============================================
        [HttpGet]
        public async Task<IActionResult> CoverDesignCalculator1(int bookId = 12) // Default value 12
        {
            try
            {
                int userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId"));
                if (userId == 0)
                {
                    return RedirectToAction("UserLogin", "Account");
                }
                ViewBag.UserId = userId; // ✅ send to Razor view
                ViewBag.SelectedBookId = bookId;
                // Get data from service (may be null if no record saved yet)
                var response = await _bookDesignService.GetCoverDesignCalculator(userId, bookId);

                // Create and populate ViewModel - use defaults when response is null
                var viewModel = new CoverDesignCalculatorVM
                {
                    BookId = bookId,
                    UserId = userId,
                    Title = response?.BookDetails ?? "No Book Details",
                    CoverDesignData = response,

                    // Form properties
                    BindingType = response?.BindingType ?? "paperback",
                    InteriorType = response?.InteriorType ?? "bw",
                    PaperType = response?.PaperType ?? "white",
                    MeasurementUnits = response?.MeasurementUnits ?? "inches",
                    TrimSize = response?.TrimSize ?? "6x9",
                    PagesCount = response?.PagesCount ?? 0,
                    Width = response?.Width ?? 0m,
                    Height = response?.Height ?? 0m,
                    SafeArea = response?.SafeArea ?? 0m,
                    SpineWidth = response?.SpineWidth ?? 0m,
                    SpineTitle = response?.SpineTitle,
                    SpineDescription = response?.SpineDescription
                };

                // Still set ViewBag for JavaScript compatibility
                ViewBag.UserId = userId;
                ViewBag.SelectedBookId = bookId;

                return View("CoverDesignCalculator1", viewModel);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                return View("CoverDesignCalculator1", new CoverDesignCalculatorVM());
            }
        }
        //==============================================
        //       On Page Load
        // Cover Design Calculator for a user and book
        //==============================================
        [HttpGet]
        public IActionResult CoverDesignCalculator(int bookId = 0)
        {
            int userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
            if (userId == 0)
                return RedirectToAction("UserLogin", "Account");
            return RedirectToAction(nameof(CoverDesignCalculatorFixing), new { bookId });
        }
        public class CalculationRequest
        {
            public string BindingType { get; set; } = string.Empty;
            public string PaperType { get; set; } = string.Empty;
            public int PagesCount { get; set; }
            public string TrimSize { get; set; } = string.Empty;
            public string MeasurementUnits { get; set; } = string.Empty;
        }

        /// <summary>Interior design template picker (swiper) before Book Formatting.</summary>
        [HttpGet]
        [Route("BookDesign/SelectTemplate")]
        public async Task<IActionResult> SelectTemplate(int bookId, int totalPages = 0)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId <= 0)
                return RedirectToAction("UserLogin", "Account");

            if (bookId > 0)
            {
                var owns = await _context.Books.AsNoTracking()
                    .AnyAsync(b => b.BookId == bookId && b.UserId == userId);
                if (!owns)
                {
                    TempData["InfoMessage"] = "That book was not found.";
                    return RedirectToAction("Index", "Dashboard");
                }
            }

            if (totalPages <= 0 && bookId > 0)
            {
                var chapters = await _context.APIRawResponse.AsNoTracking()
                    .Where(r => r.BookId == bookId)
                    .Select(r => r.Chapter)
                    .Distinct()
                    .CountAsync();
                totalPages = Math.Max(chapters * 10, 24);
            }
            if (totalPages <= 0)
                totalPages = 100;

            var designs = await _context.BookDesign
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.DesignId)
                .ToListAsync();

            return View("Index", new BookDesignVM
            {
                BookId = bookId,
                TotalPages = totalPages,
                Designs = designs
            });
        }

        /// <summary>Saves user's chosen interior design template for a book.</summary>
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("BookDesign/SaveSelection")]
        public async Task<IActionResult> SaveSelection(int bookId, int designId)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            if (userId <= 0)
                return Json(new { success = false, message = "Please sign in again." });
            if (bookId <= 0 || designId <= 0)
                return Json(new { success = false, message = "Book and design are required." });

            var ownsBook = await _context.Books.AsNoTracking()
                .AnyAsync(b => b.BookId == bookId && b.UserId == userId);
            if (!ownsBook)
                return Json(new { success = false, message = "Book not found." });

            var designExists = await _context.BookDesign.AsNoTracking()
                .AnyAsync(d => d.DesignId == designId && d.IsActive);
            if (!designExists)
                return Json(new { success = false, message = "Design not found." });

            var row = await _context.BookSelectionsUser
                .FirstOrDefaultAsync(s => s.BookId == bookId && s.UserId == userId);
            if (row == null)
            {
                _context.BookSelectionsUser.Add(new BookSelectionsUser
                {
                    BookId = bookId,
                    UserId = userId,
                    DesignId = designId,
                    CreatedDate = DateTime.UtcNow
                });
            }
            else
            {
                row.DesignId = designId;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        private static int? ResolvePreviewPageCount(SaveBookFormattingRequest req, string statePayload)
        {
            var maxPages = Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount;

            if (req.PreviewPageCount is >= 1 && req.PreviewPageCount.Value <= maxPages)
                return req.PreviewPageCount;

            if (!string.IsNullOrWhiteSpace(statePayload))
            {
                try
                {
                    using var doc = JsonDocument.Parse(statePayload);
                    if (doc.RootElement.TryGetProperty("previewPageCount", out var pp)
                        && pp.TryGetInt32(out var fromDraft)
                        && fromDraft >= 1
                        && fromDraft <= maxPages)
                        return fromDraft;
                }
                catch (JsonException) { /* ignore malformed draft */ }
            }

            return null;
        }
    }
}
