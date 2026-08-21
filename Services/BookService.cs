using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Globalization;
using System.Text.RegularExpressions;
namespace EBookDashboard.Services
{
    public class BookService : IBookService
    {
        private readonly ApplicationDbContext _context;
        private readonly IChapterIterationService _chapterIterations;
        private readonly IWebHostEnvironment _env;

        public BookService(ApplicationDbContext context, IChapterIterationService chapterIterations, IWebHostEnvironment env)
        {
            _context = context;
            _chapterIterations = chapterIterations;
            _env = env;
        }

        public async Task<Books?> GetBookByIdAsync(int bookId)
        {
            return await _context.Books.FindAsync(bookId);
        }
        // ----- Books -----
        public async Task<IEnumerable<Books>> GetAllBooksAsync()
        {
            return await _context.Books
                                 .Include(b => b.Chapters)
                                 .ToListAsync();
        }

        //public async Task<Books?> GetBookByIdAsync(int bookId)
        //{
        //    return await _context.Books
        //                         .Include(b => b.Chapters)
        //                         .FirstOrDefaultAsync(b => b.BookId == bookId);
        //}
        //public Task<BookDetailsDto?> GetBookDetailsAsync(int userId, int bookId)
        //{
        //    throw new NotImplementedException();
        //}
        //============================================
        // ----- Get Book Details with Chapters -----
        //============================================
        //public async Task<BookDetailsDto?> GetBookDetailsAsync(int userId, int bookId, int responseId)
        //{
        //    // It should be from APIRawResponse table
        //    return await _context.Books
        //        .Where(b => b.UserId == userId && b.BookId == bookId)
        //        .Select(b => new BookDetailsDto
        //        {
        //            BookId = b.BookId,
        //            Title = b.Title,
        //            Description = b.Description,
        //            Dedication= b.Dedication,
        //            Ghostwriting= b.Ghostwriting,
        //            Epigraph= b.Epigraph,
        //            CreatedAt = b.CreatedAt,
        //            Genre = b.Genre,
        //            Status=b.Status,
        //            TotalChapters = b.Chapters.Count,
        //            Chapters = b.Chapters
        //                .OrderBy(c => c.ChapterNumber)
        //                .Select(c => new ChapterDto
        //                {
        //                    ChapterNumber = c.ChapterNumber,
        //                    Title = c.Title,
        //                    Content = c.Content,
        //                    StatusCode = c.Status
        //                })
        //                .ToList()
        //        })
        //        .FirstOrDefaultAsync();
        //}
        //=======================================
        // Chapter Number Generation
        //=====================================
        public async Task<int> GetNextChapterNumberAsync(int userId, int bookId)
        {
            if (userId <= 0 || bookId <= 0)
            {
                Console.WriteLine($"❌ Invalid parameters: UserId={userId}, BookId={bookId}");
                return 1;
            }

            try
            {
                Console.WriteLine($"🔍 Getting next chapter number for User: {userId}, Book: {bookId}");

                // Highest chapter from saved manuscript rows (Finalize / library)
                int maxFromChapters = 0;
                if (await _context.Chapters.AnyAsync(c => c.BookId == bookId))
                {
                    maxFromChapters = await _context.Chapters
                        .Where(c => c.BookId == bookId)
                        .MaxAsync(c => c.ChapterNumber > 0 ? c.ChapterNumber : (c.OrderIndex > 0 ? c.OrderIndex : c.SrNo));
                }

                // Highest chapter from generation history (PreviewOnly saves here too)
                int maxFromRaw = 0;
                if (await _context.APIRawResponse.AnyAsync(r => r.UserId == userId && r.BookId == bookId))
                {
                    maxFromRaw = await _context.APIRawResponse
                        .Where(r => r.UserId == userId && r.BookId == bookId)
                        .MaxAsync(r => r.Chapter);
                }

                var maxChapter = Math.Max(maxFromChapters, maxFromRaw);
                if (maxChapter <= 0)
                {
                    Console.WriteLine($"✅ No chapters found. Starting with chapter 1");
                    return 1;
                }

                int nextChapter = maxChapter + 1;
                Console.WriteLine($"✅ Next chapter: {nextChapter} (max from library: {maxFromChapters}, from AI history: {maxFromRaw})");
                return nextChapter;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error getting next chapter number: {ex.Message}");
                return 1;
            }
        }

        //==================================================
        // ----- Get Book Details from APIRawResponse -----
        //==================================================
        public async Task<BookDetailsDto?> GetBookDetailsFromRawDataAsync(int userId, int bookId)
        {
            var rawResponse = await _context.APIRawResponse
        .FirstOrDefaultAsync(b => b.UserId == userId && b.BookId == bookId);

            if (rawResponse == null)
                return null;

            // Parse chapter number safely
            int chapterNumber = 1;
            if (rawResponse.Chapter > 0)
            {
                chapterNumber = rawResponse.Chapter;
            }

            return new BookDetailsDto
            {
                BookId = rawResponse.BookId ?? 0,
                Title = rawResponse.Title ?? "Untitled Book",
                Description = "",
                Genre = "",
                TotalChapters = 1,
                Chapters = new List<ChapterDto>
                    {
                        new ChapterDto
                            {
                                ChapterNumber = chapterNumber,
                                Title = rawResponse.Title ?? "Untitled Chapter",
                                Content = rawResponse.ResponseData ?? "",
                                StatusCode = "Generated",
                                CreatedAt=rawResponse.CreatedAt
                            }
                    },
                RawResponseId = rawResponse.ResponseId,
                Endpoint = rawResponse.Endpoint ?? "",
                CreatedAt = rawResponse.CreatedAt,
            };
        }

