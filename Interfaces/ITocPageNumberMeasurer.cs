using EBookDashboard.Models.DTO;
using EBookDashboard.Services;

namespace EBookDashboard.Interfaces;

/// <summary>
/// Measures 1-based PDF start pages for each chapter using the same Chromium print pipeline as export.
/// </summary>
public interface ITocPageNumberMeasurer
{
    /// <summary>
    /// Returns one start page per <c>section.chapter#ch-N</c> in document order.
    /// </summary>
    Task<IReadOnlyList<int>> MeasureChapterStartPagesAsync(
        string fullBookHtml,
        BookPdfExportOptions exportOptions,
        BookPdfPlatformLayout.PdfLayoutSpec layout,
        string bookTitle,
        IReadOnlyList<string> chapterTitles,
        int expectedChapterCount,
        CancellationToken cancellationToken = default);
}
