using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace EBookDashboard.Services
{
    public class BookProcessingService
    {
        private readonly ApplicationDbContext _context;
      
        public BookProcessingService(ApplicationDbContext context)
        {
            _context = context;
        }
      
        // Method to process a single raw response
        public async Task<bool> ProcessRawResponse(int responseId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Get the raw response
                var rawResponse = await _context.APIRawResponse
                    .FirstOrDefaultAsync(r => r.ResponseId == responseId);

                if (rawResponse == null)
                {
                    Console.WriteLine($"❌ Raw response with ID {responseId} not found");
                    return false;
                }
                // ✅ ADD NULL CHECKING
                if (string.IsNullOrEmpty(rawResponse.ResponseData))
                {
                    Console.WriteLine($"❌ No ResponseData found for response ID {responseId}");
                    return false;
                }

                // Parse the JSON response with error handling
               var apiResponse = JsonConvert.DeserializeObject<AIBookResponse>(rawResponse.ResponseData);
                 if (apiResponse?.Data == null)
                    {
                        Console.WriteLine($"❌ Invalid ResponseData format for response ID {responseId}");
                        return false;
                 }
                // ✅ ADD NULL CHECKING FOR CONTENT
                if (string.IsNullOrEmpty(apiResponse.Data.Content))
                {
                    Console.WriteLine($"❌ No content found in response {responseId}");
                    return false;
                }
                // CORRECT: Call the static method from helper class
                var parsedChapters = ChapterParserHelper.ParseChaptersFromContent(apiResponse.Data.Content);

                // Create and save the book
                var book = await CreateBookFromResponse(rawResponse, apiResponse, parsedChapters);

                // Save chapters
                await CreateChaptersForBook(book, parsedChapters, rawResponse.UserId ?? 1);

                // Update the raw response with the parsed book ID, Convert int to string for ParsedBookId
                rawResponse.ParsedBookId = book.BookId.ToString();
                _context.APIRawResponse.Update(rawResponse);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                Console.WriteLine($"✅ Successfully processed response {responseId} into Book ID: {book.BookId} with {parsedChapters.Count} chapters");
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"❌ Error processing response {responseId}: {ex.Message}");
                return false;
            }
        }

        // Method to process all unprocessed raw responses
        public async Task<int> ProcessAllUnprocessedResponses()
        {
            var unprocessedResponses = await _context.APIRawResponse
                .Where(r => string.IsNullOrEmpty(r.ParsedBookId) && !string.IsNullOrEmpty(r.ResponseData))
                .ToListAsync();

            int successCount = 0;

            foreach (var response in unprocessedResponses)
            {
                var result = await ProcessRawResponse(response.ResponseId);
                if (result) successCount++;
            }

            Console.WriteLine($"✅ Processed {successCount} out of {unprocessedResponses.Count} unprocessed responses");
            return successCount;

        }
        private async Task<Books> CreateBookFromResponse(APIRawResponse rawResponse, AIBookResponse apiResponse, List<ParsedChapter> chapters)
        {
            // Parse request data to get original parameters if needed
            var requestData = string.IsNullOrEmpty(rawResponse.RequestData)
                ? null
                : JsonConvert.DeserializeObject<AIBookRequest>(rawResponse.RequestData);

            var book = new Books
            {
                Title = apiResponse.Data?.SuggestChapterName ?? "AI Generated Book",
                UserId = rawResponse.UserId ?? 1,
                AuthorId = rawResponse.UserId ?? 1, // Assuming same as user
                CategoryId = 1, // Default category
                LanguageId = 1, // Default language
                CoverImagePath = "/images/default-cover.jpg",
                ManuscriptPath = "/manuscripts/default.pdf",
                Genre = "Educational",
                Description = requestData != null
                    ? $"AI-generated book about {requestData.UserInput}"
                    : "AI-generated book",
                WordCount = ChapterParserHelper.CalculateTotalWordCount(chapters),
                Status = "Generated",
                CreatedAt = rawResponse.CreatedAt //DateTime.UtcNow
            };

            _context.Books.Add(book);
            await _context.SaveChangesAsync();
            return book;
        }
        private async Task CreateChaptersForBook(Books book, List<ParsedChapter> parsedChapters, int userId)
        {
            var chapters = new List<Chapters>();

            for (int i = 0; i < parsedChapters.Count; i++)
            {
                var parsedChapter = parsedChapters[i];
                var chapterWordCount = ChapterParserHelper.CountWords(parsedChapter.Content);

                var chapter = new Chapters
                {
                    BookId = book.BookId,
                    Title = parsedChapter.Title,
                    SubTitle = string.Empty,
                    Content = parsedChapter.Content,
                    ChapterNumber = parsedChapter.ChapterNumber,
                    SrNo = parsedChapter.ChapterNumber,
                    OrderIndex = parsedChapter.ChapterNumber,
                    LanguageId = 1, // Default language
                    WordCount = chapterWordCount,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedByUserId = userId,
                    IsPublished = false,
                    Status = "Generated"
                };

                chapters.Add(chapter);
            }

            _context.Chapters.AddRange(chapters);
            await _context.SaveChangesAsync();
        }
    }
}