        //=============================================
        // ----- Create Book from Request -----
        //=============================================
        public async Task<Books> CreateBookFromRequestAsync(CreateBookRequest request)
        {
            if (BookDraftGuard.IsPlaceholderTitle(request.Title))
            {
                var reusable = await BookDraftGuard.FindReusableEmptyUntitledAsync(_context, request.UserId);
                if (reusable != null)
                {
                    reusable.UpdatedAt = DateTime.UtcNow;
                    _context.Books.Update(reusable);
                    await _context.SaveChangesAsync();
                    return reusable;
                }
            }
            else
            {
                await BookDraftGuard.EnsureUniqueTitleAsync(_context, request.UserId, request.Title);
            }

            var book = new Books
            {
                AuthorId = request.AuthorId,
                UserId = request.UserId,
                CategoryId = request.CategoryId,
                Title = request.Title.Trim(),
                Subtitle = request.Subtitle,
                AuthorCode = request.AuthorCode,
                BookCode = request.BookCode,
                LanguageId = request.LanguageId,
                CoverImagePath = request.CoverImagePath ?? "",
                ManuscriptPath = request.ManuscriptPath ?? "",
                Genre = request.Genre?.Trim() ?? "",
                Description = request.Description?.Trim(),
                WordCount = request.WordCount,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Status = BookStatus.Draft.ToString(),
                Dedication = request.Dedication?.Trim() ?? "",
                Ghostwriting = request.Ghostwriting?.Trim() ?? "",
                Epigraph = request.Epigraph?.Trim() ?? "",
            };

            return await CreateBookAsync(book);
        }
        // ----- to make BookId relationship Key -----
        public async Task<Books> CreateBookAsync(Books book)
        {
            // Set default values if not provided
            book.CreatedAt = DateTime.UtcNow;
            book.UpdatedAt = DateTime.UtcNow;
            book.Status = book.Status ?? BookStatus.Draft.ToString();
            if (!BookDraftGuard.IsPlaceholderTitle(book.Title))
                await BookDraftGuard.EnsureUniqueTitleAsync(_context, book.UserId, book.Title);

            _context.Books.Add(book);
            await _context.SaveChangesAsync();
            return book;
        }
        //=======================================
        public async Task<bool> UpdateBookAsync(Books book)
        {
            book.UpdatedAt = DateTime.UtcNow;
            _context.Books.Update(book);
            return await _context.SaveChangesAsync() > 0;
        }

