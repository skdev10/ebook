using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.ViewModels;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CognitiveServices.Speech.Transcription;
using Microsoft.EntityFrameworkCore;
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

        public BookDesignController(
            ApplicationDbContext context,
            IBookDesignService bookDesignService,
            IBookService bookService,
            IWebHostEnvironment env,
            IConfiguration configuration,
            BookFlowStateService bookFlow)
        {
            _context = context;
            _bookDesignService = bookDesignService ?? throw new ArgumentNullException(nameof(bookDesignService));
            _bookService = bookService ?? throw new ArgumentNullException(nameof(bookService));
            _env = env;
            _configuration = configuration;
            _bookFlow = bookFlow;
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

                if (bookId == 0)
                {
                    bookId = 12; // Default book ID
                }

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

                var statePayload = JsonSerializer.Serialize(new
                {
                    format = existing.Format ?? "Ebook",
                    interiorStyle = existing.InteriorStyle ?? "Novel",
                    textSize = existing.TextSize ?? "Medium",
                    lineSpacing = existing.LineSpacing ?? "1.6",
                    publishingPlatforms = existing.PublishingPlatforms ?? "",
                    publishingPlatform = existing.PublishingPlatform ?? primaryPlatform ?? "",
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
                if (previewPages is >= 1 and <= Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount)
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
                    createdAt = response.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
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
        public async Task<IActionResult> CoverDesignCalculatorFixing(int bookId = 0, string? format = null)
        {
            try
            {
                int userId = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
                if (userId == 0)
                {
                    return RedirectToAction("UserLogin", "Account");
                }
                var hasGeneratedBook = HttpContext.Session.GetString("HasGeneratedBook") == "1"
                    || await _context.Books.AnyAsync(b => b.UserId == userId);
                if (!hasGeneratedBook)
                {
                    ViewBag.LockMessage = "Select a book from the Dashboard to start formatting.";
                    ViewBag.LockGoto = "/Dashboard";
                    ViewBag.LockButtonText = "Go to AI Writer";
                }
                // Get bookId from TempData, query param, or last selected from session
                if (bookId == 0)
                    bookId = Convert.ToInt32(TempData["NextStepBookId"] ?? 0);
                if (bookId == 0)
                {
                    var q = Request.Query["bookId"].FirstOrDefault();
                    if (!string.IsNullOrEmpty(q) && int.TryParse(q, out var qBookId))
                        bookId = qBookId;
                }
                if (bookId == 0)
                    bookId = HttpContext.Session.GetInt32("LastSelectedBookId") ?? 0;
                if (bookId <= 0)
                {
                    TempData["InfoMessage"] = "Select a book from the Dashboard to continue formatting.";
                    return RedirectToAction("Index", "Dashboard");
                }

                var bookRow = await _context.Books.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
                if (bookRow == null)
                {
                    TempData["InfoMessage"] = "That book was not found. Choose a project from the Dashboard.";
                    return RedirectToAction("Index", "Dashboard");
                }

                if (BookFlowStateService.IsPublishedStatus(bookRow.Status))
                {
                    TempData["InfoMessage"] = "This book is already published. Find it under Published Books on the dashboard.";
                    return RedirectToAction("Index", "Dashboard");
                }

                if (!BookFlowStateService.SessionEntryMatches(HttpContext, bookId))
                    HttpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);

                var (savedFlowStep, savedFlowPath) = await _bookFlow.GetStepAsync(bookId);
                if (BookFlowStateService.StepRank(savedFlowStep) < BookFlowStateService.StepRank(BookFlowStateService.StepFormat))
                {
                    TempData["InfoMessage"] = "Finish AI Writer first, then open Book Formatting.";
                    return Redirect(_bookFlow.BuildResumeUrl(bookId, savedFlowStep, savedFlowPath));
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
                ViewBag.PrintReadyWhitePaperMultiplier = ParseDoubleSetting("PrintReadyCover:WhitePaperSpineInchesPerPage", 0.002252);
                ViewBag.PrintReadyCreamPaperMultiplier = ParseDoubleSetting("PrintReadyCover:CreamPaperSpineInchesPerPage", 0.002500);
                ViewBag.PrintReadyColorPaperMultiplier = ParseDoubleSetting("PrintReadyCover:ColorPaperSpineInchesPerPage", 0.002347);
                ViewBag.PrintReadyBleedInches = ParseDoubleSetting("PrintReadyCover:BleedInches", 0.125);
                ViewBag.PrintReadyDefaultTrimWidthInches = ParseDoubleSetting("PrintReadyCover:DefaultTrimWidthInches", 6.0);
                ViewBag.PrintReadyDefaultTrimHeightInches = ParseDoubleSetting("PrintReadyCover:DefaultTrimHeightInches", 9.0);

                var formatPath = ResolveFormatPath(preferredFormat, viewModel.PublishingPlatform, viewModel.PublishingPlatforms);
                await _bookFlow.SaveStepAsync(bookId, BookFlowStateService.StepFormat, formatPath);
                ViewBag.FlowBookId = bookId;
                ViewBag.FlowStep = BookFlowStateService.StepFormat;
                ViewBag.FlowPath = formatPath;
                ViewBag.FlowBackUrl = $"/Books/AIGenerateBook?bookId={bookId}";

                return View("CoverDesignCalculatorFixing", viewModel);
            }
            catch (Exception ex)
            {
                ViewBag.Error = ex.Message;
                var uid = Convert.ToInt32(HttpContext.Session.GetInt32("UserId") ?? 0);
                ViewBag.UserId = uid;
                ViewBag.SelectedBookId = 0;
                var fallbackModel = new CoverDesignCalculatorVM
                {
                    UserId = uid,
                    BookId = 0,
                    BookCoverPages = new List<BookCoverPages>()
                };
                return View("CoverDesignCalculatorFixing", fallbackModel);
            }
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
        //public IActionResult Index(int bookId, int totalPages)
        //{
        //    var designs = _context.BookDesign
        //        .Where(d => d.IsActive)
        //        .ToList();

        //    return View(new BookDesignVM
        //    {
        //        BookId = bookId,
        //        TotalPages = totalPages,
        //        Designs = designs
        //    });
        //}
        //[HttpPost]
        //public IActionResult SaveSelection(int bookId, int designId)
        //{
        //    var userId = 1; // get from logged-in user

        //    _context.BookSelectionsUser.Add(new BookSelectionsUser
        //    {
        //        BookId = bookId,
        //        DesignId = designId,
        //        UserId = userId
        //    });

        //    _context.SaveChanges();

        //    return Json(new { success = true });
        //}

        private static int? ResolvePreviewPageCount(SaveBookFormattingRequest req, string statePayload)
        {
            const int maxPages = Application.Kdp.Constants.KdpPaperbackConstants.MaxPageCount;

            if (req.PreviewPageCount is >= 1 and <= maxPages)
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
