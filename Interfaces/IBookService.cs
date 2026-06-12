using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.ViewModels;
using EBookDashboard.Services;

namespace EBookDashboard.Interfaces
{
    public interface IBookService
    {
        // Books
        Task<IEnumerable<Books>> GetAllBooksAsync();
        Task<Books?> GetBookByIdAsync(int bookId);
        Task<Books> CreateBookAsync(Books book);
        Task<Books> CreateBookFromRequestAsync(CreateBookRequest request); // Updated parameter type
        Task<bool> UpdateBookAsync(Books book);
        Task<bool> UpdateBookCoverImagePathAsync(int bookId, int userId, string coverImagePath);
        Task<bool> DeleteBookAsync(int bookId);
        /// <summary>Hard-delete a book for the owning user — DB row, settings keys, and upload files.</summary>
        Task<bool> DeleteBookForUserAsync(int bookId, int userId);
        /// <summary>Mark book as Published after final download/export.</summary>
        Task<bool> MarkPublishedAsync(int bookId, int userId, CancellationToken cancellationToken = default);

        // Categories
        Task<IEnumerable<Categories>> GetAllCategoriesAsync();

        // Book Prices
        Task<BookPrice?> GetPriceByBookIdAsync(int bookId);
        Task<BookPrice> SetBookPriceAsync(BookPrice price);
        Task<bool> UpdateBookPriceAsync(BookPrice price);

        // Book Versions
        Task<IEnumerable<BookVersion>> GetVersionsByBookIdAsync(int bookId);
        Task<BookVersion> AddBookVersionAsync(BookVersion version);
        Task<BookVersion?> GetVersionByIdAsync(int versionId);
        
        Task<BookDetailsDto?> GetBookDetailsFromRawDataAsync(int userId, int bookId);
        // -------- - User Books with Chapters -----
        Task<UserBooksViewModel> GetUserBooksWithChaptersAsync(int userId);
        Task<UserBook?> GetUserBookDetailsAsync(int userId, int bookId);
        Task<List<UserBook>> GetUserBooksSummaryAsync(int userId);

        // NEW: API Raw Response methods
        Task<APIRawResponse?> GetLatestBookResponseAsync(int userId, int bookId);

        //int CalculateWordCount(string content);
        //string GetContentPreview(string? content, int maxLength = 150);
        Task<IEnumerable<SavedBookDto>> GetSavedBooksForDropdownAsync(int userId, int bookId);
        Task<IEnumerable<ChapterDto>> GetSavedBooksChaptersAsync(int userId, int bookId);
        Task<int?> GetLatestBookIdAsync(int userId);
        Task<SavedBookDto?> GetLatestBookAsync(int userId);
        Task<(bool success, string data, string message)> GetBookApiResponseAsync(int userId, int bookId);
        Task<APIRawResponse?> GetSelectedBookResponseAsync(int userId, int bookId, int chapterNo);
        // Chapter Number Generation
        Task<int> GetNextChapterNumberAsync(int userId, int bookId);
        Task<BookDetailsResponseDto?> GetBookDetailsAsync(int userId, int bookId);
        /// <summary>Lightweight load for formatter/preview: no isActive updates, faster.</summary>
        Task<BookDetailsResponseDto?> GetBookDetailsForPreviewAsync(int userId, int bookId);

        /// <summary>Word count and description from DB + live chapter manuscript.</summary>
        Task<BookManuscriptStats.ManuscriptSummary> GetManuscriptSummaryAsync(int userId, int bookId);

        /// <summary>Persist computed word count / description onto Books when DB fields are empty.</summary>
        Task SyncBookMetadataFromManuscriptAsync(int userId, int bookId);
        Task<BookDetailsResponseDto?> GetBookDetailsAsync2(int userId, int bookId, int chapterNo, int? responseId = null);
        Task<bool> SetRecordReadOnlyAsync(FinalizeChapters finalize);
        Task<int> GetLastChapterAsync(int userId, int bookId);
    }
}