        /// <summary>
        /// Updates the cover image path for a book. Only updates if the book belongs to the given user.
        /// </summary>
        public async Task<bool> UpdateBookCoverImagePathAsync(int bookId, int userId, string coverImagePath)
        {
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
            if (book == null) return false;
            book.CoverImagePath = coverImagePath ?? "";
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

        /// <inheritdoc />
        public async Task<bool> DeleteBookForUserAsync(int bookId, int userId)
        {
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
            if (book == null) return false;

            var settingsKeys = new List<string>
            {
                BookResumeUrlHelper.PerBookSettingsKey(bookId),
                $"user:{userId}:lastBookId"
            };
            var prefixed = await _context.Settings
                .Where(s => s.Key.StartsWith($"book:{bookId}:"))
                .ToListAsync();
            var extra = await _context.Settings
                .Where(s => settingsKeys.Contains(s.Key))
                .ToListAsync();

            _context.Settings.RemoveRange(prefixed);
            _context.Settings.RemoveRange(extra);
            _context.Books.Remove(book);
            await _context.SaveChangesAsync();

            TryDeleteBookUploadFolder(userId, bookId);
            return true;
        }

        /// <inheritdoc />
        public async Task<bool> MarkPublishedAsync(int bookId, int userId, CancellationToken cancellationToken = default)
        {
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
            if (book == null) return false;
            var dirty = false;
            if (!BookFlowStateService.IsPublishedStatus(book.Status))
            {
                book.Status = "Published";
                dirty = true;
            }
            if (book.isActive != 0)
            {
                book.isActive = 0;
                dirty = true;
            }
            if (dirty)
            {
                book.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
            return true;
        }

        private void TryDeleteBookUploadFolder(int userId, int bookId)
        {
            try
            {
                var root = _env.WebRootPath ?? "";
                if (string.IsNullOrEmpty(root)) return;
                var dir = Path.Combine(root, "uploads", userId.ToString(), "books", bookId.ToString());
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                /* best-effort file cleanup */
            }
        }

        // ----- Categories -----
        public async Task<IEnumerable<Categories>> GetAllCategoriesAsync()
        {
            return await _context.Categories
                                 .OrderBy(c => c.CategoryName)
                                 .ToListAsync();
        }

        // ----- Book Prices -----
        public async Task<BookPrice?> GetPriceByBookIdAsync(int bookId)
        {
            return await _context.BookPrice
                                 .FirstOrDefaultAsync(p => p.BookId == bookId);
        }

        public async Task<BookPrice> SetBookPriceAsync(BookPrice price)
        {
            _context.BookPrice.Add(price);
            await _context.SaveChangesAsync();
            return price;
        }

        public async Task<bool> UpdateBookPriceAsync(BookPrice price)
        {
            _context.BookPrice.Update(price);
            return await _context.SaveChangesAsync() > 0;
        }

        // ----- Book Versions -----
        public async Task<IEnumerable<BookVersion>> GetVersionsByBookIdAsync(int bookId)
        {
            return await _context.BookVersions
                                 .Where(v => v.BookId == bookId)
                                 .ToListAsync();
        }

        public async Task<BookVersion> AddBookVersionAsync(BookVersion version)
        {
            _context.BookVersions.Add(version);
            await _context.SaveChangesAsync();
            return version;
        }

        public async Task<BookVersion?> GetVersionByIdAsync(int versionId)
        {
            return await _context.BookVersions
                                 .FirstOrDefaultAsync(v => v.BookVersionId == versionId);
        }

        //Task<IEnumerable<Categories>> IBookService.GetAllCategoriesAsync()
        //{
        //    throw new NotImplementedException();
        //}
        public async Task<UserBooksViewModel> GetUserBooksWithChaptersAsync(int userId)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserId == userId);

            var books = await _context.Books
                .Where(b => b.UserId == userId)
                .Include(b => b.Chapters)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new UserBook
                {
                    BookId = b.BookId,
                    Title = b.Title,
                    Description = b.Description ?? "No description available",
                    Genre = b.Genre ?? "Uncategorized",
                    Status = b.Status,
                    CreatedAt = b.CreatedAt,
                    UpdatedAt = b.UpdatedAt,
                    TotalChapters = b.Chapters.Count,
                    TotalWords = b.Chapters.Sum(c => c.Content != null ? CalculateWordCount(c.Content) : 0),
                    CoverImagePath = b.CoverImagePath ?? "/images/default-book-cover.jpg",
                    Chapters = b.Chapters
                        .OrderBy(c => c.ChapterNumber)
                        .Select(c => new UserChapter
                        {
                            ChapterId = c.ChapterId,
                            ChapterNumber = c.ChapterNumber,
                            Title = c.Title,
                            Content = c.Content,
                            Status = c.Status,
                            CreatedAt = c.CreatedAt,
                            UpdatedAt = c.UpdatedAt,
                            WordCount = c.Content != null ? CalculateWordCount(c.Content) : 0,
                            PreviewContent = c.Content != null ? GetContentPreview(c.Content, CalculateWordCount(c.Content)) : "No content available"
                        })
                        .ToList()
                })
                .ToListAsync();

            return new UserBooksViewModel
            {
                UserId = userId,
                UserName = user?.FullName ?? "User",
                Books = books
            };
        }

