using EBookDashboard.Models.DTO;
using EBookDashboard.Services.PdfExport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EBookDashboard.Services;

/// <summary>
/// Pre-downloads Chromium / launches browser once at startup so the first user PDF export does not cold-start fail.
/// </summary>
public sealed class ChromiumPdfWarmupHostedService : IHostedService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChromiumPdfWarmupHostedService> _logger;

    public ChromiumPdfWarmupHostedService(IConfiguration configuration, ILogger<ChromiumPdfWarmupHostedService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (PdfExportEngine.Resolve(_configuration) == PdfExportEngine.PdfSharp)
        {
            _logger.LogInformation("PDF engine is PdfSharp-only; skipping Chromium warm-up.");
            return Task.CompletedTask;
        }

        return WarmUpAsync(cancellationToken);
    }

    private async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            var exporter = new ChromiumPdfExporter(_logger, _configuration);
            var layout = BookPdfPlatformLayout.Resolve(new BookPdfExportOptions(), BookPdfLayoutOptions.FromConfiguration(_configuration));
            var html = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>@page{size:6in 9in;margin:0;}body{margin:0;padding:1in;font-family:Georgia,serif;}</style></head><body><p>Warmup</p></body></html>";
            var bytes = await exporter.ExportAsync(html, layout, "<span></span>", "<span></span>", cancellationToken);
            _logger.LogInformation("Chromium PDF warm-up complete ({Bytes} bytes).", bytes.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chromium PDF warm-up failed — first export may be slower; PdfSharp fallback remains available.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
