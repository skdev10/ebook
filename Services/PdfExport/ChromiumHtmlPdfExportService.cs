using EBookDashboard.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EBookDashboard.Services.PdfExport;

/// <summary>Primary HTML→PDF via headless Chromium (BookPreview CSS 1:1).</summary>
public sealed class ChromiumHtmlPdfExportService : IPdfHtmlExportService
{
    private readonly ILogger<ChromiumHtmlPdfExportService> _logger;
    private readonly IConfiguration _configuration;

    public ChromiumHtmlPdfExportService(ILogger<ChromiumHtmlPdfExportService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public string EngineName => PdfExportEngine.Chromium;

    public async Task<byte[]> ExportHtmlAsync(PdfHtmlExportRequest request, CancellationToken cancellationToken = default)
    {
        var exporter = new ChromiumPdfExporter(_logger, _configuration);
        var bytes = await exporter.ExportAsync(
            request.Html,
            request.Layout,
            request.HeaderTemplate ?? string.Empty,
            request.FooterTemplate ?? string.Empty,
            cancellationToken);

        _logger.LogInformation(
            "Chromium HTML PDF: {Bytes} bytes, book={BookId}, 6x9={W}x{H}",
            bytes.Length, request.BookId, request.Layout.PdfWidth, request.Layout.PdfHeight);

        return bytes;
    }
}
