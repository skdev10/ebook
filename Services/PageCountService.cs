using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services;

public sealed class PageCountService : IPageCountService
{
    private readonly ApplicationDbContext _context;
    private readonly PricingOptions _options;

    public PageCountService(ApplicationDbContext context, IOptions<PricingOptions> options)
    {
        _context = context;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<int> GetWordCountAsync(int bookId)
    {
        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookId == bookId);
        if (book == null)
            throw new InvalidOperationException($"Book {bookId} was not found.");

        if (book.WordCount > 0)
            return book.WordCount;

        var chapterWords = await _context.Chapters.AsNoTracking()
            .Where(c => c.BookId == bookId)
            .Select(c => c.Content)
            .ToListAsync();
        var computed = chapterWords.Sum(BookManuscriptStats.CountWords);
        return computed > 0 ? computed : 0;
    }

    /// <inheritdoc />
    public async Task<int> GetBillablePageCountAsync(int bookId)
    {
        var words = await GetWordCountAsync(bookId);
        var perPage = _options.WordsPerPage;
        if (perPage <= 0)
            throw new InvalidOperationException("Pricing:WordsPerPage must be a positive integer in configuration.");
        if (words <= 0)
            return 0;
        return (int)Math.Ceiling(words / (double)perPage);
    }
}
