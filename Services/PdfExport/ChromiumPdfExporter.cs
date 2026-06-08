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
    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static bool _fetched;

    private static readonly string[] LinuxBrowserCandidates =
    [
        "/usr/bin/google-chrome-stable",
        "/usr/bin/google-chrome",
        "/usr/bin/chromium-browser",
        "/usr/bin/chromium",
        "/snap/bin/chromium"
    ];

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
        var executablePath = await ResolveBrowserExecutableAsync(cancellationToken);
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
            WaitUntil = [WaitUntilNavigation.Networkidle0, WaitUntilNavigation.DOMContentLoaded],
            Timeout = 120_000
        });

        await page.EvaluateFunctionAsync(@"async () => {
            if (document.fonts && document.fonts.ready) await document.fonts.ready;
            await new Promise(r => setTimeout(r, 1500));
        }");
        await Task.Delay(500, cancellationToken);

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
        var o = new PdfOptions
        {
            PrintBackground = true,
            PreferCSSPageSize = true,
            DisplayHeaderFooter = true,
            HeaderTemplate = headerTemplate,
            FooterTemplate = footerTemplate,
            MarginOptions = new MarginOptions
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

    private async Task<string?> ResolveBrowserExecutableAsync(CancellationToken cancellationToken)
    {
        var configured = _configuration["Puppeteer:ExecutablePath"]
            ?? Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        if (OperatingSystem.IsLinux())
        {
            foreach (var candidate in LinuxBrowserCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        foreach (var win in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
                 })
        {
            if (File.Exists(win)) return win;
        }

        await EnsureChromiumAsync(cancellationToken);
        return null;
    }

    private static async Task EnsureChromiumAsync(CancellationToken cancellationToken)
    {
        if (_fetched) return;
        await FetchLock.WaitAsync(cancellationToken);
        try
        {
            if (_fetched) return;
            var bf = new BrowserFetcher();
            await bf.DownloadAsync();
            _fetched = true;
        }
        finally
        {
            FetchLock.Release();
        }
    }
}
