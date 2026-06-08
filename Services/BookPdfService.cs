using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using HtmlAgilityPack;
using EBookDashboard.Models.DTO;
using Microsoft.AspNetCore.Hosting;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services;

public class BookPdfService : IBookPdfService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BookPdfService> _logger;
    private readonly IConfiguration _configuration;
    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static bool _fetched;

    public BookPdfService(IWebHostEnvironment env, ILogger<BookPdfService> logger, IConfiguration configuration)
    {
        _env = env;
        _logger = logger;
        _configuration = configuration;
    }

    private static readonly string[] LinuxBrowserCandidates =
    [
        "/usr/bin/google-chrome-stable",
        "/usr/bin/google-chrome",
        "/usr/bin/chromium-browser",
        "/usr/bin/chromium",
        "/snap/bin/chromium"
    ];

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

        var winChrome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Google", "Chrome", "Application", "chrome.exe");
        if (File.Exists(winChrome)) return winChrome;

        var winChromeX86 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Google", "Chrome", "Application", "chrome.exe");
        if (File.Exists(winChromeX86)) return winChromeX86;

        var winEdge = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft", "Edge", "Application", "msedge.exe");
        if (File.Exists(winEdge)) return winEdge;

        var winEdge64 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft", "Edge", "Application", "msedge.exe");
        if (File.Exists(winEdge64)) return winEdge64;

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

    public async Task<byte[]> RenderFullBookPdfAsync(
        BookDetailsResponseDto details,
        string? coverImageDataUrl,
        string? displayTitle,
        string? displayAuthor,
        string? displayGenre,
        BookPdfExportOptions exportOptions,
        string? publisherDisplayName,
        CancellationToken cancellationToken = default)
    {
        var opt = exportOptions ?? new BookPdfExportOptions();

        var title = (displayTitle ?? details.BookTitle ?? "").Trim();
        if (string.IsNullOrEmpty(title)) title = "Untitled";
        var author = (displayAuthor ?? details.AuthorName ?? "").Trim();
        var genre = (displayGenre ?? details.Genre ?? "").Trim();
        var coverSrc = await ResolveCoverSrcAsync(coverImageDataUrl, details.CoverImagePath, cancellationToken);

        var phBase = BookManuscriptHtmlFormatter.CreateBaseContext(
            title,
            details.Subtitle,
            details.Description,
            genre,
            author);

        var layout = BookPdfPlatformLayout.Resolve(opt);

        // One DB chapter = one PDF chapter. Do not split on in-body <h2> — those are section headings (##), not new chapters.
        var chapters = BookChapterExportHelper.OrderForExport(details.Chapters);
        var sections = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(chapters, phBase, opt);
        var tocHtml = BuildTocHtml(chapters, phBase);
        var copyrightHtml = BuildCopyrightPageHtml(title, author, publisherDisplayName);
        var themeCss = InteriorExportTheme.BuildPdfThemeCss(opt);
        var bodyTpl = InteriorExportTheme.PdfBodyTemplateClass(opt.InteriorStyle);
        var shellCls = InteriorPrintDocumentBuilder.PreviewShellClass(opt.InteriorStyle);
        var wrapCls = InteriorPrintDocumentBuilder.PreviewInteriorWrapClass(opt.InteriorStyle);

        var html = BuildPrintDocumentHtml(
            title,
            author,
            genre,
            details.Subtitle,
            coverSrc,
            opt.IncludeCoverPage,
            copyrightHtml,
            tocHtml,
            sections,
            themeCss,
            layout.PageSizeCss,
            bodyTpl,
            shellCls,
            wrapCls);

        var headerTemplate = BuildHeaderTemplate(title, author);
        var footerTemplate = BuildFooterTemplate();

        var executablePath = await ResolveBrowserExecutableAsync(cancellationToken);
        var launchOptions = new LaunchOptions
        {
            Headless = true,
            ExecutablePath = executablePath,
            Args = new[]
            {
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--font-render-hinting=none",
                "--disable-gpu"
            }
        };

        try
        {
            await using var browser = await Puppeteer.LaunchAsync(launchOptions);
            await using var page = await browser.NewPageAsync();
            await page.EmulateMediaTypeAsync(MediaType.Print);
            await page.SetContentAsync(html, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                Timeout = 90_000
            });
            var fontStatus = await page.EvaluateFunctionAsync<string>(@"async () => {
                if (document.fonts && document.fonts.ready) await document.fonts.ready;
                await new Promise(r => setTimeout(r, 1200));
                if (!document.fonts || !document.fonts.forEach) return 'no-fonts-api';
                var loaded = [], failed = [];
                document.fonts.forEach(f => {
                    var line = (f.family || '?') + ' ' + (f.weight || '') + ' ' + (f.status || '');
                    if (f.status === 'loaded') loaded.push(line);
                    else if (f.status === 'error' || f.status === 'unloaded') failed.push(line);
                });
                return JSON.stringify({ loaded: loaded.length, failed: failed.length, failedFamilies: failed.slice(0, 8) });
            }");
            _logger.LogInformation("PDF font preload book={BookId} style={Style}: {FontStatus}",
                details.BookId, opt.InteriorStyle, fontStatus ?? "unknown");
            await Task.Delay(800, cancellationToken);

            var pdfBytes = await page.PdfDataAsync(BuildPdfOptions(layout, headerTemplate, footerTemplate));
            EnsureValidPdf(pdfBytes);
            _logger.LogInformation("PDF generated: {Bytes} bytes, style={Style}, book={BookId}",
                pdfBytes.Length, opt.InteriorStyle, details.BookId);
            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Puppeteer PDF render failed (executable={Executable}); falling back to PdfSharp.", executablePath ?? "bundled");
            try
            {
                var fallback = BookPdfSharpRenderer.RenderInteriorPdf(details, title, author, opt);
                EnsureValidPdf(fallback);
                _logger.LogInformation("PDF generated via PdfSharp fallback: {Bytes} bytes, book={BookId}", fallback.Length, details.BookId);
                return fallback;
            }
            catch (Exception sharpEx)
            {
                _logger.LogError(sharpEx, "PdfSharp fallback also failed for book {BookId}", details.BookId);
                throw new InvalidOperationException(
                    "PDF generation failed. Install Google Chrome or Microsoft Edge on the server, or set Puppeteer:ExecutablePath in configuration.",
                    ex);
            }
        }
    }

    private static void EnsureValidPdf(byte[]? pdfBytes)
    {
        if (pdfBytes == null || pdfBytes.Length < 128)
            throw new InvalidOperationException("PDF generation produced an empty document.");
        if (pdfBytes[0] != (byte)'%' || pdfBytes[1] != (byte)'P' || pdfBytes[2] != (byte)'D' || pdfBytes[3] != (byte)'F')
            throw new InvalidOperationException("PDF generation produced invalid output.");
    }

    private static PdfOptions BuildPdfOptions(BookPdfPlatformLayout.PdfLayoutSpec layout, string headerTemplate, string footerTemplate)
    {
        var o = new PdfOptions
        {
            PrintBackground = true,
            PreferCSSPageSize = layout.PreferCssPageSize,
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
            o.Width = layout.PdfWidth;
            o.Height = layout.PdfHeight;
        }

        return o;
    }

    private static string CultureInvariant(FormattableString fs) => FormattableString.Invariant(fs);

    private static string BuildCopyrightPageHtml(string title, string author, string? publisherDisplayName)
    {
        var y = DateTime.UtcNow.Year;
        var sb = new StringBuilder();
        sb.AppendLine("""<div class="front-matter-page copyright-page">""");
        sb.AppendLine($"""<p class="cr-meta"><strong>{WebUtility.HtmlEncode(title)}</strong></p>""");
        if (!string.IsNullOrEmpty(author))
            sb.AppendLine($"""<p class="cr-meta">{WebUtility.HtmlEncode(author)}</p>""");
        sb.AppendLine($"""<p class="cr-legal">Copyright © {y.ToString(CultureInfo.InvariantCulture)} {WebUtility.HtmlEncode(author)}. All rights reserved.</p>""");
        if (!string.IsNullOrEmpty(publisherDisplayName))
            sb.AppendLine($"""<p class="cr-legal">Prepared for publication by {WebUtility.HtmlEncode(publisherDisplayName)}.</p>""");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    /// <summary>
    /// Pulls in-chapter headings (h1–h6) from formatted body HTML in document order for PDF table of contents.
    /// </summary>
    private static List<string> ExtractHeadingsFromChapterBodyHtml(string bodyHtml)
    {
        var headings = new List<string>();
        if (string.IsNullOrWhiteSpace(bodyHtml)) return headings;
        try
        {
            var doc = new HtmlDocument();
            doc.OptionFixNestedTags = true;
            doc.LoadHtml(bodyHtml);
            var nodes = doc.DocumentNode.SelectNodes("//h1|//h2|//h3|//h4|//h5|//h6");
            if (nodes == null) return headings;
            foreach (var node in nodes)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText ?? "");
                text = Regex.Replace(text.Replace('\n', ' '), @"\s+", " ").Trim();
                if (text.Length == 0) continue;
                headings.Add(text);
            }
        }
        catch
        {
            // Malformed chapter HTML should not block export.
        }

        return headings;
    }

    private static string BuildTocHtml(List<ChapterDto> chapters, BookManuscriptHtmlFormatter.PlaceholderContext phBase)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<div class="front-matter-page toc-page">""");
        sb.AppendLine("""<h1 class="toc-title">Contents</h1>""");
        sb.AppendLine("""<p class="toc-hint">Chapter titles and in-chapter headings (print-style contents) with clickable chapter links and page references.</p>""");
        sb.AppendLine("""<ol class="toc-list">""");
        var tocNarrative = 0;
        for (var i = 0; i < chapters.Count; i++)
        {
            var ch = chapters[i];
            if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                tocNarrative++;
            var phNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 1 : tocNarrative;
            var sectionId = i + 1;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            var chapterLine = BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum);
            var bodyHtml = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(ch.Content, ph, chapterLine);
            var subHeadings = ExtractHeadingsFromChapterBodyHtml(bodyHtml);

            sb.AppendLine("""<li class="toc-item">""");
            sb.AppendLine(CultureInvariant(
                $"""<div class="toc-chapter-line"><a href="#ch-{sectionId}" class="toc-link">{WebUtility.HtmlEncode(chapterLine)}</a><span class="toc-page-ref" data-target="ch-{sectionId}">…</span></div>"""));
            if (subHeadings.Count > 0)
            {
                sb.AppendLine("""<ul class="toc-subheadings">""");
                foreach (var h in subHeadings)
                    sb.AppendLine(CultureInvariant(
                        $"""<li class="toc-subheading-item"><span class="toc-heading-prefix">Heading:</span> {WebUtility.HtmlEncode(h)}</li>"""));
                sb.AppendLine("</ul>");
            }

            sb.AppendLine("</li>");
        }

        if (chapters.Count == 0)
            sb.AppendLine("""<li class="toc-item">No chapters yet.</li>""");
        sb.AppendLine("</ol></div>");
        return sb.ToString();
    }

    /// <summary>Stable chapter order for export — one stored chapter per PDF section (no splitting on ## / h2).</summary>
    private static List<ChapterDto> OrderChaptersForPdf(IEnumerable<ChapterDto> source) =>
        source.OrderBy(c => c.ChapterNumber).ToList();

    private static string BuildHeaderTemplate(string title, string author)
    {
        var t = WebUtility.HtmlEncode(TruncateForHeader(title, 48));
        var a = WebUtility.HtmlEncode(TruncateForHeader(author, 36));
        return "<div style=\"width:100%;font-size:9px;color:#475569;padding:0 8mm;box-sizing:border-box;" +
               "display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid #e2e8f0;\">" +
               "<span style=\"font-weight:600;max-width:45%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;\">" + t + "</span>" +
               "<span style=\"max-width:45%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;text-align:right;\">" + a + "</span>" +
               "</div>";
    }

    private static string BuildFooterTemplate()
    {
        return """
            <div style="width:100%;font-size:9px;color:#64748b;padding:0 8mm;box-sizing:border-box;display:flex;justify-content:center;align-items:center;gap:4px;">
              <span>Page</span>
              <span class="pageNumber"></span>
              <span>of</span>
              <span class="totalPages"></span>
            </div>
            """;
    }

    private static string TruncateForHeader(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
    }

    private async Task<string?> ResolveCoverSrcAsync(string? dataUrl, string? coverPath, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dataUrl) &&
            (dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ||
             dataUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             dataUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return dataUrl.Trim();

        if (string.IsNullOrWhiteSpace(coverPath)) return null;
        var path = coverPath.Trim();
        try
        {
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return path;

            if (path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
            {
                var rel = path.TrimStart('~').TrimStart('/');
                var physical = Path.Combine(_env.WebRootPath ?? "", rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(physical))
                {
                    var bytes = await File.ReadAllBytesAsync(physical, ct);
                    var b64 = Convert.ToBase64String(bytes);
                    var ext = Path.GetExtension(physical).ToLowerInvariant();
                    var mime = ext switch
                    {
                        ".png" => "image/png",
                        ".gif" => "image/gif",
                        ".webp" => "image/webp",
                        _ => "image/jpeg"
                    };
                    return $"data:{mime};base64,{b64}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not embed cover image from path {Path}", path);
        }

        return null;
    }

    private static string BuildPrintDocumentHtml(
        string title,
        string author,
        string genre,
        string? subtitle,
        string? coverSrc,
        bool includeCoverPage,
        string copyrightHtml,
        string tocHtml,
        string chapterSections,
        string themeCss,
        string pageSizeCss,
        string bodyTemplateClass,
        string previewShellClass,
        string previewWrapClass)
    {
        var coverBlock = !includeCoverPage
            ? ""
            : string.IsNullOrEmpty(coverSrc)
            ? """
              <div class="cover-page cover-fallback">
                <div class="cover-fallback-inner">
                  <h1 class="cover-title">COVER</h1>
                  <p class="cover-meta">Generate or select a cover in the dashboard for a full graphic cover in export.</p>
                </div>
              </div>
              """
            : $"""
              <div class="cover-page">
                <img src="{WebUtility.HtmlEncode(coverSrc)}" alt="" class="cover-img" />
              </div>
              """;

        var metaLines = new StringBuilder();
        if (!string.IsNullOrEmpty(author))
            metaLines.Append(CultureInvariant($"""<p class="title-page-author">{WebUtility.HtmlEncode(author)}</p>"""));
        if (!string.IsNullOrEmpty(genre))
            metaLines.Append(CultureInvariant($"""<p class="title-page-genre">{WebUtility.HtmlEncode(genre)}</p>"""));

        var subtitleBlock = string.IsNullOrWhiteSpace(subtitle)
            ? ""
            : CultureInvariant($"""<p class="subtitle">{WebUtility.HtmlEncode(subtitle.Trim())}</p>""");
        var pageHeightPx = pageSizeCss.Contains("A4", StringComparison.OrdinalIgnoreCase) ? 1122.0 : 864.0;

        var doc = new StringBuilder();
        doc.AppendLine("<!DOCTYPE html>");
        doc.AppendLine("<html lang=\"en\">");
        doc.AppendLine("<head><meta charset=\"utf-8\"/>");
        doc.AppendLine(InteriorPrintDocumentBuilder.GoogleFontLinks());
        doc.AppendLine("<style>");
        doc.AppendLine(CultureInvariant($"@page {{ size: {pageSizeCss}; }}"));
        doc.AppendLine("* { box-sizing: border-box; }");
        doc.AppendLine(themeCss);
        doc.AppendLine(".cover-page { page-break-after: always; width: 100%; min-height: 100vh; position: relative; margin: 0; padding: 0; background: #1e1b4b; }");
        doc.AppendLine(".cover-img { width: 100%; height: 100vh; object-fit: cover; display: block; }");
        doc.AppendLine(".cover-fallback { display: flex; align-items: center; justify-content: center; color: #fafafa; min-height: 100vh; }");
        doc.AppendLine(".cover-fallback-inner { text-align: center; padding: 24mm; }");
        doc.AppendLine(".cover-title { font-size: 28pt; margin: 0 0 8mm; letter-spacing: 0.08em; }");
        doc.AppendLine(".cover-meta { font-size: 11pt; opacity: 0.85; max-width: 140mm; margin: 0 auto; }");
        doc.AppendLine(".title-page { page-break-after: always; text-align: center; padding-top: 36mm; }");
        doc.AppendLine(".title-page h1 { font-size: 24pt; margin: 0 0 8mm; font-weight: 600; }");
        doc.AppendLine(".title-page-author { font-size: 13pt; margin: 4mm 0; }");
        doc.AppendLine(".title-page-genre { font-size: 10pt; color: #64748b; text-transform: uppercase; letter-spacing: 0.12em; }");
        doc.AppendLine("</style></head>");
        doc.AppendLine("<body class=\"book-pdf-body " + bodyTemplateClass + " " + previewShellClass + " " + previewWrapClass + "\">");
        doc.Append(coverBlock);
        doc.AppendLine("<div class=\"title-page\">");
        doc.Append("<h1>").Append(WebUtility.HtmlEncode(title)).AppendLine("</h1>");
        doc.AppendLine(subtitleBlock);
        doc.Append(metaLines);
        doc.AppendLine("</div>");
        doc.AppendLine(copyrightHtml);
        doc.AppendLine(tocHtml);
        doc.AppendLine("""<div class="manuscript-root">""");
        doc.Append(chapterSections);
        doc.AppendLine("</div>");
        doc.AppendLine("<script>");
        doc.AppendLine("(function () {");
        doc.AppendLine("  var pageHeight = " + pageHeightPx.ToString("0.###", CultureInfo.InvariantCulture) + ";");
        doc.AppendLine("  if (!Number.isFinite(pageHeight) || pageHeight <= 0) pageHeight = 864;");
        doc.AppendLine("  var refs = document.querySelectorAll('.toc-page-ref[data-target]');");
        doc.AppendLine("  refs.forEach(function (el) {");
        doc.AppendLine("    var id = el.getAttribute('data-target');");
        doc.AppendLine("    if (!id) return;");
        doc.AppendLine("    var target = document.getElementById(id);");
        doc.AppendLine("    if (!target) return;");
        doc.AppendLine("    var top = target.getBoundingClientRect().top + window.scrollY;");
        doc.AppendLine("    var pageNo = Math.max(1, Math.floor(top / pageHeight) + 1);");
        doc.AppendLine("    el.textContent = String(pageNo);");
        doc.AppendLine("  });");
        doc.AppendLine("})();");
        doc.AppendLine("</script>");
        doc.AppendLine("</body></html>");
        return doc.ToString();
    }
}
