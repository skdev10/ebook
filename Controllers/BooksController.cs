using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.ViewModels;
using EBookDashboard.Services;
using static EBookDashboard.Services.CoverExternalApiHelper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Recommendations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EBookDashboard.Controllers
{
    [Authorize]
    //[Route("[controller]/[action]")]
    public class BooksController : Controller
    {
        private readonly HttpClient _httpClient;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IBookService _bookService;
        private readonly IAPIRawResponseService _rawResponseService;
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<BooksController> _logger;
        private readonly IChapterIterationService _chapterIterationService;
        private readonly IBookPdfService _bookPdfService;

        public BooksController(
            IBookService bookService,
            ApplicationDbContext context,
            IHttpClientFactory httpClientFactory,
            IAPIRawResponseService rawResponseService,
            IConfiguration configuration,
            ILogger<BooksController> logger,
            IChapterIterationService chapterIterationService,
            IBookPdfService bookPdfService)
        {
            _httpClientFactory = httpClientFactory;
            _httpClient = httpClientFactory.CreateClient();
            _bookService = bookService;
            _rawResponseService = rawResponseService;
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _chapterIterationService = chapterIterationService;
            _bookPdfService = bookPdfService;
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
            if (bookId.HasValue && bookId.Value > 0)
            {
                await SetActiveBookAsync(userId.Value, bookId.Value);
            }
            ViewBag.UserId = userId;
            ViewBag.SelectedBookId = bookId;
            var userBooks = await _context.Books
                .Where(b => b.UserId == userId.Value)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new BookDropdownItem { BookId = b.BookId, Title = b.Title })
                .ToListAsync();
            var model = new AIGenerateBookViewModel
            {
                BookRequest = new AIBookRequest(),
                AvailablePlans = _context.Plans.ToList(),
                UserBooks = userBooks
            };
            // Browser must abort fetch before proxies/IIS default timeouts leave the UI stuck on "Generating…"
            var fetchMins = int.TryParse(_configuration["ChapterGeneration:BrowserFetchTimeoutMinutes"], out var fm) ? fm : 12;
            fetchMins = Math.Clamp(fetchMins, 5, 45);
            ViewBag.ChapterGenerateFetchTimeoutMs = fetchMins * 60 * 1000;
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
        public async Task<IActionResult> GetFullBookContent(int userId, int bookId)
        {
            try
            {
                if (userId == 0 || bookId == 0)
                    return Json(new { success = false, message = "User ID and Book ID are required" });

                var result = await _bookService.GetBookDetailsForPreviewAsync(userId, bookId);
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
        [Route("Books/AIGenerateBook")]
        public async Task<IActionResult> AIGenerateBook()
        {
            string body;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
                body = await reader.ReadToEndAsync();

            var model = ParseAIBookRequestFromBody(body);
            if (model == null)
                return Json(new { error = true, message = "Invalid request data — empty body or invalid JSON." });

            var apiUrl = _configuration["ExternalApi:GenerateUrl"] ?? "http://162.229.248.26:8001/api/generate_chapter";
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { error = true, message = "Server configuration error: ExternalApi:ApiKey is not set. Add it in appsettings, environment variables, or user secrets." });
            var responseData = string.Empty;
            int? rawResponseId = null;
            try
            {
                // Named client: timeout from ChapterGeneration:HttpTimeoutMinutes (see Program.cs). Do not use new HttpClient() here.
                var client = _httpClientFactory.CreateClient("ExternalChapterGeneration");
                var apiPayload = GenerateChapterPayloadBuilder.CloneForExternalGenerateApi(model);
                var json = JsonConvert.SerializeObject(apiPayload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = content };
                httpRequest.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

                Console.WriteLine($"📤Sending request to API: {apiUrl} (payload length {json.Length})");
                // RequestAborted: if the browser aborts (timeout / navigation), stop waiting on the external API.
                using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseContentRead, HttpContext.RequestAborted);
                responseData = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);

                    // Log the API response in VS Output or console
                    Console.WriteLine($"📥API Response Status: {response.StatusCode}");
                    Console.WriteLine($"📥API Response Data: {responseData}");

                    // ✅ SAVE RAW RESPONSE FIRST
                    rawResponseId = await _rawResponseService.SaveRawResponseAsync(
                        model,
                        responseData,
                        apiUrl,
                        response.StatusCode.ToString()
                    );

                    Console.WriteLine($"📥 API Response Status: {response.StatusCode}");
                    Console.WriteLine($"📥 Raw Response saved with ID: {rawResponseId}");


                    if (!response.IsSuccessStatusCode)
                        return Json(new { error = true, message = $"API error: {response.StatusCode}", detail = responseData?.Length > 200 ? responseData.Substring(0, 200) + "..." : responseData });
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
                            await _chapterIterationService.RecordSuccessfulGenerationAsync(rawResponseId.Value, HttpContext.RequestAborted);
                        }
                        catch (Exception itEx)
                        {
                            _logger.LogWarning(itEx, "Chapter iteration not recorded — ensure chapter_iterations table exists (see DatabaseScripts/create_chapter_iterations_table.sql).");
                        }
                    }

                    if (rawResponseId.HasValue)
                        Response.Headers.Append("X-Saved-Response-Id", rawResponseId.Value.ToString(CultureInfo.InvariantCulture));
                    return Content(responseData, "application/json");
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                if (rawResponseId == null)
                    try { await _rawResponseService.SaveRawResponseAsync(model, responseData ?? "", apiUrl, "504", "Timeout waiting for generation API"); } catch { }
                Console.WriteLine($"❌ Generate chapter timeout: {ex.Message}");
                return Json(new { error = true, message = "Generation is taking longer than expected. If your chapter is very long, try again or shorten the prompt. You can also retry in a few minutes." });
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

        //=============================================
        [HttpPost]
        public async Task<ActionResult> EditChapter(string userId, string bookId, string chapter, string changes)
        {
            var apiUrl = _configuration["ExternalApi:EditUrl"] ?? "http://162.229.248.26:8001/api/edit";
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { error = true, message = "Server configuration error: ExternalApi:ApiKey is not set." });

            var payload = new { user_id = userId, book_id = bookId, chapter, changes };
            var json = JsonConvert.SerializeObject(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
            using var response = await _httpClient.SendAsync(req);
            var result = await response.Content.ReadAsStringAsync();
            return Content(result, "application/json");
        }

        //==================================
        //     Edit Book
        //==================================
        // ✅ 2️⃣ — POST: Call external API and return book data as JSON
        // Generate Book via API
        [HttpPost]
        [Route("Books/AIEditBook")]
        public async Task<IActionResult> AIEditBook([FromBody] AIBookRequestEdit model)
        {
            if (model == null)
                return Json(new { error = true, message = "Invalid request data" });

            var apiUrl = _configuration["ExternalApi:EditUrl"] ?? "http://162.229.248.26:8001/api/edit";
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { error = true, message = "Server configuration error: ExternalApi:ApiKey is not set." });

            var userId = model.UserId ?? "";
            var bookId = model.BookId ?? "";
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
                    var editGenClient = _httpClientFactory.CreateClient("ExternalChapterGeneration");
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var requestMsg = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = content };
                    requestMsg.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

                    using var response = await editGenClient.SendAsync(requestMsg, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
                    responseData = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);

                    try
                    {
                        var saveTask = _rawResponseService.SaveRawResponseEditAsync(model, responseData, apiUrl, response.StatusCode.ToString());
                        if (await Task.WhenAny(saveTask, Task.Delay(TimeSpan.FromSeconds(8))) == saveTask)
                            await saveTask;
                    }
                    catch (Exception saveEx) { _logger.LogWarning(saveEx, "Raw response save failed"); }

                    if (response.IsSuccessStatusCode)
                        return Content(responseData, "application/json");

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
                // Convert UserId from string to int safely
                if (!int.TryParse(request.UserId, out int userId))
                {
                    userId = 1; // Default fallback
                }
                // === ADD THESE DEBUG LINES ===
                Console.WriteLine($"🔍 DEBUG: Raw UserId from request: '{request.UserId}'");
                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                Console.WriteLine($"🔍 DEBUG: Session UserId: {sessionUserId}");
                // =============================

                    // === ADD THIS COMPARISON ===
                    if (sessionUserId.HasValue && userId != sessionUserId.Value)
                    {
                        Console.WriteLine($"⚠️ DEBUG: USER ID MISMATCH! Session: {sessionUserId.Value}, Using: {userId}");
                    }
                    // ===========================

                    Console.WriteLine($"💾 Saving book to database - UserId: {userId} (converted from: {request.UserId})");
                    Console.WriteLine($"💾 Saving book to database - UserId: {userId} (converted from: {request.UserId})");

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
                // Convert UserId from string to int safely
                if (!int.TryParse(request.UserId, out int userId))
                {
                    userId = 1; // Default fallback
                }
                // === ADD THESE DEBUG LINES ===
                Console.WriteLine($"🔍 DEBUG: Raw UserId from request: '{request.UserId}'");
                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                Console.WriteLine($"🔍 DEBUG: Session UserId: {sessionUserId}");
                // =============================

                // === ADD THIS COMPARISON ===
                if (sessionUserId.HasValue && userId != sessionUserId.Value)
                {
                    Console.WriteLine($"⚠️ DEBUG: USER ID MISMATCH! Session: {sessionUserId.Value}, Using: {userId}");
                }
                // ===========================

                Console.WriteLine($"💾 Saving book to database - UserId: {userId} (converted from: {request.UserId})");
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
                    bookTitle = b.BookTitle,
                    createdDate = b.CreatedDate.ToString("MMM dd, yyyy")
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
        /// PDF built only from iterations marked finalized; includes generation/finalization timestamps per chapter.
        /// </summary>
        [HttpPost]
        [Route("Books/DownloadFinalizedChaptersPdf")]
        public async Task<IActionResult> DownloadFinalizedChaptersPdf([FromBody] ExportBookPdfRequest req, CancellationToken cancellationToken)
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

            var details = await _chapterIterationService.BuildPdfReadyFromFinalizedAsync(sessionUserId.Value, req.BookId, cancellationToken);
            if (details == null || !details.Success)
                return Json(new { success = false, message = "No finalized chapters yet. On Book formatting, pick a draft version per chapter and use Finalize, then try again." });

            var orderedChapters = details.Chapters.OrderBy(c => c.ChapterNumber).ToList();
            if (orderedChapters.Count == 0 || !orderedChapters.Any(c => !string.IsNullOrWhiteSpace(c.Content)))
                return Json(new { success = false, message = "Finalized chapters have no exportable body text." });

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
                var fileName = $"{safe}-finalized-{req.BookId}.pdf";
                return File(pdfBytes, "application/pdf", fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DownloadFinalizedChaptersPdf failed for book {BookId}", req.BookId);
                return Json(new { success = false, message = "PDF generation failed." });
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
                var resolvedCover = await ResolveBookCoverAsync(
                    r.CoverImagePath,
                    r.Title,
                    fallbackCovers[(r.BookId % fallbackCovers.Length + fallbackCovers.Length) % fallbackCovers.Length]);

                list.Add(new EBookHubItemViewModel
                {
                    BookId = r.BookId,
                    Title = r.Title,
                    Description = r.Description ?? "",
                    WordCount = r.WordCount,
                    FullName = r.AuthorName,
                    CoverImagePath = resolvedCover,
                    Genre = r.Genre ?? ""
                });
            }
            return View(list);
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
            if (!(book.Status ?? "").Equals("Paid", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction("BookPayment", "Checkout", new { bookId = id });
            }
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
            var book = await _context.Books.FindAsync(id);
            if (book != null)
            {
                _context.Books.Remove(book);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: Books/Publish/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Publish(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book != null)
            {
                if (!(book.Status ?? "").Equals("Paid", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction("BookPayment", "Checkout", new { bookId = id });
                }
                book.Status = "Published";
                book.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
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
        [Route("Books/EditChapter")]
        public async Task<IActionResult> EditChapter([FromBody] APIEditChapterRequest model)
        {
            if (model == null)
                return BadRequest("Invalid request payload.");
            var apiUrl = (_configuration["ExternalApi:EditUrl"] ?? "http://162.229.248.26:8001/api/edit").Trim();
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            const string apiHeaderName = "X-API-Key";

            string responseData = string.Empty;
            int? rawResponseId = null;

            try
            {
                if (string.IsNullOrEmpty(apiKey))
                    return BadRequest(new { success = false, message = "ExternalApi:ApiKey is not set." });

                var editClient = _httpClientFactory.CreateClient("ExternalChapterGeneration");
                var json = JsonConvert.SerializeObject(model);
                using var httpRequestEdit = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                httpRequestEdit.Headers.TryAddWithoutValidation(apiHeaderName, apiKey);

                Console.WriteLine($"📤Forwarding edit request to API: {json}");

                using var response = await editClient.SendAsync(httpRequestEdit, HttpCompletionOption.ResponseHeadersRead, HttpContext.RequestAborted);
                responseData = await response.Content.ReadAsStringAsync(HttpContext.RequestAborted);

                // Save raw response for audit
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

                Console.WriteLine($"📥 Edit API response status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    // return the raw content and code so frontend can show error
                    return StatusCode((int)response.StatusCode, responseData);
                }
                // Optionally parse and persist edited content into Chapters table
                try
                {
                    dynamic parsed = JsonConvert.DeserializeObject(responseData);
                    string newContent = parsed?.data?.content ?? parsed?.content ?? null;

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
                // Optionally parse response as JSON to easily return it. We'll return raw content with application/json content type.
                return Content(responseData, "application/json");
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
                return StatusCode(500, $"Server error: {ex.Message}");
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

            var apiUrl = (_configuration["ExternalApi:EditUrl"] ?? "http://162.229.248.26:8001/api/edit").Trim();
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, message = "ExternalApi:ApiKey is not set." });

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
            req.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
            using var response = await _httpClient.SendAsync(req);
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

                var titleTrim = TruncateTitle(model.ChapterTitle);
                if (string.IsNullOrEmpty(titleTrim))
                    titleTrim = $"Chapter {chapterNum}";

                var chapter = await _context.Chapters.FirstOrDefaultAsync(c => c.BookId == bookId && c.ChapterNumber == chapterNum);
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

                return Json(new
                {
                    success = true,
                    message = "Chapter updated successfully.",
                    responseId = newResponseId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveChapterContent failed");
                return StatusCode(500, new { success = false, message = "Could not save chapter: " + ex.Message });
            }
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
                return Json(new { success = false, message = "Select a PDF or text file." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                var ct = file.ContentType ?? "";
                if (ct.Contains("pdf", StringComparison.OrdinalIgnoreCase)) ext = ".pdf";
                else if (ct.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) ext = ".txt";
            }

            if (ext != ".pdf" && ext != ".txt" && ext != ".md" && ext != ".markdown")
                return Json(new { success = false, message = "Supported formats: PDF, Plain text (.txt), Markdown (.md)." });

            byte[] bytes;
            await using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, cancellationToken);
                bytes = ms.ToArray();
            }

            if (bytes.Length == 0)
                return Json(new { success = false, message = "This file appears empty." });

            try
            {
                string text;
                if (ext == ".pdf")
                    text = ExtractPdfTextAsPlain(bytes, cancellationToken);
                else
                    text = DecodeTextFile(bytes);

                text = (text ?? string.Empty).Trim();
                if (text.Length > 2_000_000)
                    text = text.Substring(0, 2_000_000) + "\n...[truncated]";

                if (string.IsNullOrWhiteSpace(text))
                    return Json(new { success = false, message = "No readable text found (scanned PDFs need OCR). Paste the text instead." });

                var suggestedBookTitle = SuggestBookTitleFromFileName(file.FileName);
                var (suggestedChapterNo, suggestedChapterTitle) = SuggestChapterFromBodyText(text);

                return Json(new
                {
                    success = true,
                    fileName = file.FileName,
                    text,
                    characterCount = text.Length,
                    suggestedBookTitle,
                    suggestedChapterNo,
                    suggestedChapterTitle
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ImportChapterFile failed for {Name}", file.FileName);
                return Json(new { success = false, message = "Could not read this file. Please try uploading a different export or paste the text instead." });
            }
        }

        private static string ExtractPdfTextAsPlain(byte[] bytes, CancellationToken cancellationToken)
        {
            using var document = PdfDocument.Open(new MemoryStream(bytes, writable: false), new ParsingOptions { UseLenientParsing = true });
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var words = page.GetWords();
                    if (words != null && words.Any())
                    {
                        var line = string.Join(" ", words.Select(w => w.Text));
                        if (!string.IsNullOrWhiteSpace(line))
                            sb.AppendLine(line);
                    }
                    else if (page.Letters != null && page.Letters.Count > 0)
                    {
                        foreach (var letter in page.Letters)
                            sb.Append(letter.Value);
                        sb.AppendLine();
                    }
                }
                catch (Exception)
                {
                    /* skip unreadable page */
                }
            }

            return sb.ToString();
        }

        private static string DecodeTextFile(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }

        private static string SuggestBookTitleFromFileName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(fileName.Trim());
            if (string.IsNullOrWhiteSpace(baseName)) return string.Empty;
            baseName = Regex.Replace(baseName.Replace('_', ' '), @"\s+", " ").Trim();
            return baseName.Length > 200 ? baseName.Substring(0, 197) + "..." : baseName;
        }

        /// <summary>First heading, "Chapter N: Title", or short first line → chapter no + title for import UI.</summary>
        private static (int chapterNo, string chapterTitle) SuggestChapterFromBodyText(string text)
        {
            var chapterNo = 1;
            var chapterTitle = string.Empty;
            if (string.IsNullOrWhiteSpace(text)) return (chapterNo, "Imported chapter");

            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                var hm = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
                if (hm.Success)
                {
                    chapterTitle = TruncateSuggestedTitle(hm.Groups[2].Value.Trim());
                    break;
                }

                var chMatch = Regex.Match(line, @"^(?:Chapter|CHAPTER)\s+(\d+)\s*[:\.\-]?\s*(.*)$", RegexOptions.IgnoreCase);
                if (chMatch.Success)
                {
                    if (int.TryParse(chMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
                        chapterNo = n;
                    var rest = chMatch.Groups[2].Value.Trim();
                    if (!string.IsNullOrEmpty(rest))
                        chapterTitle = TruncateSuggestedTitle(rest);
                    break;
                }

                if (line.Length <= 120)
                {
                    chapterTitle = TruncateSuggestedTitle(line);
                    break;
                }

                break;
            }

            if (string.IsNullOrEmpty(chapterTitle))
                chapterTitle = "Imported chapter";
            return (chapterNo, chapterTitle);
        }

        private static string TruncateSuggestedTitle(string s, int max = 150)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max - 3) + "...";
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
           

            var apiUrl = (_configuration["ExternalApi:ApproveUrl"] ?? "http://162.229.248.26:8001/api/approve").Trim();
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            const string apiHeaderName = "X-API-Key";

            string responseData = string.Empty;
            int? rawResponseId = null;

            try
            {
                if (string.IsNullOrEmpty(apiKey))
                    return BadRequest(new { success = false, message = "Server configuration error: ExternalApi:ApiKey is not set." });

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.TryAddWithoutValidation(apiHeaderName, apiKey);

                var json = JsonConvert.SerializeObject(model);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"📤 Sending finalize request to API: {json}");

                var response = await client.PostAsync(apiUrl, content);
                responseData = await response.Content.ReadAsStringAsync();
                
                // 🧾 Save raw API response
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
                        if (book != null)
                        {
                            book.Status = "Final";
                            book.UpdatedAt = DateTime.UtcNow;
                            _context.Books.Update(book);
                        }
                        int chapterNum = int.TryParse(model.Chapter, out var c) ? c : 0;
                        // ✅ Save or update Book and Chapter
                        var chapter = await _context.Chapters.FirstOrDefaultAsync(c => c.BookId == bookId && c.ChapterNumber == chapterNum);

                        if (chapter != null)
                        {
                            chapter.Status = "Final";
                            chapter.UpdatedAt = DateTime.UtcNow;
                            _context.Chapters.Update(chapter);
                        }
                        else
                        {
                            // create stub chapter if not exists (optional)
                            _context.Chapters.Add(new Chapters
                            {
                                BookId = bookId,
                                ChapterNumber = chapterNum,
                                Title = $"Chapter {chapterNum}",
                                Content = "", // unchanged
                                Status = "Final",
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            });
                        }

                        await _context.SaveChangesAsync();
                    }
                    if (int.TryParse(model.UserId, out int uid) && uid > 0)
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
                .Where(b => b.UserId == sessionUserId.Value)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.BookId, b.Title, b.CoverImagePath, b.Description, b.Genre, b.Subtitle })
                .ToListAsync();

            var bookIds = books.Select(b => b.BookId).ToList();
            var promptKeys = bookIds.SelectMany(id => new[] { $"book:{id}:aiCoverPrompt", $"book:{id}:aiCoverLastPreview" }).ToList();
            var promptRows = await _context.Settings
                .Where(s => promptKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");

            var coverUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value);
            // Display name for cover only — do not use email as default author text (wrong UX on preview).
            var authorDisplayName = (coverUser?.FullName ?? "").Trim();

            var result = books.Select(b =>
            {
                var pKey = $"book:{b.BookId}:aiCoverPrompt";
                var lastKey = $"book:{b.BookId}:aiCoverLastPreview";
                return new
                {
                    bookId = b.BookId,
                    title = b.Title ?? "",
                    subtitle = b.Subtitle ?? "",
                    coverImagePath = b.CoverImagePath ?? "",
                    description = b.Description ?? "",
                    genre = b.Genre ?? "",
                    aiCoverPrompt = promptRows.GetValueOrDefault(pKey, ""),
                    aiCoverLastPreview = promptRows.GetValueOrDefault(lastKey, ""),
                    authorName = authorDisplayName
                };
            }).ToList();

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
            var promptRows = await _context.Settings
                .Where(s => s.Key == pKey || s.Key == lastKey)
                .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");

            var coverUser = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == sessionUserId.Value);
            var authorDisplayName = (coverUser?.FullName ?? "").Trim();

            var book = new
            {
                bookId = b.BookId,
                title = b.Title ?? "",
                subtitle = b.Subtitle ?? "",
                coverImagePath = b.CoverImagePath ?? "",
                description = b.Description ?? "",
                genre = b.Genre ?? "",
                aiCoverPrompt = promptRows.GetValueOrDefault(pKey, ""),
                aiCoverLastPreview = promptRows.GetValueOrDefault(lastKey, ""),
                authorName = authorDisplayName
            };

            return Json(new { success = true, book });
        }

        /// <summary>Generate AI cover preview via external POST /api/generate-cover. Returns { success, options[] }. See docs/EXTERNAL_BOOK_API.md</summary>
        [HttpPost]
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
            var size = (_configuration["ExternalApi:CoverGenerateSize"] ?? "1024x1536").Trim();
            var quality = (_configuration["ExternalApi:CoverGenerateQuality"] ?? "medium").Trim();
            var apiUrl = (_configuration["ExternalApi:GenerateCoverUrl"] ?? "http://162.229.248.26:8001/api/generate-cover").Trim();
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, message = "ExternalApi:ApiKey is not set." });

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
            _logger.LogInformation("Generate cover request bookId={BookId} url={Url} payload={Payload}", bookId, apiUrl, json);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(apiKey))
                    request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);

                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var response = await _httpClient.SendAsync(request, cts.Token);
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

            var apiUrl = (_configuration["ExternalApi:EditCoverUrl"] ?? "http://162.229.248.26:8001/api/edit-cover").Trim();
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            if (string.IsNullOrEmpty(apiKey))
                return Json(new { success = false, message = "ExternalApi:ApiKey is not set." });

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
                request.Headers.TryAddWithoutValidation("X-API-Key", apiKey);
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var response = await _httpClient.SendAsync(request, cts.Token);
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
            var st = book.Status ?? "";
            var paid = st.Equals("Paid", StringComparison.OrdinalIgnoreCase) || st.Equals("Published", StringComparison.OrdinalIgnoreCase);
            return Json(new { paid });
        }

        /// <summary>Download hub for a book. Requires payment; redirects to BookPayment if not paid.</summary>
        [HttpGet]
        public async Task<IActionResult> BookDownloads(int bookId)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return RedirectToAction("UserLogin", "Account");
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");
            var bs = book.Status ?? "";
            if (!bs.Equals("Paid", StringComparison.OrdinalIgnoreCase) && !bs.Equals("Published", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("BookPayment", "Checkout", new { bookId });
            ViewBag.BookId = bookId;
            ViewBag.BookTitle = book.Title ?? "Your Book";
            return View();
        }

        /// <summary>GET queue status from external API (running, waiting, max concurrent, total).</summary>
        [HttpGet]
        public async Task<IActionResult> GetQueueData()
        {
            var apiUrl = _configuration["ExternalApi:QueueDataUrl"] ?? "http://162.229.248.26:8001/api/queue-data";
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                if (!string.IsNullOrEmpty(apiKey))
                    client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);
                var response = await client.GetAsync(apiUrl);
                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return Content(json, "application/json");
                return StatusCode((int)response.StatusCode, json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Queue data API failed.");
                return Ok(new { status_running = 0, status_waiting = 0, status_max_concurrent = 0, status_total_requests = 0 });
            }
        }

        /// <summary>POST to external API to get 5 suggested chapter names. Returns suggest_chapter_name for UI.</summary>
        [HttpPost]
        public async Task<IActionResult> BookChaptersName([FromBody] JObject body)
        {
            var apiUrl = _configuration["ExternalApi:BookChaptersNameUrl"] ?? "http://162.229.248.26:8001/api/book_chapters_name";
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(1);
                if (!string.IsNullOrEmpty(apiKey))
                    client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);
                var content = new StringContent(body?.ToString() ?? "{}", Encoding.UTF8, "application/json");
                var response = await client.PostAsync(apiUrl, content);
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

        // ========================== GENERATION (EPUB/PDF - mock) ===========================
        [HttpPost]
        public async Task<IActionResult> GenerateFormats(int bookId, bool epub, bool pdf)
        {
            var sessionUserId = HttpContext.Session.GetInt32("UserId");
            if (sessionUserId == null) return Unauthorized();
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == sessionUserId.Value);
            if (book == null) return NotFound("Book not found.");
            if (!(book.Status ?? "").Equals("Paid", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, message = "Complete payment to unlock downloads." });

            // Simulate generation
            var baseOut = $"/uploads/{sessionUserId}/books/{bookId}/output";
            var outRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", sessionUserId.Value.ToString(), "books", bookId.ToString(), "output");
            Directory.CreateDirectory(outRoot);

            string? epubPath = null;
            string? pdfPath = null;

            if (epub)
            {
                epubPath = $"{baseOut}/book_{DateTime.UtcNow:yyyyMMddHHmmss}.epub";
                var physical = Path.Combine(outRoot, Path.GetFileName(epubPath));
                await System.IO.File.WriteAllTextAsync(physical, "EPUB MOCK CONTENT");
                await UpsertSettingAsync($"book:{bookId}:output:epub", epubPath, "Output");
            }
            if (pdf)
            {
                pdfPath = $"{baseOut}/book_{DateTime.UtcNow:yyyyMMddHHmmss}.pdf";
                var physical = Path.Combine(outRoot, Path.GetFileName(pdfPath));
                await System.IO.File.WriteAllTextAsync(physical, "PDF MOCK CONTENT");
                await UpsertSettingAsync($"book:{bookId}:output:pdf", pdfPath, "Output");
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



    }
}
