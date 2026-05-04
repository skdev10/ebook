using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace EBookDashboard.Services
{
    public class APIRawResponseService : IAPIRawResponseService
    {
        private readonly ApplicationDbContext _context;
        private readonly CommonMethodsService _commonMethodsService;
        private readonly IChapterIterationService _chapterIterationService;
        private readonly ILogger<APIRawResponseService>? _logger;

        public APIRawResponseService(
            ApplicationDbContext context,
            CommonMethodsService commonMethodsService,
            IChapterIterationService chapterIterationService,
            ILogger<APIRawResponseService>? logger = null)
        {
            _context = context;
            _commonMethodsService = commonMethodsService;
            _chapterIterationService = chapterIterationService;
            _logger = logger;
        }
        //==================================================
        //    Save Raw Response with generated BookId Edit
        //==================================================
        private const int MaxResponseDataLength = 500_000; // avoid huge DB writes that can hang

        public async Task<int> SaveRawResponseEditAsync(AIBookRequestEdit request, string responseData, string endpoint, string statusCode, string? errorMessage = null)
        {
            int userId = int.TryParse(request.UserId, out int uid) ? uid : 0;
            // Generate BookId using the async method
            int generatedBookId = 1; //await _commonMethodsService.GenerateBookIdAsync(userId);
            string userChange = "";
            // Extract user input if responseData is not null
            if (!string.IsNullOrEmpty(request.Changes))
            {
                userChange = ExtractUserInput(request.Changes);
                Console.WriteLine($"Extracted user input: {userChange}");
                Console.WriteLine("---");
            }

            // Truncate very large response to avoid long DB writes / timeouts
            string dataToStore = responseData;
            if (!string.IsNullOrEmpty(responseData) && responseData.Length > MaxResponseDataLength)
            {
                dataToStore = responseData.Substring(0, MaxResponseDataLength) + "\n...[truncated]";
                Console.WriteLine($"⚠️ ResponseData truncated from {responseData.Length} to {MaxResponseDataLength} chars");
            }

            var rawResponse = new APIRawResponse
            {
                Endpoint = endpoint,
                Chapter = request.Chapter,
                Title = request.Title,
                RequestData = JsonConvert.SerializeObject(request.Changes),
                ResponseData = dataToStore,
                UserId = userId,
                BookId = request.BookId != null && int.TryParse(request.BookId, out int bid) ? bid : generatedBookId,
                StatusCode = statusCode,
                ErrorMessage = errorMessage,
                CreatedAt = DateTime.UtcNow
            };

            _context.APIRawResponse.Add(rawResponse);
            await _context.SaveChangesAsync();

            try
            {
                var extracted = ExtractContentFromResponse(dataToStore);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    rawResponse.Content = extracted;
                    _context.APIRawResponse.Update(rawResponse);
                    await _context.SaveChangesAsync();
                }
            }
            catch { /* best-effort */ }

            try
            {
                await _chapterIterationService.RecordSuccessfulGenerationAsync(rawResponse.ResponseId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "RecordSuccessfulGenerationAsync failed after edit save (response {Id})", rawResponse.ResponseId);
            }

            // Update Status to "Edit" in Books table (user is editing)
            int bookId = rawResponse.BookId ?? 0;
            if (bookId > 0 && userId > 0)
            {
                var book = await _context.Books
                    .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
                if (book != null)
                {
                    book.Status = "Edit";
                    book.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
            }

            Console.WriteLine($"💾 Raw response saved with ID: {rawResponse.ResponseId}");
            return rawResponse.ResponseId;
        }
        // On Press ⚙️ Generate method Add New Record in raw response
        //==================================================
        //    Save Raw Response with generated BookId
        //==================================================
        public async Task<int> SaveRawResponseAsync(AIBookRequest request, string responseData, string endpoint, string statusCode, string? errorMessage = null)
        {
            int userId = int.TryParse(request.UserId, out int uid) ? uid : 0;
            // Generate BookId using the async method
            int generatedBookId = 1; //await _commonMethodsService.GenerateBookIdAsync(userId);
            string userInput = "";
            string content1 = "";
            string content2 = "";
            // Extract user input if responseData is not null
            if (!string.IsNullOrEmpty(request.UserInput))
            {
                userInput = ExtractUserInput(request.UserInput);
                Console.WriteLine($"Extracted user input: {userInput}");
                Console.WriteLine("---");
            }
            // Truncate before JObject.Parse / regex — huge or malformed JSON was blocking the HTTP response for minutes.
            var dataToStore = responseData ?? "";
            if (dataToStore.Length > MaxResponseDataLength)
            {
                dataToStore = dataToStore.Substring(0, MaxResponseDataLength) + "\n...[truncated]";
                Console.WriteLine($"⚠️ SaveRawResponseAsync: ResponseData truncated from {responseData?.Length ?? 0} to {MaxResponseDataLength} chars");
            }

            List<string> chapterNames = new();
            Console.WriteLine("🔄 Starting chapter extraction (bounded payload)...");
            try
            {
                chapterNames = ExtractChapterNames(dataToStore);
                if (!string.IsNullOrEmpty(dataToStore))
                {
                    content1 = ExtractContentFromResponse(dataToStore);
                    content2 = ExtractChapterContent(dataToStore);
                    Console.WriteLine($"Extracted chapters count: {chapterNames.Count}; content2 length: {content2?.Length ?? 0}");
                }
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"⚠️ Skipping heavy response parsing (save still proceeds): {parseEx.Message}");
            }

            var rawResponse = new APIRawResponse
            {
                Endpoint = endpoint,
                Chapter = request.Chapter,
                Title = request.Title,
                RequestData = JsonConvert.SerializeObject(request.UserInput),
                ResponseData = dataToStore,
                UserId = userId,
                BookId = request.BookId != null && int.TryParse(request.BookId, out int bid) ? bid : generatedBookId,
                StatusCode = statusCode,
                ErrorMessage = errorMessage,
                CreatedAt = DateTime.UtcNow,
                Content = content1,
                // ✅ FIXED — Save list of chapters properly
                ChapterNames = JsonConvert.SerializeObject(chapterNames)   // or: string.Join(",", chapters)
            };

            _context.APIRawResponse.Add(rawResponse);
            await _context.SaveChangesAsync();

            Console.WriteLine($"💾 Raw response saved with ID: {rawResponse.ResponseId}");
            return rawResponse.ResponseId;
        }
        //======================================
        //    Extracting Chapter Content
        //=====================================
        public string ExtractChapterContent(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                {
                    Console.WriteLine("❌ JSON input is null or empty for content extraction");
                    return string.Empty;
                }

                Console.WriteLine($"📥 Extracting content from JSON");

                // Parse JSON
                var jsonObject = JObject.Parse(json);

                // Navigate to data.content
                var content = jsonObject["data"]?["content"]?.ToString();

                if (string.IsNullOrEmpty(content))
                {
                    Console.WriteLine("❌ 'content' field is null or empty");
                    return string.Empty;
                }

                Console.WriteLine($"✅ Content extracted, length: {content.Length} chars");
                Console.WriteLine($"📝 First 200 chars of content: {content.Substring(0, Math.Min(200, content.Length))}...");

                return content;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Error extracting chapter content: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return string.Empty;
            }
        }
        //======================================
        // Extract content starting from "content" field
        //======================================
        public static string ExtractContentFromResponse(string jsonResponse)
        {
            if (string.IsNullOrEmpty(jsonResponse))
                return string.Empty;

            try
            {
                // Parse the JSON
                var jsonObject = JObject.Parse(jsonResponse);

                // Navigate to data.content
                var content = jsonObject["data"]?["content"]?.ToString();

                if (!string.IsNullOrEmpty(content))
                {
                    Console.WriteLine("✅ Successfully extracted content from data.content");
                    return content;
                }

                // Fallback: try direct "content" property
                content = jsonObject["content"]?.ToString();
                if (!string.IsNullOrEmpty(content))
                {
                    Console.WriteLine("✅ Successfully extracted content from root content property");
                    return content;
                }

                Console.WriteLine("❌ No content field found in JSON response");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Error extracting content from JSON: {ex.Message}");

                // Fallback: try regex extraction
                return ExtractContentWithRegex(jsonResponse);
            }
        }
        //======================================
        // Fallback method using Regex
        //======================================
        public static string ExtractContentWithRegex(string jsonResponse)
        {
            try
            {
                // Pattern to match "content":"...anything..." 
                // This handles multi-line content and escaped characters
                var pattern = @"\""content\"":\""(.*?)\""";
                var match = Regex.Match(jsonResponse, pattern, RegexOptions.Singleline);

                if (match.Success && match.Groups.Count > 1)
                {
                    string extracted = match.Groups[1].Value;
                    Console.WriteLine($"✅ Extracted content using regex, length: {extracted.Length}");
                    return extracted;
                }

                // Alternative pattern for different formatting
                pattern = @"\""content\"":\s*\""([^\""]*)\""";
                match = Regex.Match(jsonResponse, pattern);

                if (match.Success && match.Groups.Count > 1)
                {
                    string extracted = match.Groups[1].Value;
                    Console.WriteLine($"✅ Extracted content using alternative regex, length: {extracted.Length}");
                    return extracted;
                }

                Console.WriteLine("❌ Could not extract content with regex either");
                return string.Empty;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Error in regex extraction: {ex.Message}");
                return string.Empty;
            }
        }
        //======================================
        //    Extracting Chapter Heading
        //=====================================
        public List<string> ExtractChapterNames(string json)
        {
            List<string> chapters = new List<string>();

            try
            {
                if (string.IsNullOrEmpty(json))
                {
                    Console.WriteLine("❌ JSON input is null or empty");
                    return chapters;
                }

                Console.WriteLine($"📥 Raw JSON received for chapter extraction");

                // Parse JSON
                var jsonObject = JObject.Parse(json);

                // Navigate to data.suggest_chapter_name
                var chapterText = jsonObject["data"]?["suggest_chapter_name"]?.ToString();

                if (string.IsNullOrEmpty(chapterText))
                {
                    Console.WriteLine("❌ 'suggest_chapter_name' is null or empty");
                    return chapters;
                }

                Console.WriteLine($"📋 Chapter text: '{chapterText}'");

                // Check if it's an HTML list or plain text
                if (chapterText.Contains("<li>") || chapterText.Contains("<ul>"))
                {
                    // It's an HTML list - extract <li> items
                    MatchCollection matches = Regex.Matches(chapterText, @"<li>(.*?)<\/li>", RegexOptions.Singleline);
                    Console.WriteLine($"🔍 Found {matches.Count} list items in HTML");

                    foreach (Match match in matches)
                    {
                        if (match.Success && match.Groups.Count > 1)
                        {
                            string chapterName = match.Groups[1].Value.Trim();
                            // Clean HTML tags from the chapter name
                            chapterName = Regex.Replace(chapterName, @"<[^>]*>", "").Trim();

                            if (!string.IsNullOrEmpty(chapterName))
                            {
                                chapters.Add(chapterName);
                                Console.WriteLine($"✅ Added chapter from list: {chapterName}");
                            }
                        }
                    }
                }
                else
                {
                    // It's plain text - could be a single chapter or multiple separated by newlines
                    Console.WriteLine($"📝 Processing as plain text");

                    // Split by newlines and process each line
                    var lines = chapterText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    Console.WriteLine($"🔍 Found {lines.Length} lines in text");

                    foreach (var line in lines)
                    {
                        string trimmedLine = line.Trim();

                        // Skip empty lines and numbering (like "1. ", "Chapter 1: ", etc.)
                        if (string.IsNullOrEmpty(trimmedLine)) continue;

                        // Remove common prefixes
                        string cleanName = Regex.Replace(trimmedLine,
                            @"^(?:\d+[\.\)]\s*|Chapter\s+\d+[:\.]\s*|-\s*|\*\s*)", "",
                            RegexOptions.IgnoreCase).Trim();

                        if (!string.IsNullOrEmpty(cleanName))
                        {
                            chapters.Add(cleanName);
                            Console.WriteLine($"✅ Added chapter from text: {cleanName}");
                        }
                    }

                    // If no lines were added (maybe it's one single title), add the whole text
                    if (chapters.Count == 0 && !string.IsNullOrWhiteSpace(chapterText))
                    {
                        chapters.Add(chapterText.Trim());
                        Console.WriteLine($"✅ Added single chapter title: {chapterText.Trim()}");
                    }
                }

                Console.WriteLine($"📚 Total chapters extracted: {chapters.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Error extracting chapter names: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            return chapters;
        }
        //==========================================
        // Method 1: Using Regex (recommended)
        //=========================================
        public static string ExtractUserInput(string jsonResponse)
        {
            if (string.IsNullOrEmpty(jsonResponse))
                return string.Empty;

            // Check if it's a plain string (not JSON)
            if (!jsonResponse.TrimStart().StartsWith("{") && !jsonResponse.TrimStart().StartsWith("["))
            {
                Console.WriteLine($"✅ Input appears to be plain text, not JSON: '{jsonResponse}'");
                return jsonResponse; // Return the plain text directly
            }
            try
            {
                // Try to parse as JSON first
                var jsonObject = JObject.Parse(jsonResponse);
                var userInput = jsonObject["user_input"]?.ToString();

                if (!string.IsNullOrEmpty(userInput))
                {
                    return userInput;
                }
            }
            catch
            {
                // If JSON parsing fails, fall back to regex
                var pattern = @"\""user_input\"":\""([^\""]*)\""";
                var match = Regex.Match(jsonResponse, pattern);

                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value;
                }
            }

            return string.Empty;
        }
        // Method 2: Using String operations (alternative)
        public static string ExtractUserInputUsingStringOps(string jsonResponse)
        {
            if (string.IsNullOrEmpty(jsonResponse))
                return string.Empty;

            string searchPattern = "\"user_input\":\"";
            int startIndex = jsonResponse.IndexOf(searchPattern);

            if (startIndex == -1) return string.Empty;

            startIndex += searchPattern.Length;
            int endIndex = jsonResponse.IndexOf("\"", startIndex);

            if (endIndex == -1) return string.Empty;

            return jsonResponse.Substring(startIndex, endIndex - startIndex);
        }
        // Fist we will generate BookId then save raw response
        public async Task<int> SaveRawResponseAsync2(AIBookRequest request, string responseData, string endpoint, string statusCode, string? errorMessage = null)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Step 1: Create and save a new Book first
                var newBook = new Books
                {
                    AuthorId = int.TryParse(request.UserId, out int aid) ? aid : 0, // Adjust based on your AIBookRequest properties
                    UserId = int.TryParse(request.UserId, out int uid) ? uid : 0,
                    CategoryId = 1, // Set default or get from request
                    Title = request.Title ?? "Untitled Book",
                    Subtitle = string.Empty,
                    AuthorCode = "AUTHOR_CODE", // Set appropriate value
                    LanguageId = 1, // Set default language
                    CoverImagePath = string.Empty,
                    ManuscriptPath = string.Empty,
                    Genre = "Default Genre", // Set appropriate genre
                    Description = string.Empty,
                    WordCount = 0,
                    CreatedAt = DateTime.UtcNow,
                    Status = BookStatus.Draft.ToString()
                };

                _context.Books.Add(newBook);
                await _context.SaveChangesAsync(); // This generates the BookId

                Console.WriteLine($"📚 New Book saved with ID: {newBook.BookId}");

                // Step 2: Save raw response with the generated BookId
                var rawResponse = new APIRawResponse
                {
                    Endpoint = endpoint,
                    Chapter = request.Chapter,
                    Title = request.Title,
                    RequestData = JsonConvert.SerializeObject(request),
                    ResponseData = responseData,
                    UserId = newBook.UserId, // Use from the created book
                    BookId = newBook.BookId, // Use the generated BookId from the new book
                    StatusCode = statusCode,
                    ErrorMessage = errorMessage,
                    CreatedAt = DateTime.UtcNow,
                    ParsedBookId = newBook.BookId.ToString() // Store as string for reference
                };

                _context.APIRawResponse.Add(rawResponse);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                Console.WriteLine($"💾 Raw response saved with ID: {rawResponse.ResponseId} for Book ID: {newBook.BookId}");
                return rawResponse.ResponseId;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        // On press 💾 Update/Save button Updated method to handle both insert and update logic
        public async Task<int> UpdateRawResponseAsync(AIBookRequest request, string responseData, string endpoint, string statusCode, string? errorMessage = null)
        {
            // Parse IDs from request
            int? userId = int.TryParse(request.UserId, out int uid) ? uid : (int?)null;
            int? bookId = int.TryParse(request.BookId, out int bid) ? bid : (int?)null;
            int? responseId = int.TryParse(request.ResponseId, out int rid) ? rid : (int?)null;

            // Check if we should update existing record
            APIRawResponse existingResponse = null;

            // For update, we should primarily use ResponseId to find the existing record
            if (responseId.HasValue)
            {
                existingResponse = await _context.APIRawResponse
                    .FirstOrDefaultAsync(r => r.ResponseId == responseId.Value);
            }

            // If ResponseId not found, try to find by other criteria
            if (existingResponse == null && userId.HasValue && bookId.HasValue && request.Chapter > 0)
            {
                existingResponse = await _context.APIRawResponse
                    .FirstOrDefaultAsync(r => r.UserId == userId.Value
                                           && r.BookId == bookId.Value
                                           && r.Chapter == request.Chapter);
            }

            if (existingResponse != null)
            {
                // UPDATE existing record
                existingResponse.Endpoint = endpoint;
                existingResponse.Title = request.Title;
                existingResponse.RequestData = JsonConvert.SerializeObject(request);
                existingResponse.ResponseData = responseData;
                existingResponse.StatusCode = statusCode;
                existingResponse.ErrorMessage = errorMessage;
                existingResponse.UpdatedAt = DateTime.UtcNow; // Add update timestamp

                _context.APIRawResponse.Update(existingResponse);
                await _context.SaveChangesAsync();

                Console.WriteLine($"📝 Raw response updated with ID: {existingResponse.ResponseId}");
                return existingResponse.ResponseId;
            }
            else
            {
                // ADD new record (if no existing record found)
                var rawResponse = new APIRawResponse
                {
                    Endpoint = endpoint,
                    Chapter = request.Chapter,
                    Title = request.Title,
                    RequestData = JsonConvert.SerializeObject(request),
                    ResponseData = responseData,
                    UserId = userId,
                    BookId = bookId,
                    StatusCode = statusCode,
                    ErrorMessage = errorMessage,
                    CreatedAt = DateTime.UtcNow
                };

                _context.APIRawResponse.Add(rawResponse);
                await _context.SaveChangesAsync();

                Console.WriteLine($"💾 Raw response saved with ID: {rawResponse.ResponseId}");
                return rawResponse.ResponseId;
            }
        }

        // We will save data in Books table when Finalize button pressed from APIRawResponse table

        public async Task<APIRawResponse> GetRawResponseAsync(int responseId)
        {
            return await _context.APIRawResponse.FirstOrDefaultAsync(r => r.ResponseId == responseId);
        }

        // NEW: Method to save raw response with a Book object
        public async Task<int> SaveRawResponseWithBookAsync(AIBookRequest request, string responseData, string endpoint, string statusCode, Books book, string? errorMessage = null)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Step 1: Save the Book first
                _context.Books.Add(book);
                await _context.SaveChangesAsync();

                Console.WriteLine($"📚 Book saved with ID: {book.BookId}");

                // Step 2: Save the raw response with the BookId
                var rawResponse = new APIRawResponse
                {
                    Endpoint = endpoint,
                    Chapter = request.Chapter,
                    Title = request.Title,
                    RequestData = JsonConvert.SerializeObject(request),
                    ResponseData = responseData,
                    UserId = int.TryParse(request.UserId, out int uid) ? uid : (int?)null,
                    BookId = book.BookId, // Use the generated BookId
                    StatusCode = statusCode,
                    ErrorMessage = errorMessage,
                    CreatedAt = DateTime.UtcNow,
                    ParsedBookId = book.BookId.ToString()
                };

                _context.APIRawResponse.Add(rawResponse);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                Console.WriteLine($"💾 Raw response saved with ID: {rawResponse.ResponseId} for Book ID: {book.BookId}");
                return rawResponse.ResponseId;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        Task<Models.APIRawResponse> IAPIRawResponseService.GetRawResponseAsync(int responseId)
        {
            return GetRawResponseAsync(responseId);
        }
        //================================================
        // Get all API raw responses for a user and book
        //================================================
        public async Task<IEnumerable<APIRawResponse>> GetRawResponsesByUserAndBookAsync(int userId, int bookId, int responseId)
        {
            var query = _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId);

            if (responseId > 0)
            {
                // If responseId is provided and > 0, get that specific response
                query = query.Where(r => r.ResponseId == responseId);
                Console.WriteLine($"🔍 Getting specific response ID: {responseId} for user {userId}, book {bookId}");
            }
            else
            {
                // If responseId is 0 or not provided, get the latest response
                query = query.OrderByDescending(r => r.CreatedAt);
                Console.WriteLine($"🔍 Getting latest response for user {userId}, book {bookId}");
            }

            return await query.ToListAsync();
        }

        // Get latest API raw response for a user and book
        public async Task<APIRawResponse?> GetLatestRawResponseAsync(int userId, int bookId)
        {
            return await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();
        }

        // Get API raw responses by chapter for a user and book
        public async Task<APIRawResponse?> GetRawResponseByChapterAsync(int userId, int bookId, int chapter)
        {
            return await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId && r.Chapter == chapter)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();
        }

        // Get all chapters for a user and book from raw responses
        public async Task<IEnumerable<APIRawResponse>> GetChaptersFromRawResponsesAsync(int userId, int bookId)
        {
            return await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId)
                .OrderBy(r => r.Chapter)
                .ToListAsync();
        }

        // Check if raw responses exist for a user and book
        public async Task<bool> RawResponsesExistAsync(int userId, int bookId)
        {
            return await _context.APIRawResponse
                .AnyAsync(r => r.UserId == userId && r.BookId == bookId);
        }

        // Get raw response count for a user and book
        public async Task<int> GetRawResponseCountAsync(int userId, int bookId)
        {
            return await _context.APIRawResponse
                .CountAsync(r => r.UserId == userId && r.BookId == bookId);
        }
        // Get latest ResponseId for a user and book
        public async Task<int?> GetLatestResponseIdAsync(int userId, int bookId)
        {
            return await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => r.ResponseId)
                .FirstOrDefaultAsync();
        }
        //=========================================
        //   Last Chapter Number
        //=========================================
        public async Task<int> GetLastChapterAsync(int userId, int bookId)
        {
            var lastChapter = await _context.APIRawResponse
                .Where(x => x.UserId == userId && x.BookId == bookId && x.Chapter != null)
                .OrderByDescending(x => x.Chapter)
                .Select(x => x.Chapter)
                .FirstOrDefaultAsync();

            // If no chapter found, return 0 (or 1 if you want to start from Chapter 1)
            return lastChapter;
        }
        // =============================================
        //   Get all chapters for a Book (Ordered)
        // =============================================
        public async Task<List<APIRawResponse>> GetChaptersByBookAsync(int bookId, int userId)
        {
            return await _context.APIRawResponse
                .Where(x => x.BookId == bookId
                         && x.UserId == userId
                         && x.StatusCode != null)
                .OrderBy(x => x.Chapter)
                .ThenBy(x => x.CreatedAt)
                .AsNoTracking()
                .ToListAsync();
        }

        // =============================================
        //   Get single chapter details
        // =============================================
        public async Task<APIRawResponse?> GetChapterAsync(int responseId)
        {
            return await _context.APIRawResponse
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ResponseId == responseId);
        }


    }
}
