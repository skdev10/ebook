using EBookDashboard.Models.DTO;
using EBookDashboard.Services;

namespace EBookDashboard.Interfaces;

/// <summary>HTML → PDF engine abstraction (Chromium, PdfSharp fallback, future DinkToPdf).</summary>
public interface IPdfHtmlExportService
{
    /// <summary>Engine identifier from configuration (e.g. Chromium, PdfSharp).</summary>
    string EngineName { get; }

    /// <summary>Renders styled interior HTML to print-ready PDF bytes.</summary>
    Task<byte[]> ExportHtmlAsync(
        PdfHtmlExportRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Input for <see cref="IPdfHtmlExportService"/>.</summary>
public sealed class PdfHtmlExportRequest
{
    public required string Html { get; init; }
    public required BookPdfPlatformLayout.PdfLayoutSpec Layout { get; init; }
    public string? HeaderTemplate { get; init; }
    public string? FooterTemplate { get; init; }
    public int BookId { get; init; }
}