        public async Task<UserBook?> GetUserBookDetailsAsync(int userId, int bookId)
        {
            return await _context.Books
                .Where(b => b.UserId == userId && b.BookId == bookId)
                .Include(b => b.Chapters)
                .Select(b => new UserBook
                {
                    BookId = b.BookId,
                    Title = b.Title,
                    Description = b.Description ?? "No description available",
                    Genre = b.Genre ?? "Uncategorized",
                    Status = b.Status,
                    CreatedAt = b.CreatedAt,
                    UpdatedAt = b.UpdatedAt,
                    TotalChapters = b.Chapters.Count,
                    TotalWords = b.Chapters.Sum(c => CalculateWordCount(c.Content)),
                    CoverImagePath = b.CoverImagePath ?? "/images/default-book-cover.jpg",
                    Chapters = b.Chapters
                        .OrderBy(c => c.ChapterNumber)
                        .Select(c => new UserChapter
                        {
                            ChapterId = c.ChapterId,
                            ChapterNumber = c.ChapterNumber,
                            Title = c.Title,
                            Content = c.Content,
                            Status = c.Status,
                            CreatedAt = c.CreatedAt,
                            UpdatedAt = c.UpdatedAt,
                            WordCount = c.Content != null ? CalculateWordCount(c.Content) : 0,
                            PreviewContent = GetContentPreview(c.Content, CalculateWordCount(c.Content))
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();
        }
        public async Task<List<UserBook>> GetUserBooksSummaryAsync(int userId)
        {
            return await _context.Books
                .Where(b => b.UserId == userId)
                .Include(b => b.Chapters)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new UserBook
                {
                    BookId = b.BookId,
                    Title = b.Title,
                    Description = b.Description ?? "No description available",
                    Genre = b.Genre ?? "Uncategorized",
                    Status = b.Status,
                    CreatedAt = b.CreatedAt,
                    UpdatedAt = b.UpdatedAt,
                    TotalChapters = b.Chapters.Count,
                    TotalWords = b.Chapters.Sum(c => CalculateWordCount(c.Content)),
                    CoverImagePath = b.CoverImagePath ?? "/images/default-book-cover.jpg"
                })
                .ToListAsync();
        }
        private int CalculateWordCount(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0;

            // Remove HTML tags for accurate word count
            var plainText = System.Text.RegularExpressions.Regex.Replace(
                content, "<.*?>", string.Empty);

            // Split by multiple whitespace characters and count non-empty words
            var wordCount = 0;
            var wordPattern = new System.Text.RegularExpressions.Regex(@"\b\w+\b");
            wordCount = wordPattern.Matches(plainText).Count;

            return wordCount;
        }

        private string GetContentPreview(string? content, int maxLength = 150)
        {
            if (string.IsNullOrEmpty(content))
                return "No content available";

            // Remove HTML tags for preview
            var plainText = System.Text.RegularExpressions.Regex.Replace(
                content, "<.*?>", string.Empty);

            return plainText.Length <= maxLength
                ? plainText
                : plainText.Substring(0, maxLength) + "...";
        }
     
        //=======================================
        // Get a single saved API response for a user and book
        //====================================
        public async Task<APIRawResponse?> GetLatestBookResponseAsync(int userId, int bookId)
        {
            // APIRawResponse table

            return await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();
        }
      
        //==========================================
        //   Get all saved books for a user
        //==========================================
        public async Task<IEnumerable<SavedBookDto>> GetSavedBooksForDropdownAsync(int userId, int bookId)
        {
            if (userId == 0)
                throw new ArgumentException("UserId is required.");

            var query = _context.Books
                .Where(b => b.UserId == userId);

            // Apply BookId filter ONLY if bookId != 0
            if (bookId != 0)
            {
                query = query.Where(b => b.BookId == bookId);
            }

            return await query
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new SavedBookDto
                {
                    UserId = b.UserId,
                    BookId = b.BookId,
                    BookTitle = b.Title,
                    CreatedDate = b.CreatedAt
                })
                .ToListAsync();
        }
        //==========================================
        //      When Book Selected from Drop-Down
        //   Get all saved book's Chapter Nos
        //==========================================
        public async Task<IEnumerable<ChapterDto>> GetSavedBooksChaptersAsync(int userId, int bookId)
        {
            if (userId == 0)
                throw new ArgumentException("UserId is required.");

            // Metadata only — never pull LONGTEXT bodies for the chapter dropdown / progress bar.
            var merged = await GetMergedPreviewChaptersAsync(userId, bookId, noTracking: true, includeBodies: false);
            foreach (var c in merged)
                c.Content = string.Empty;
            return merged;
        }
        // Get latest BookId for a user
        public async Task<int?> GetLatestBookIdAsync(int userId)
        {
            if (userId == 0)
                throw new ArgumentException("UserId is required.");

            var latestBook = await _context.Books
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new { b.BookId })
                .FirstOrDefaultAsync();

            return latestBook?.BookId;
        }

        // Get latest book with details
        public async Task<SavedBookDto?> GetLatestBookAsync(int userId)
        {
            if (userId == 0)
                throw new ArgumentException("UserId is required.");

            return await _context.Books
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new SavedBookDto
                {
                    BookId = b.BookId,
                    BookTitle = b.Title,
                    CreatedDate = b.CreatedAt
                })
                .FirstOrDefaultAsync();
        }
        public async Task<(bool success, string data, string message)> GetBookApiResponseAsync(int userId, int bookId)
        {
            var response = await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();

            if (response == null)
                return (false, null, "No response found.");

            return (true, response.ResponseData, "Response retrieved successfully.");
        }

        /// <summary>
        /// Resolve manuscript body from <see cref="APIRawResponse.Content"/> or JSON in <see cref="APIRawResponse.ResponseData"/>.
        /// </summary>
        private static string ResolveChapterBodyContent(string? content, string? responseData)
        {
            if (!string.IsNullOrWhiteSpace(content))
                return ChapterContentNormalizer.NormalizeForManuscript(content);

            if (string.IsNullOrWhiteSpace(responseData))
                return string.Empty;

            return ChapterContentNormalizer.NormalizeForManuscript(responseData);
        }

