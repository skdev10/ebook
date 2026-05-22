using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

/// <summary>
/// Unifies publish-ready book status: promotes Generated/Saved/Final books to Finalized when every chapter with content is finalized.
/// </summary>
public sealed class BookPublishReadinessService
{
    private static readonly string[] ListedBookStatuses =
    {
        "Published", "Paid"
    };

    private static readonly string[] PublishReadyBookStatuses =
    {
        "Finalized", "Published", "Paid", "Final"
    };

    private static readonly string[] PromotableBookStatuses =
    {
        "Generated", "Saved", "Final", "Edit", "Draft"
    };

    private static readonly string[] FinalChapterStatuses =
    {
        "Final", "Finalized", "ReadOnly", "Published"
    };

    private readonly ApplicationDbContext _context;
    private readonly IBookService _bookService;

    public BookPublishReadinessService(ApplicationDbContext context, IBookService bookService)
    {
        _context = context;
        _bookService = bookService;
    }

    public static bool IsListedBookStatus(string? status)
    {
        var s = (status ?? "").Trim();
        return ListedBookStatuses.Any(x => s.Equals(x, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPublishReadyBookStatus(string? status)
    {
        var s = (status ?? "").Trim();
        return PublishReadyBookStatuses.Any(x => s.Equals(x, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsFinalChapterStatus(string? status)
    {
        var s = (status ?? "").Trim();
        return FinalChapterStatuses.Any(x => s.Equals(x, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns true if the book row was promoted to Finalized.</summary>
    public async Task<bool> TryPromoteBookToFinalizedAsync(int userId, int bookId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0 || bookId <= 0) return false;

        var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (book == null) return false;

        var status = (book.Status ?? "").Trim();
        if (IsListedBookStatus(status)) return false;
        if (!PromotableBookStatuses.Any(s => status.Equals(s, StringComparison.OrdinalIgnoreCase)))
            return false;

        var details = await _bookService.GetBookDetailsForPreviewAsync(userId, bookId);
        if (details == null || !details.Success) return false;

        var withContent = details.Chapters?
            .Where(c => !string.IsNullOrWhiteSpace(c.Content))
            .ToList() ?? new List<ChapterDto>();

        if (withContent.Count == 0) return false;

        if (!withContent.All(c => IsFinalChapterStatus(c.StatusCode)))
            return false;

        book.Status = "Finalized";
        book.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
