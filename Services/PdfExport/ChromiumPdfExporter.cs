using System.Text.RegularExpressions;
using EBookDashboard.Models.DTO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using EBookDashboard.Services;
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
        // SetContentAsync has origin about:blank — file:// and /uploads img src will not load.
        // Inline local images as data URIs so figures survive Chromium print.
        // A missing file throws here, before any PDF bytes are produced.
        html = InlineLocalImageSources(html);

        byte[] pdf;
        if (PrintHtmlChunker.ShouldChunk(html))
            pdf = await ExportChunkedAsync(html, layout, headerTemplate, footerTemplate, cancellationToken);
        else
        {
            var executablePath = await ChromiumLaunchHelper.ResolveExecutableAsync(_configuration, cancellationToken);
            await using var browser = await Puppeteer.LaunchAsync(BuildLaunchOptions(executablePath));
            pdf = await RenderHtmlAsync(browser, html, layout, headerTemplate, footerTemplate, cancellationToken);
        }

        var missingImages = PrintImageAudit.MissingFromPdf(html, pdf);
        if (missingImages.Count > 0)
            throw new PdfImageLoadException(missingImages);
        return pdf;
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
            // CSS @page is the only margin box (see InteriorSpacingTheme). Puppeteer margins stay 0
            // unless a Chromium header/footer is on, in which case they repeat that same box so
            // whichever one Chromium honors is the trade inset and not a second one.
            MarginOptions = useChromeHeaderFooter
                ? new MarginOptions
                {
                    Top = InteriorSpacingTheme.In(InteriorSpacingTheme.PrintTopIn),
                    Bottom = InteriorSpacingTheme.In(InteriorSpacingTheme.PrintBottomIn),
                    Left = InteriorSpacingTheme.In(InteriorSpacingTheme.PrintInsideIn),
                    Right = InteriorSpacingTheme.In(InteriorSpacingTheme.PrintOutsideIn)
                }
                : new MarginOptions { Top = "0", Bottom = "0", Left = "0", Right = "0" }
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

    private static LaunchOptions BuildLaunchOptions(string? executablePath) => new()
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
            "--disable-web-security",
            "--allow-file-access-from-files"
        ]
    };

    /// <summary>
    /// One browser, one print per chapter group, then a single merged PDF.
    /// Page numbers are stamped later on the merged file so numbering stays continuous.
    /// </summary>
    private async Task<byte[]> ExportChunkedAsync(
        string html,
        BookPdfPlatformLayout.PdfLayoutSpec layout,
        string headerTemplate,
        string footerTemplate,
        CancellationToken cancellationToken)
    {
        var chunks = PrintHtmlChunker.Split(html);
        BookPrintProgress.Report(5, $"Rendering {chunks.Count} chunks");
        var executablePath = await ChromiumLaunchHelper.ResolveExecutableAsync(_configuration, cancellationToken);
        await using var browser = await Puppeteer.LaunchAsync(BuildLaunchOptions(executablePath));
        var parts = new List<byte[]>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var percent = 10 + (int)Math.Round(70.0 * i / chunks.Count);
            BookPrintProgress.Report(percent, $"Rendering chunk {i + 1} of {chunks.Count}");
            _logger.LogInformation("Chromium chunk {Index}/{Count} for print PDF", i + 1, chunks.Count);
            parts.Add(await RenderHtmlAsync(browser, chunks[i], layout, headerTemplate, footerTemplate, cancellationToken));
        }

        BookPrintProgress.Report(85, "Merging chunks");
        return PdfDocumentMerger.MergeChapterChunks(parts);
    }

    private async Task<byte[]> RenderHtmlAsync(
        IBrowser browser,
        string html,
        BookPdfPlatformLayout.PdfLayoutSpec layout,
        string headerTemplate,
        string footerTemplate,
        CancellationToken cancellationToken)
    {
        await using var page = await browser.NewPageAsync();
        page.DefaultTimeout = 900_000;
        page.DefaultNavigationTimeout = 900_000;
        await page.EmulateMediaTypeAsync(MediaType.Print);
        await page.SetContentAsync(html, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = 900_000
        });

        await page.EvaluateFunctionAsync(@"async () => {
            if (document.fonts && document.fonts.ready) await document.fonts.ready;
        }");

        var missing = await page.EvaluateFunctionAsync<string[]>(@"async () => {
            const imgs = Array.from(document.images || []);
            await Promise.all(imgs.map(img => {
                if (img.complete) return Promise.resolve();
                return new Promise(resolve => {
                    img.addEventListener('load', resolve, { once: true });
                    img.addEventListener('error', resolve, { once: true });
                });
            }));
            const missing = [];
            imgs.forEach((img, index) => {
                const src = img.getAttribute('src') || '';
                const fileName = img.getAttribute('data-filename')
                    || img.getAttribute('alt')
                    || (src.startsWith('data:') ? ('inline-image-' + (index + 1)) : decodeURIComponent(src.split('/').pop() || src).split('?')[0]);
                const svg = /image\/svg|\.svg(\?|$)/i.test(src);
                const width = img.naturalWidth || 0;
                const drawn = svg ? (img.getBoundingClientRect().width || 0) : 0;
                if (!img.complete || (!svg && !(width > 0)) || (svg && !(width > 0) && !(drawn > 0)))
                    missing.push(fileName || src || ('image-' + (index + 1)));
            });
            return missing;
        }");
        if (missing is { Length: > 0 })
            throw new PdfImageLoadException(missing);

        await page.EvaluateFunctionAsync(DeferOverflowFiguresScript);
        await Task.Delay(200, cancellationToken);

        var fontStatus = await page.EvaluateFunctionAsync<string>(@"() => {
            if (!document.fonts || !document.fonts.forEach) return 'no-fonts-api';
            var failed = [];
            document.fonts.forEach(f => { if (f.status === 'error') failed.push(f.family); });
            return failed.length ? 'failed:' + failed.join(',') : 'ok';
        }");
        _logger.LogInformation("Chromium PDF font status: {Status}", fontStatus ?? "unknown");

        return await page.PdfDataAsync(BuildPdfOptions(layout, headerTemplate, footerTemplate));
    }

    /// <summary>Rewrite file:// and absolute local paths in img src to data URIs for about:blank SetContent.</summary>
    public static string InlineLocalImageSources(string html)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf("src=", StringComparison.OrdinalIgnoreCase) < 0)
            return html;

        var missing = new List<string>();
        var inlined = Regex.Replace(
            html,
            @"src=""(file:///[^""]+|/[^""]+)""",
            m =>
            {
                try
                {
                    var raw = m.Groups[1].Value;
                    var local = raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                        || raw.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)
                        || raw.StartsWith("/fonts/", StringComparison.OrdinalIgnoreCase);
                    if (!local)
                        return m.Value;

                    string path;
                    if (raw.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    {
                        path = Uri.UnescapeDataString(new Uri(raw).LocalPath);
                    }
                    else
                    {
                        var candidates = new[]
                        {
                            Path.Combine(Environment.CurrentDirectory, "wwwroot", raw.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)),
                            Path.Combine(AppContext.BaseDirectory, "wwwroot", raw.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)),
                            Path.Combine(Environment.CurrentDirectory, raw.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))
                        };
                        path = candidates.FirstOrDefault(File.Exists) ?? "";
                    }

                    var fileName = string.IsNullOrEmpty(path) ? Path.GetFileName(raw) : Path.GetFileName(path);
                    if (string.IsNullOrEmpty(fileName))
                        fileName = raw;
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        missing.Add(fileName);
                        return m.Value;
                    }

                    var bytes = File.ReadAllBytes(path);
                    if (bytes.Length < 400 || bytes.Length > 12_000_000)
                    {
                        missing.Add(fileName);
                        return m.Value;
                    }
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    var mime = ext switch
                    {
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        ".svg" => "image/svg+xml",
                        _ => "image/jpeg"
                    };
                    return "src=\"data:" + mime + ";base64," + Convert.ToBase64String(bytes) + "\"";
                }
                catch
                {
                    missing.Add(Path.GetFileName(m.Groups[1].Value));
                    return m.Value;
                }
            },
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (missing.Count > 0)
            throw new PdfImageLoadException(missing);
        return inlined;
    }

    /// <summary>
    /// A figure that does not fit the remaining text block moves down, and the paragraphs
    /// after it move up into that space. A caption stays inside the figure. A captioned
    /// figure is left in place so its words keep their order.
    /// </summary>
    private const string DeferOverflowFiguresScript = """
        () => {
            const raw = getComputedStyle(document.documentElement).getPropertyValue('--text-block-h').trim();
            const pageH = parseFloat(raw);
            if (!pageH || pageH < 100) return;
            document.querySelectorAll('.reader-page-body').forEach(body => {
                const chapter = body.closest('section.chapter') || body.parentElement;
                const title = chapter ? chapter.querySelector('.reader-page-title') : null;
                let used = title ? (title.getBoundingClientRect().height || 0) : 0;
                let guard = 0;
                let i = 0;
                while (i < body.children.length && guard++ < 4000) {
                    const el = body.children[i];
                    const h = el.getBoundingClientRect().height || 0;
                    const caption = el.matches && el.matches('figure.manuscript-figure')
                        ? el.querySelector('figcaption')
                        : null;
                    const movable = el.matches && el.matches('figure.manuscript-figure')
                        && !(caption && (caption.textContent || '').trim().length > 0);
                    if (movable && h > 0 && used > 8 && used + h > pageH) {
                        let room = pageH - used;
                        let j = i + 1;
                        let last = null;
                        while (j < body.children.length && room > 8) {
                            const nxt = body.children[j];
                            if (nxt.matches && nxt.matches('figure')) break;
                            const nh = nxt.getBoundingClientRect().height || 0;
                            if (nh <= 0 || nh > room) break;
                            room -= nh;
                            last = nxt;
                            j++;
                        }
                        if (last) {
                            last.after(el);
                            continue;
                        }
                    }
                    used += h;
                    while (used >= pageH) used -= pageH;
                    i++;
                }
            });
        }
        """;
}
