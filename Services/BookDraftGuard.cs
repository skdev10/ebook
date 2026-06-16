using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

/// <summary>
/// Prevents duplicate near-empty Untitled drafts — reuse one placeholder until the user adds real content.
/// </summary>
public static class BookDraftGuard
{
    private const int MinChapterContentChars = 15;
    private const int MinApiResponseChars = 30;

    public static bool IsPlaceholderTitle(string? title) => BookTitleResolver.IsPlaceholderTitle(title);

    /// <summary>True when the book has no meaningful manuscript (placeholder title + no chapter/API body).</summary>
    public static async Task<bool> IsNearEmptyBookAsync(
        ApplicationDbContext context,
        int userId,
        int bookId,
        CancellationToken cancellationToken = default)
    {
        var book = await context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (book == null) return false;
        if (!IsPlaceholderTitle(book.Title)) return false;
        if (book.WordCount > 0) return false;
        if (!string.IsNullOrWhiteSpace(book.Description)) return false;
        if (!string.IsNullOrWhiteSpace(book.BookContentHtml) && book.BookContentHtml.Trim().Length > 20)
            return false;

        var hasChapterContent = await context.Chapters.AsNoTracking()
            .AnyAsync(c => c.BookId == bookId
                           && c.Content != null
                           && c.Content.Trim().Length > MinChapterContentChars,
                cancellationToken);
        if (hasChapterContent) return false;

        var hasApiContent = await context.APIRawResponse.AsNoTracking()
            .AnyAsync(r => r.UserId == userId
                           && r.BookId == bookId
                           && ((r.ResponseData != null && r.ResponseData.Trim().Length > MinApiResponseChars)
                               || (r.Content != null && r.Content.Trim().Length > MinApiResponseChars)),
                cancellationToken);

        return !hasApiContent;
    }

    /// <summary>Returns the user's newest near-empty Untitled draft, if any.</summary>
    public static async Task<Books?> FindReusableEmptyUntitledAsync(
        ApplicationDbContext context,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var candidates = await BookFlowStateService.WhereNotPublished(context.Books)
            .Where(b => b.UserId == userId)
            .Where(b => b.Title == null || b.Title == ""
                        || b.Title == "Untitled" || b.Title == "Untitled Book")
            .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var book in candidates)
        {
            if (await IsNearEmptyBookAsync(context, userId, book.BookId, cancellationToken))
                return book;
        }

        return null;
    }

    /// <summary>
    /// Latest draft with real content — skips near-empty Untitled placeholders so delete/resume does not resurrect ghosts.
    /// </summary>
    public static async Task<Books?> FindLatestMeaningfulDraftAsync(
        ApplicationDbContext context,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var candidates = await BookFlowStateService.WhereNotPublished(context.Books.AsNoTracking())
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.isActive)
            .ThenByDescending(b => b.UpdatedAt ?? b.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var book in candidates)
        {
            if (await IsNearEmptyBookAsync(context, userId, book.BookId, cancellationToken))
                continue;
            return book;
        }

        return null;
    }

    /// <summary>Removes every near-empty Untitled placeholder for the user (e.g. after deleting one ghost draft).</summary>
    public static async Task<int> PurgeAllNearEmptyUntitledAsync(
        ApplicationDbContext context,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var candidates = await BookFlowStateService.WhereNotPublished(context.Books)
            .Where(b => b.UserId == userId)
            .Where(b => b.Title == null || b.Title == ""
                        || b.Title == "Untitled" || b.Title == "Untitled Book")
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var book in candidates)
        {
            if (!await IsNearEmptyBookAsync(context, userId, book.BookId, cancellationToken))
                continue;

            context.Books.Remove(book);
            removed++;
        }

        if (removed > 0)
            await context.SaveChangesAsync(cancellationToken);

        return removed;
    }
}
