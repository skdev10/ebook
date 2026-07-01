using EBookDashboard.Models.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// CSS-aware HTML → PDF via headless Chromium (BookPreview HTML in, styled PDF out).
/// </summary>
public sealed class ChromiumPdfExporter
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;

    public ChromiumPdfExporter(ILogger logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>Renders HTML string to PDF bytes (6×9 or A4 per layout).</summary>
    public async Task<byte[]> ExportAsync(
        string html,
        BookPdfPlatformLayout.PdfLayoutSpec layout,
        string headerTemplate,
        string footerTemplate,
        CancellationToken cancellationToken = default)
    {
        var executablePath = await ChromiumLaunchHelper.ResolveExecutableAsync(_configuration, cancellationToken);
        var launchOptions = new LaunchOptions
        {
            Headless = true,
            ExecutablePath = executablePath,
            Args =
            [
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--font-render-hinting=none",
                "--disable-gpu",
                "--disable-web-security"
            ]
        };

        await using var browser = await Puppeteer.LaunchAsync(launchOptions);
        await using var page = await browser.NewPageAsync();
        await page.EmulateMediaTypeAsync(MediaType.Print);
        await page.SetContentAsync(html, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = 120_000
        });

        await page.EvaluateFunctionAsync(@"async () => {
            if (document.fonts && document.fonts.ready) await document.fonts.ready;
            await new Promise(r => setTimeout(r, 900));
        }");
        await Task.Delay(250, cancellationToken);

        var fontStatus = await page.EvaluateFunctionAsync<string>(@"() => {
            if (!document.fonts || !document.fonts.forEach) return 'no-fonts-api';
            var failed = [];
            document.fonts.forEach(f => { if (f.status === 'error') failed.push(f.family); });
            return failed.length ? 'failed:' + failed.join(',') : 'ok';
        }");
        _logger.LogInformation("Chromium PDF font status: {Status}", fontStatus ?? "unknown");

        var pdfBytes = await page.PdfDataAsync(BuildPdfOptions(layout, headerTemplate, footerTemplate));
        return pdfBytes;
    }

    private static PdfOptions BuildPdfOptions(
        BookPdfPlatformLayout.PdfLayoutSpec layout,
        string headerTemplate,
        string footerTemplate)
    {
        var useChromeHeaderFooter = !string.IsNullOrWhiteSpace(headerTemplate)
            || !string.IsNullOrWhiteSpace(footerTemplate);

        var o = new PdfOptions
        {
            PrintBackground = true,
            // POD bleed styles set PreferCssPageSize=false so the explicit bleed Width/Height below
            // is authoritative; all other styles keep CSS @page size.
            PreferCSSPageSize = layout.PreferCssPageSize,
            DisplayHeaderFooter = useChromeHeaderFooter,
            HeaderTemplate = headerTemplate ?? string.Empty,
            FooterTemplate = footerTemplate ?? string.Empty,
            // When running headers/folios are active, Chromium draws them inside the top/bottom
            // page margins, so we reserve vertical space here. Left/Right are 0 because the horizontal
            // text inset comes entirely from the per-style .book-preview-sheet padding (adding page
            // margins here too would double the side margins). These values match the interior
            // `@page { margin: 18mm 0 16mm 0 }` rule so the result is identical regardless of which
            // one Chromium treats as authoritative. The interior CSS trims the sheet's top/bottom
            // padding by a matching amount so the text block keeps its intended position.
            MarginOptions = useChromeHeaderFooter
                ? new MarginOptions { Top = "18mm", Bottom = "16mm", Left = "0", Right = "0" }
                : new MarginOptions
                {
                    Top = layout.MarginTop,
                    Bottom = layout.MarginBottom,
                    Left = layout.MarginLeft,
                    Right = layout.MarginRight
                }
        };

        if (layout.UseBuiltInFormat)
            o.Format = layout.BuiltInFormat;
        else
        {
            o.Width = layout.PdfWidth ?? "6in";
            o.Height = layout.PdfHeight ?? "9in";
        }

        return o;
    }
}
