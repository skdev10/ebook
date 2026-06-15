using EBookDashboard.Models;

namespace EBookDashboard.Interfaces;

/// <summary>Generates whole-book HTML via upstream AI and persists to <c>books.BookContentHtml</c>.</summary>
public interface IBookGeneratorService
{
    /// <summary>Calls upstream AI with the filled prompt, sanitizes HTML, saves to the book row.</summary>
    Task<BookGenerationResult> GenerateAndSaveHtmlAsync(
        int userId,
        int bookId,
        BookRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class BookGenerationResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public string? Html { get; init; }
    public int ChapterCount { get; init; }
}