        /// <summary>
        /// One row per chapter: latest by CreatedAt, ordered by chapter number (fixes duplicate rows and wrong ordering).
        /// </summary>
        private async Task<List<ChapterDto>> GetLatestChapterRowsAsync(int userId, int bookId, bool noTracking = false, bool includeBodies = true)
        {
            var bookKey = bookId.ToString(CultureInfo.InvariantCulture);
            var query = _context.APIRawResponse.AsNoTracking()
                .Where(c => c.UserId == userId
                            && (c.BookId == bookId
                                || (c.ParsedBookId != null && c.ParsedBookId == bookKey)))
                .OrderByDescending(c => c.CreatedAt);

            List<ChapterDto> mapped;
            if (includeBodies)
            {
                // Never SELECT ResponseData (500k+ JSON blobs). Content column is the manuscript.
                var raw = await query
                    .Select(c => new
                    {
                        c.ResponseId,
                        c.Chapter,
                        c.Title,
                        c.RequestData,
                        c.Content,
                        c.StatusCode,
                        c.CreatedAt
                    })
                    .ToListAsync();
                mapped = raw.Select(c => new ChapterDto
                {
                    ResponseId = c.ResponseId,
                    ChapterNumber = c.Chapter <= 0 ? 1 : c.Chapter,
                    Title = c.Title ?? "Untitled Chapter",
                    RequestData = c.RequestData,
                    Content = ResolveChapterBodyContent(c.Content, null),
                    StatusCode = c.StatusCode ?? "Draft",
                    CreatedAt = c.CreatedAt
                }).ToList();
            }
            else
            {
                var raw = await query
                    .Select(c => new
                    {
                        c.ResponseId,
                        c.Chapter,
                        c.Title,
                        c.RequestData,
                        c.StatusCode,
                        c.CreatedAt
                    })
                    .ToListAsync();
                mapped = raw.Select(c => new ChapterDto
                {
                    ResponseId = c.ResponseId,
                    ChapterNumber = c.Chapter <= 0 ? 1 : c.Chapter,
                    Title = c.Title ?? "Untitled Chapter",
                    RequestData = c.RequestData,
                    Content = string.Empty,
                    StatusCode = c.StatusCode ?? "Draft",
                    CreatedAt = c.CreatedAt
                }).ToList();
            }

            return mapped
                .GroupBy(c => c.ChapterNumber)
                .Select(g => g.First())
                .OrderBy(c => c.ChapterNumber)
                .Where(c => includeBodies
                    ? (!string.IsNullOrWhiteSpace(c.Content) || !string.IsNullOrWhiteSpace(c.Title))
                    : !string.IsNullOrWhiteSpace(c.Title) || c.ChapterNumber > 0)
                .ToList();
        }

        private async Task<List<ChapterDto>> LoadRawChapterRowsAsync(int userId, int bookId, int chapterNo, int? responseId)
        {
            var bookKey = bookId.ToString(CultureInfo.InvariantCulture);
            IQueryable<APIRawResponse> rawQuery = _context.APIRawResponse.AsNoTracking()
                .Where(c => c.UserId == userId
                            && (c.BookId == bookId || (c.ParsedBookId != null && c.ParsedBookId == bookKey)));
            if (chapterNo > 0)
                rawQuery = rawQuery.Where(c => c.Chapter == chapterNo);
            if (responseId.HasValue && responseId.Value > 0)
                rawQuery = rawQuery.Where(c => c.ResponseId == responseId.Value);

            var rawChapters = await rawQuery
                .OrderByDescending(c => c.CreatedAt)
                .ThenBy(c => c.ResponseId)
                .Select(c => new
                {
                    c.ResponseId,
                    c.Chapter,
                    c.Title,
                    c.RequestData,
                    c.Content,
                    c.StatusCode,
                    c.CreatedAt
                })
                .ToListAsync();

            return rawChapters.Select(c => new ChapterDto
            {
                ResponseId = c.ResponseId,
                ChapterNumber = c.Chapter <= 0 ? 1 : c.Chapter,
                Title = c.Title ?? "Untitled Chapter",
                RequestData = c.RequestData,
                Content = ResolveChapterBodyContent(c.Content, null),
                StatusCode = c.StatusCode ?? "Draft",
                CreatedAt = c.CreatedAt,
            }).ToList();
        }

        // ====Book======================================= 100% OK
        // Load Selected Book from Drop-Down from Books table
        // ==============================================
        public Task<BookDetailsResponseDto?> GetBookDetailsAsync(int userId, int bookId)
        {
            // Read-only: never mutate isActive on fetch. Table-wide ExecuteUpdate here raced with
            // page-init writes (SetActiveBook / middleware) and hung the portal under MySQL lock wait + retry.
            // Merged chapters match formatter/preview so Writer sees the same manuscript after initialize.
            return GetBookDetailsForPreviewAsync(userId, bookId);
        }

