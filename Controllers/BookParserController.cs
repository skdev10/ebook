using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using static EBookDashboard.Models.DTO.BookModels;

namespace EBookDashboard.Controllers
{
    public class BookParserController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly BookParserService _service;
        private readonly ApplicationDbContext _context;

        public BookParserController(IWebHostEnvironment env, ApplicationDbContext context)
        {
            _env = env;
            _context = context;
            _service = new BookParserService();
        }

        [HttpGet("/parse-book")]
        public IActionResult ParseBook()
        {
            var filePath = Path.Combine(_env.ContentRootPath, "Response2026.txt");

            if (!System.IO.File.Exists(filePath))
                return NotFound("File not found");

            var rawJson = System.IO.File.ReadAllText(filePath);
            var result = _service.Parse(rawJson);

            return Json(result);
        }

        [HttpPost]
        [Route("BookParser/ParseAndSave")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> ParseAndSave([FromBody] ParseRequest request)
        {
            try
            {
                if (request == null || request.UserId <= 0 || request.BookId <= 0)
                {
                    return BadRequest(new { success = false, message = "Invalid UserId or BookId" });
                }

                // Get all raw responses from database filtered by UserId and BookId, ordered by BookId
                var rawResponses = await _context.APIRawResponse
                    .Where(r => r.UserId == request.UserId && r.BookId == request.BookId)
                    .OrderBy(r => r.BookId)
                    .ThenBy(r => r.ResponseId)
                    .ToListAsync();

                if (rawResponses == null || !rawResponses.Any())
                {
                    return NotFound(new { success = false, message = $"No responses found for UserId={request.UserId} and BookId={request.BookId}" });
                }

                var processedCount = 0;
                var updatedCount = 0;
                var createdCount = 0;
                var errors = new List<string>();
                var extractedContents = new List<object>(); // Store extracted content for display

                // Process each response
                foreach (var rawResponse in rawResponses)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(rawResponse.ResponseData))
                        {
                            errors.Add($"ResponseId {rawResponse.ResponseId}: Response data is empty");
                            continue;
                        }

                        // Use ChapterDataExtractor to convert ResponseData
                        Console.WriteLine($"📖 Extracting data from ResponseId {rawResponse.ResponseId} using ChapterDataExtractor...");
                        Console.WriteLine($"📋 ResponseData length: {rawResponse.ResponseData?.Length ?? 0} characters");
                        
                        // Extract chapter data using ChapterDataExtractor (it can handle JSON string directly)
                        Dictionary<string, object> extractedData = null;
                        try
                        {
                            extractedData = ChapterDataExtractor.ExtractChapterData(rawResponse.ResponseData);
                            
                            if (extractedData == null || extractedData.Count == 0)
                            {
                                Console.WriteLine($"⚠️ ChapterDataExtractor returned empty result for ResponseId {rawResponse.ResponseId}");
                                errors.Add($"ResponseId {rawResponse.ResponseId}: ChapterDataExtractor returned empty result");
                                // Continue processing anyway to save to database
                            }
                            else
                            {
                                Console.WriteLine($"✅ Extracted successfully. Style: {extractedData.GetValueOrDefault("style")}, Chapter: {extractedData.GetValueOrDefault("suggest_chapter_name")}, Content: {extractedData.GetValueOrDefault("content")?.ToString()?.Length ?? 0} chars");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"❌ Error in ChapterDataExtractor: {ex.Message}");
                            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                            errors.Add($"ResponseId {rawResponse.ResponseId}: ChapterDataExtractor error - {ex.Message}");
                            // Create empty extracted data to continue processing
                            extractedData = new Dictionary<string, object>();
                        }

                        // Store extracted content for display (only if we have data)
                        if (extractedData != null && extractedData.Count > 0)
                        {
                            var chapterTitle = extractedData.GetValueOrDefault("suggest_chapter_name")?.ToString();
                            var style = extractedData.GetValueOrDefault("style")?.ToString();
                            var content1 = extractedData.GetValueOrDefault("content")?.ToString();
                            var highlights = extractedData.GetValueOrDefault("highlights");

                            extractedContents.Add(new
                            {
                                responseId = rawResponse.ResponseId,
                                chapterTitle = chapterTitle ?? string.Empty,
                                style = style ?? string.Empty,
                                content = content1 ?? string.Empty,
                                highlights = highlights
                            });
                            
                            Console.WriteLine($"💾 Stored extracted content for ResponseId {rawResponse.ResponseId} - Chapter: {chapterTitle}, Content Length: {content1?.Length ?? 0}");
                        }
                        else
                        {
                            Console.WriteLine($"⚠️ No extracted content to store for ResponseId {rawResponse.ResponseId}");
                        }

                        // Also parse using BookParserService for saving to database
                        Console.WriteLine($"📖 Parsing ResponseId {rawResponse.ResponseId} using BookParserService...");
                        var parsedResult = _service.Parse(rawResponse.ResponseData);
                        Console.WriteLine($"✅ Parsed successfully. Chapter: {parsedResult.Book?.ChapterTitle}, Sections: {parsedResult.Book?.Sections?.Count ?? 0}");

                        // Check if parsed content already exists for this ResponseId
                        var existingParsed = await _context.ParsedBookContent
                            .FirstOrDefaultAsync(p => p.ResponseId == rawResponse.ResponseId);

                        // Get extracted data from ChapterDataExtractor for new fields
                        // Ensure we have a valid dictionary even if extraction failed
                        if (extractedData == null)
                        {
                            extractedData = new Dictionary<string, object>();
                        }

                        var chapterName = extractedData.GetValueOrDefault("chapter_name")?.ToString() ?? string.Empty;
                        var content = extractedData.GetValueOrDefault("content")?.ToString() ?? string.Empty;
                        var highlightOfPreviousChapter = extractedData.GetValueOrDefault("highlight_of_previous_chapter")?.ToString() ?? string.Empty;
                        var userInput = rawResponse.RequestData ?? string.Empty; // Get user input from RequestData

                        // Log extracted values for debugging
                        Console.WriteLine($"🔍 Extracted values for ResponseId {rawResponse.ResponseId}:");
                        Console.WriteLine($"   - ChapterName: {(string.IsNullOrEmpty(chapterName) ? "NULL/EMPTY" : (chapterName.Length > 50 ? chapterName.Substring(0, 50) + "..." : chapterName))}");
                        Console.WriteLine($"   - Content Length: {content?.Length ?? 0}");
                        Console.WriteLine($"   - HighlightOfPreviousChapter Length: {highlightOfPreviousChapter?.Length ?? 0}");
                        Console.WriteLine($"   - UserInput Length: {userInput?.Length ?? 0}");

                        if (existingParsed != null)
                        {
                            // Update existing record
                            existingParsed.ChapterTitle = parsedResult.Book?.ChapterTitle;
                            existingParsed.BookSectionsJson = JsonConvert.SerializeObject(parsedResult.Book?.Sections);
                            existingParsed.HighlightChapterName = parsedResult.Highlights?.ChapterName;
                            existingParsed.HighlightsJson = JsonConvert.SerializeObject(parsedResult.Highlights?.Items);
                            existingParsed.FullParsedDataJson = JsonConvert.SerializeObject(parsedResult);
                            
                            // Update new fields from ChapterDataExtractor
                            existingParsed.ChapterName = chapterName;
                            existingParsed.Content = content;
                            existingParsed.HighlightOfPreviousChapter = highlightOfPreviousChapter;
                            existingParsed.UserInput = userInput;
                            
                            existingParsed.UpdatedAt = DateTime.UtcNow;
                            existingParsed.BookId = rawResponse.BookId;
                            existingParsed.UserId = rawResponse.UserId;

                            // Log entity details before updating
                            Console.WriteLine($"📝 Entity details before Update:");
                            Console.WriteLine($"   - ParsedContentId: {existingParsed.ParsedContentId}");
                            Console.WriteLine($"   - ResponseId: {existingParsed.ResponseId}");
                            Console.WriteLine($"   - ChapterName: {existingParsed.ChapterName ?? "NULL"} -> {chapterName ?? "NULL"}");
                            Console.WriteLine($"   - Content: {(existingParsed.Content?.Length > 0 ? $"{existingParsed.Content.Length} chars" : "NULL/EMPTY")} -> {(content?.Length > 0 ? $"{content.Length} chars" : "NULL/EMPTY")}");
                            
                            _context.ParsedBookContent.Update(existingParsed);
                            updatedCount++;
                            Console.WriteLine($"💾 Updated ParsedBookContent in context for ResponseId {rawResponse.ResponseId} - Chapter: {chapterName}");
                        }
                        else
                        {
                            // Create new record
                            var parsedContent = new ParsedBookContent
                            {
                                ResponseId = rawResponse.ResponseId,
                                BookId = rawResponse.BookId,
                                UserId = rawResponse.UserId,
                                ChapterTitle = parsedResult.Book?.ChapterTitle,
                                BookSectionsJson = JsonConvert.SerializeObject(parsedResult.Book?.Sections),
                                HighlightChapterName = parsedResult.Highlights?.ChapterName,
                                HighlightsJson = JsonConvert.SerializeObject(parsedResult.Highlights?.Items),
                                FullParsedDataJson = JsonConvert.SerializeObject(parsedResult),
                                
                                // Set new fields from ChapterDataExtractor
                                ChapterName = chapterName,
                                Content = content,
                                HighlightOfPreviousChapter = highlightOfPreviousChapter,
                                UserInput = userInput,
                                
                                CreatedAt = DateTime.UtcNow
                            };

                            // Log entity details before adding
                            Console.WriteLine($"📝 Entity details before Add:");
                            Console.WriteLine($"   - ResponseId: {parsedContent.ResponseId}");
                            Console.WriteLine($"   - BookId: {parsedContent.BookId}");
                            Console.WriteLine($"   - UserId: {parsedContent.UserId}");
                            Console.WriteLine($"   - ChapterName: {parsedContent.ChapterName ?? "NULL"}");
                            Console.WriteLine($"   - Content: {(parsedContent.Content?.Length > 0 ? $"{parsedContent.Content.Length} chars" : "NULL/EMPTY")}");
                            Console.WriteLine($"   - HighlightOfPreviousChapter: {(parsedContent.HighlightOfPreviousChapter?.Length > 0 ? $"{parsedContent.HighlightOfPreviousChapter.Length} chars" : "NULL/EMPTY")}");
                            
                            _context.ParsedBookContent.Add(parsedContent);
                            createdCount++;
                            Console.WriteLine($"💾 Added ParsedBookContent to context for ResponseId {rawResponse.ResponseId} - Chapter: {chapterName}, Content Length: {content?.Length ?? 0}");
                        }

                        processedCount++;
                        Console.WriteLine($"✅ Successfully processed ResponseId {rawResponse.ResponseId}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"ResponseId {rawResponse.ResponseId}: {ex.Message}");
                        Console.WriteLine($"❌ Error processing ResponseId {rawResponse.ResponseId}: {ex.Message}");
                        Console.WriteLine($"❌ Stack Trace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"❌ Inner Exception: {ex.InnerException.Message}");
                        }
                    }
                }

                // Save all changes to parsedbookcontent table
                Console.WriteLine($"💾 Saving {processedCount} parsed records to parsedbookcontent table...");
                try
                {
                    var savedCount = await _context.SaveChangesAsync();
                    Console.WriteLine($"✅ Successfully saved {savedCount} changes to parsedbookcontent table");
                }
                catch (Exception saveEx)
                {
                    Console.WriteLine($"❌ Error saving to database: {saveEx.Message}");
                    Console.WriteLine($"❌ Stack Trace: {saveEx.StackTrace}");
                    if (saveEx.InnerException != null)
                    {
                        Console.WriteLine($"❌ Inner Exception: {saveEx.InnerException.Message}");
                    }
                    throw; // Re-throw to be caught by outer catch
                }

                return Ok(new
                {
                    success = true,
                    message = $"Successfully processed {processedCount} response(s). Created: {createdCount}, Updated: {updatedCount}",
                    totalResponses = rawResponses.Count,
                    processedCount = processedCount,
                    createdCount = createdCount,
                    updatedCount = updatedCount,
                    errors = errors.Any() ? errors : null,
                    extractedContents = extractedContents // Return extracted content for display
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing and saving: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        public class ParseRequest
        {
            public int UserId { get; set; }
            public int BookId { get; set; }
        }
    }
}
