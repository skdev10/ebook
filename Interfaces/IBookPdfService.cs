using EBookDashboard.Models.DTO;

namespace EBookDashboard.Interfaces;

public interface IBookPdfService
{
    /// <summary>
    /// Builds print HTML and renders PDF (headless Chromium). Uses formatted chapter bodies consistent with dashboard preview.
    /// </summary>
    Task<byte[]> RenderFullBookPdfAsync(
        BookDetailsResponseDto details,
        string? coverImageDataUrl,
        string? displayTitle,
        string? displayAuthor,
        string? displayGenre,
        BookPdfExportOptions exportOptions,
        string? publisherDisplayName,
        CancellationToken cancellationToken = default);
}
