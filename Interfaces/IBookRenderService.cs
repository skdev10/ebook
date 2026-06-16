using EBookDashboard.Models.DTO;
using EBookDashboard.Services;

namespace EBookDashboard.Interfaces;

/// <summary>
/// Single source of truth for canonical book HTML — shared by formatter preview iframe and PDF export.
/// </summary>
public interface IBookRenderService
{
    /// <summary>
    /// Builds the full print HTML document (same input as Chromium PDF export).
    /// </summary>
    Task<BookRenderResult> BuildBookHtmlAsync(BookRenderRequest request, CancellationToken cancellationToken = default);
}

public sealed class BookRenderRequest
{
    public required BookDetailsResponseDto Details { get; init; }
    public required BookPdfExportOptions ExportOptions { get; init; }
    public string? CoverImageDataUrl { get; init; }
    public string? DisplayTitle { get; init; }
    public string? DisplayAuthor { get; init; }
    public string? DisplayGenre { get; init; }
    public string? PublisherDisplayName { get; init; }
}

public sealed class BookRenderResult
{
    public required string Html { get; init; }
    public required BookPdfPlatformLayout.PdfLayoutSpec Layout { get; init; }
    public required BookFormattingSettings Settings { get; init; }
}