        /// <summary>
        /// Lightweight book + chapters load for formatter/preview. Skips isActive updates for faster response.
        /// Use this for GetFullBookContent (Book Formatter page) so loading does not time out.
        /// </summary>
        public async Task<BookDetailsResponseDto?> GetBookDetailsForPreviewAsync(int userId, int bookId)
        {
            try
            {
                var book = await _context.Books
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.UserId == userId && b.BookId == bookId);

                if (book == null)
                    return null;

                var chapters = await GetMergedPreviewChaptersAsync(userId, bookId, noTracking: true);

                var authorName = await ResolveAuthorDisplayNameAsync(userId);
                var displayTitle = await BookTitleResolver.ResolveDisplayTitleAsync(
                    _context, userId, book.BookId, book.Title);

                return new BookDetailsResponseDto
                {
                    Success = true,
                    BookId = book.BookId,
                    BookTitle = displayTitle,
                    Subtitle = book.Subtitle,
                    Description = book.Description,
                    Genre = book.Genre,
                    AuthorName = authorName,
                    CoverImagePath = book.CoverImagePath,
                    TotalChapters = chapters.Count,
                    Chapters = chapters,
                    BookContentHtml = book.BookContentHtml
                };
            }
            catch (Exception ex)
            {
                return new BookDetailsResponseDto { Success = false, Message = ex.Message };
            }
        }

        /// <inheritdoc />
        public async Task<BookManuscriptStats.ManuscriptSummary> GetManuscriptSummaryAsync(int userId, int bookId)
        {
            var book = await _context.Books.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
            if (book == null)
                return new BookManuscriptStats.ManuscriptSummary(0, "");

            var details = await GetBookDetailsForPreviewAsync(userId, bookId);
            return BookManuscriptStats.Resolve(book, details);
        }

        /// <inheritdoc />
        public async Task SyncBookMetadataFromManuscriptAsync(int userId, int bookId)
        {
            var book = await _context.Books
                .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId);
            if (book == null) return;

            var needsWords = book.WordCount <= 0;
            var needsDesc = string.IsNullOrWhiteSpace(book.Description);
            if (!needsWords && !needsDesc) return;

            var summary = await GetManuscriptSummaryAsync(userId, bookId);
            var changed = false;

            if (needsWords && summary.WordCount > 0)
            {
                book.WordCount = summary.WordCount;
                changed = true;
            }

            if (needsDesc && !string.IsNullOrWhiteSpace(summary.Description))
            {
                book.Description = summary.Description.Length > 2000
                    ? summary.Description.Substring(0, 1997) + "…"
                    : summary.Description;
                changed = true;
            }

            if (!changed) return;
            book.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Formatter / cover-flow preview: use official <c>chapters</c> rows when finalized (ReadOnly/Final);
        /// otherwise latest <c>apirawresponse</c> per chapter (drafts from AI Writer).
        /// </summary>
        private async Task<List<ChapterDto>> GetMergedPreviewChaptersAsync(int userId, int bookId, bool noTracking = true, bool includeBodies = true)
        {
            var rawLatest = await GetLatestChapterRowsAsync(userId, bookId, noTracking, includeBodies);

            List<(int ChapterNumber, string? Title, bool IsFinalized, int IterationNumber, int ResponseId, DateTime CreatedAt, string Content)> iterHeads;
            var iterBase = _context.ChapterIterations.AsNoTracking()
                .Where(i => i.UserId == userId && i.BookId == bookId);
            if (includeBodies)
            {
                var iterRows = await iterBase
                    .Where(i => i.Content != null && i.Content != "")
                    .Select(i => new
                    {
                        i.ChapterNumber,
                        i.Title,
                        i.IsFinalized,
                        i.IterationNumber,
                        i.ResponseId,
                        i.GenerationDate,
                        i.GenerationTime,
                        i.Content
                    })
                    .ToListAsync();
                iterHeads = iterRows
                    .GroupBy(i => i.ChapterNumber)
                    .Select(g => g.OrderByDescending(x => x.IsFinalized).ThenByDescending(x => x.IterationNumber).First())
                    .Select(i => (i.ChapterNumber, i.Title, i.IsFinalized, i.IterationNumber, i.ResponseId ?? 0, i.GenerationDate.Add(i.GenerationTime), i.Content ?? ""))
                    .ToList();
            }
            else
            {
                var iterRows = await iterBase
                    .Select(i => new
                    {
                        i.ChapterNumber,
                        i.Title,
                        i.IsFinalized,
                        i.IterationNumber,
                        i.ResponseId,
                        i.GenerationDate,
                        i.GenerationTime
                    })
                    .ToListAsync();
                iterHeads = iterRows
                    .GroupBy(i => i.ChapterNumber)
                    .Select(g => g.OrderByDescending(x => x.IsFinalized).ThenByDescending(x => x.IterationNumber).First())
                    .Select(i => (i.ChapterNumber, i.Title, i.IsFinalized, i.IterationNumber, i.ResponseId ?? 0, i.GenerationDate.Add(i.GenerationTime), ""))
                    .ToList();
            }
            var iterByChapter = iterHeads.ToDictionary(x => x.ChapterNumber);

            List<(int ChapterNumber, string? Title, string? Status, DateTime CreatedAt, DateTime UpdatedAt, string Content)> libHeads;
            var chBase = _context.Chapters.AsNoTracking().Where(c => c.BookId == bookId);
            if (includeBodies)
            {
                var libRows = await chBase
                    .Select(c => new { c.ChapterNumber, c.Title, c.Status, c.CreatedAt, c.UpdatedAt, c.Content })
                    .ToListAsync();
                libHeads = libRows.Select(c => (c.ChapterNumber, c.Title, c.Status, c.CreatedAt, c.UpdatedAt, c.Content ?? "")).ToList();
            }
            else
            {
                var libRows = await chBase
                    .Select(c => new { c.ChapterNumber, c.Title, c.Status, c.CreatedAt, c.UpdatedAt })
                    .ToListAsync();
                libHeads = libRows.Select(c => (c.ChapterNumber, c.Title, c.Status, c.CreatedAt, c.UpdatedAt, "")).ToList();
            }

            bool HasBody(string? content) => !string.IsNullOrWhiteSpace(content);
            bool IsOfficialStatus(string? status) =>
                string.Equals(status, "ReadOnly", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Final", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Finalized", StringComparison.OrdinalIgnoreCase);

            var official = libHeads
                .Where(c => IsOfficialStatus(c.Status) && (!includeBodies || HasBody(c.Content)))
                .GroupBy(c => c.ChapterNumber)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First());

            var draftLibrary = libHeads
                .Where(c => (!includeBodies || HasBody(c.Content)) && !official.ContainsKey(c.ChapterNumber))
                .GroupBy(c => c.ChapterNumber)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First());

            var numbers = official.Keys
                .Union(draftLibrary.Keys)
                .Union(rawLatest.Select(r => r.ChapterNumber))
                .Union(iterByChapter.Keys)
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            var result = new List<ChapterDto>();
            var narrativeOrdinal = 0;
            foreach (var n in numbers)
            {
                if (n > 0) narrativeOrdinal++;

                if (official.TryGetValue(n, out var ch))
                {
                    result.Add(new ChapterDto
                    {
                        ResponseId = 0,
                        ChapterNumber = n,
                        Title = BookChapterExportHelper.GetDefaultStoredTitle(ch.Title, n, narrativeOrdinal),
                        Content = includeBodies ? ChapterContentNormalizer.NormalizeForManuscript(ch.Content) : string.Empty,
                        StatusCode = ch.Status,
                        CreatedAt = ch.UpdatedAt != default ? ch.UpdatedAt : ch.CreatedAt
                    });
                }
                else
                {
                    var r = rawLatest.FirstOrDefault(x => x.ChapterNumber == n);
                    if (r != null)
                    {
                        r.Title = BookChapterExportHelper.GetDefaultStoredTitle(r.Title, n, narrativeOrdinal);
                        if (includeBodies)
                            r.Content = ChapterContentNormalizer.NormalizeForManuscript(r.Content);
                        else
                            r.Content = string.Empty;
                        result.Add(r);
                        continue;
                    }
                    if (draftLibrary.TryGetValue(n, out var draftCh))
                    {
                        result.Add(new ChapterDto
                        {
                            ResponseId = 0,
                            ChapterNumber = n,
                            Title = BookChapterExportHelper.GetDefaultStoredTitle(draftCh.Title, n, narrativeOrdinal),
                            Content = includeBodies ? ChapterContentNormalizer.NormalizeForManuscript(draftCh.Content) : string.Empty,
                            StatusCode = draftCh.Status,
                            CreatedAt = draftCh.UpdatedAt != default ? draftCh.UpdatedAt : draftCh.CreatedAt
                        });
                        continue;
                    }
                    if (iterByChapter.TryGetValue(n, out var iter))
                    {
                        result.Add(new ChapterDto
                        {
                            ResponseId = iter.ResponseId,
                            ChapterNumber = n,
                            Title = BookChapterExportHelper.GetDefaultStoredTitle(iter.Title, n, narrativeOrdinal),
                            Content = includeBodies ? ChapterContentNormalizer.NormalizeForManuscript(iter.Content) : string.Empty,
                            StatusCode = iter.IsFinalized ? "Finalized" : "Draft",
                            CreatedAt = iter.CreatedAt
                        });
                    }
                }
            }

            if (result.Count == 0 && includeBodies)
            {
                var storedHtml = await _context.Books.AsNoTracking()
                    .Where(b => b.BookId == bookId && b.UserId == userId)
                    .Select(b => b.BookContentHtml)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(storedHtml))
                    result = BookContentHtmlParser.ToChapterDtos(storedHtml);
            }

            return result;
        }

        private async Task<string?> ResolveAuthorDisplayNameAsync(int userId)
        {
            var row = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => new { u.FullName, u.UserEmail })
                .FirstOrDefaultAsync();
            if (row == null) return null;
            if (!string.IsNullOrWhiteSpace(row.FullName)) return row.FullName.Trim();
            return string.IsNullOrWhiteSpace(row.UserEmail) ? null : row.UserEmail.Trim();
        }
        
        //=======================================
        // Get a single saved API response for a user and book
        //====================================
        public async Task<APIRawResponse?> GetSelectedBookResponseAsync(int userId, int bookId, int chapterNo)
        {
            // APIRawResponse table
            return await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId && r.Chapter == chapterNo)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();
        }
        //======================================================
        //  Finalize Chapter: Add record + Update APIRawResponse
        //======================================================
        public async Task<bool> SetRecordReadOnlyAsync(FinalizeChapters finalize)
        {
            try
            {
                if (finalize == null || finalize.ResponseId <= 0)
                    return false;

                // 1. Insert new record in FinalizeChapter table
                await _context.FinalizeChapters.AddAsync(finalize);

                // 2. Update StatusCode in APIRawResponse table
                var rawResponse = await _context.APIRawResponse
                    .FirstOrDefaultAsync(r =>
                      r.ResponseId == finalize.ResponseId &&
                      r.UserId == finalize.UserId &&
                      r.BookId == finalize.BookId &&
                      r.Chapter == finalize.Chapter
                    );
                if (rawResponse == null)
                {
                    return false; // no matching record found
                }

                rawResponse.StatusCode = "ReadOnly";
                rawResponse.UpdatedAt = DateTime.Now;

                // 3. Update Status to "Finalized" in Books table
                var book = await _context.Books
                    .FirstOrDefaultAsync(b => b.BookId == finalize.BookId && b.UserId == finalize.UserId);
                if (book != null)
                {
                    book.Status = "Finalized";
                    book.UpdatedAt = DateTime.UtcNow;
                }

                // Commit all operations (atomic)
                await _context.SaveChangesAsync();

                try
                {
                    await _chapterIterations.FinalizeByResponseIdAsync(
                        finalize.UserId, finalize.BookId, finalize.Chapter, finalize.ResponseId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Chapter iteration finalize (table may be missing): {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in SetRecordReadOnlyAsync: {ex.Message}");
                return false;
            }
        }

        // ====Chapter==================================== 100% OK
        // Load Selected Chapter of Book from Drop-Down 
        //   NOTE : Content field will preview
        // ==============================================
        public async Task<BookDetailsResponseDto?> GetBookDetailsAsync2(int userId, int bookId, int chapterNo, int? responseId = null)
        {
            try
            {
                var book = await _context.Books.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.UserId == userId && b.BookId == bookId);

                if (book == null)
                    return null;

                List<ChapterDto> chapters;
                if (responseId.HasValue && responseId.Value > 0)
                {
                    chapters = await LoadRawChapterRowsAsync(userId, bookId, chapterNo, responseId);
                }
                else
                {
                    var merged = await GetMergedPreviewChaptersAsync(userId, bookId, noTracking: true);
                    chapters = chapterNo > 0
                        ? merged.Where(c => c.ChapterNumber == chapterNo).ToList()
                        : merged;
                    if (chapters.Count == 0 && chapterNo > 0)
                        chapters = await LoadRawChapterRowsAsync(userId, bookId, chapterNo, responseId: null);
                }

                return new BookDetailsResponseDto
                {
                    Success = true,
                    BookId = book.BookId,
                    BookTitle = book.Title,
                    Description = book.Description,
                    Genre = book.Genre,
                    TotalChapters = chapters.Count,
                    Chapters = chapters
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [Service] Error loading book details: {ex.Message}");
                return new BookDetailsResponseDto
                {
                    Success = false,
                    Message = ex.Message
                };
            }
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
        public List<string> ExtractChapterNames(string json)
        {
            // Deserialize only the part we need
            dynamic obj = JsonConvert.DeserializeObject(json);

            string htmlList = obj.data.suggest_chapter_name;

            List<string> chapters = new List<string>();

            // Extract <li>...</li> content using Regex
            MatchCollection matches = Regex.Matches(htmlList, @"<li>(.*?)<\/li>");

            foreach (Match match in matches)
            {
                chapters.Add(match.Groups[1].Value.Trim());
            }

            return chapters;
        }

        public Task<bool> SetRecordReadOnlyAsync(int responseId)
        {
            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public async Task<(bool Success, string Message)> DeleteWriterChapterAsync(
            int userId,
            int bookId,
            int chapterNumber,
            CancellationToken cancellationToken = default)
        {
            if (userId <= 0 || bookId <= 0 || chapterNumber <= 0)
                return (false, "Invalid book or chapter number.");

            var book = await _context.Books
                .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
            if (book == null)
                return (false, "Book not found or access denied.");

            var chapterRow = await _context.Chapters
                .FirstOrDefaultAsync(c => c.BookId == bookId && c.ChapterNumber == chapterNumber, cancellationToken);

            var hasChapterData = chapterRow != null
                || await _context.APIRawResponse.AnyAsync(
                    r => r.UserId == userId && r.BookId == bookId && r.Chapter == chapterNumber,
                    cancellationToken)
                || await _context.ChapterIterations.AnyAsync(
                    i => i.UserId == userId && i.BookId == bookId && i.ChapterNumber == chapterNumber,
                    cancellationToken);
            if (!hasChapterData)
                return (false, "Chapter not found.");

            var finalizeRows = await _context.FinalizeChapters
                .Where(f => f.UserId == userId && f.BookId == bookId && f.Chapter == chapterNumber)
                .ToListAsync(cancellationToken);
            if (finalizeRows.Count > 0)
                _context.FinalizeChapters.RemoveRange(finalizeRows);

            var iterations = await _context.ChapterIterations
                .Where(i => i.UserId == userId && i.BookId == bookId && i.ChapterNumber == chapterNumber)
                .ToListAsync(cancellationToken);
            if (iterations.Count > 0)
                _context.ChapterIterations.RemoveRange(iterations);

            var rawResponses = await _context.APIRawResponse
                .Where(r => r.UserId == userId && r.BookId == bookId && r.Chapter == chapterNumber)
                .ToListAsync(cancellationToken);
            if (rawResponses.Count > 0)
                _context.APIRawResponse.RemoveRange(rawResponses);

            if (chapterRow != null)
                _context.Chapters.Remove(chapterRow);

            await _context.SaveChangesAsync(cancellationToken);
            return (true, $"Chapter {chapterNumber} deleted.");
        }

       
    }

}