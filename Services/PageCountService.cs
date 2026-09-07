using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services;

public sealed class PageCountService : IPageCountService
{
    private const int MaxBillablePages = 828;
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
            .Where(b => b.BookId == bookId)
            .Select(b => new { b.WordCount, b.BookContentHtml })
            .FirstOrDefaultAsync();
        if (book == null)
            throw new InvalidOperationException($"Book {bookId} was not found.");

        var chapterRows = await _context.Chapters.AsNoTracking()
            .Where(c => c.BookId == bookId)
            .Select(c => new ContentSlice(c.ChapterNumber, c.Content, c.UpdatedAt))
            .ToListAsync();

        var rawRows = await _context.APIRawResponse.AsNoTracking()
            .Where(c => c.BookId == bookId)
            .Select(c => new ContentSlice(c.Chapter, c.Content, c.CreatedAt))
            .ToListAsync();

        var iterRows = await _context.ChapterIterations.AsNoTracking()
            .Where(i => i.BookId == bookId)
            .Select(i => new ContentSlice(i.ChapterNumber, i.Content, i.GenerationDate))
            .ToListAsync();

        var fromManuscript = SumRichestChapterWords(chapterRows, rawRows, iterRows);
        var fromBookHtml = BookManuscriptStats.CountWords(book.BookContentHtml);
        return Math.Max(book.WordCount, Math.Max(fromManuscript, fromBookHtml));
    }

    /// <inheritdoc />
    public async Task<int> GetBillablePageCountAsync(int bookId)
    {
        var layoutPages = await GetSavedLayoutPageCountAsync(bookId);
        if (layoutPages > 0)
            return layoutPages;

        var words = await GetWordCountAsync(bookId);
        var perPage = _options.WordsPerPage;
        if (perPage <= 0)
            throw new InvalidOperationException("Pricing:WordsPerPage must be a positive integer in configuration.");
        if (words <= 0)
            return 0;
        return Math.Min(MaxBillablePages, (int)Math.Ceiling(words / (double)perPage));
    }

    private async Task<int> GetSavedLayoutPageCountAsync(int bookId)
    {
        var key = $"book:{bookId}:printReadyPageCount";
        var raw = await _context.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        if (!int.TryParse(raw, out var pages) || pages <= 0)
            return 0;
        return Math.Clamp(pages, 1, MaxBillablePages);
    }

    private static int SumRichestChapterWords(params IReadOnlyList<ContentSlice>[] sources)
    {
        var richest = new Dictionary<int, int>();
        foreach (var source in sources)
        {
            var latest = new Dictionary<int, (DateTime Stamp, string Content)>();
            foreach (var row in source)
            {
                if (!latest.TryGetValue(row.Chapter, out var prev) || row.Stamp >= prev.Stamp)
                    latest[row.Chapter] = (row.Stamp, row.Content ?? "");
            }

            foreach (var pair in latest)
            {
                var words = BookManuscriptStats.CountWords(pair.Value.Content);
                if (!richest.TryGetValue(pair.Key, out var prevWords) || words > prevWords)
                    richest[pair.Key] = words;
            }
        }

        return richest.Values.Sum();
    }

    private readonly record struct ContentSlice(int Chapter, string? Content, DateTime Stamp);
}
