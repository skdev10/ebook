using EBookDashboard.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EBookDashboard.Services.PdfExport;

/// <summary>Selects the configured HTML→PDF engine implementation.</summary>
public sealed class PdfHtmlExportServiceResolver
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;

    public PdfHtmlExportServiceResolver(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Returns the primary engine from <c>PdfExport:Engine</c>.</summary>
    public IPdfHtmlExportService ResolvePrimary()
    {
        var engine = PdfExportEngine.Resolve(_configuration);
        if (engine == PdfExportEngine.PdfSharp)
            return CreatePdfSharp();

        if (engine == PdfExportEngine.DinkToPdf)
        {
            var log = _loggerFactory.CreateLogger<PdfHtmlExportServiceResolver>();
            log.LogWarning("DinkToPdf is not wired; using Chromium.");
        }

        return CreateChromium();
    }

    /// <summary>Chromium implementation for CSS-accurate export.</summary>
    public IPdfHtmlExportService CreateChromium() =>
        new ChromiumHtmlPdfExportService(
            _loggerFactory.CreateLogger<ChromiumHtmlPdfExportService>(),
            _configuration);

    /// <summary>Structured PdfSharp path (no HTML) — used only as orchestration fallback.</summary>
    public IPdfHtmlExportService CreatePdfSharp() =>
        new PdfSharpHtmlPdfExportService(
            _loggerFactory.CreateLogger<PdfSharpHtmlPdfExportService>());
}

/// <summary>Placeholder for HTML engine — BookPdfService uses structured PdfSharpBookExporter directly as fallback.</summary>
public sealed class PdfSharpHtmlPdfExportService : IPdfHtmlExportService
{
    private readonly ILogger<PdfSharpHtmlPdfExportService> _logger;

    public PdfSharpHtmlPdfExportService(ILogger<PdfSharpHtmlPdfExportService> logger)
    {
        _logger = logger;
    }

    public string EngineName => PdfExportEngine.PdfSharp;

    public Task<byte[]> ExportHtmlAsync(PdfHtmlExportRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("PdfSharp HTML path not used for book {BookId}; caller should use structured fallback.", request.BookId);
        throw new NotSupportedException("PdfSharp export uses structured manuscript data, not raw HTML. Use BookPdfService fallback.");
    }
}
