using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using EBookDashboard.Services.PdfExport;
using HtmlAgilityPack;
using EBookDashboard.Models.DTO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services;

public class BookPdfService : IBookPdfService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BookPdfService> _logger;
    private readonly IConfiguration _configuration;

    public BookPdfService(IWebHostEnvironment env, ILogger<BookPdfService> logger, IConfiguration configuration)
    {
        _env = env;
        _logger = logger;
        _configuration = configuration;
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

        var layout = BookPdfPlatformLayout.Resolve(opt, BookPdfLayoutOptions.FromConfiguration(_configuration));

        // One DB chapter = one PDF chapter. Do not split on in-body <h2> — those are section headings (##), not new chapters.
        var chapters = BookChapterExportHelper.OrderForExport(details.Chapters);
        var sections = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(chapters, phBase, opt);
        var tocHtml = BuildTocHtml(chapters, phBase);
        var copyrightHtml = BuildCopyrightPageHtml(title, author, publisherDisplayName);
        var bodyTpl = InteriorExportTheme.PdfBodyTemplateClass(opt.InteriorStyle);
        var shellCls = InteriorPrintDocumentBuilder.PreviewShellClass(opt.InteriorStyle);
        var wrapCls = InteriorPrintDocumentBuilder.PreviewInteriorWrapClass(opt.InteriorStyle);

        // BookPreview HTML = PDF input (CSS-based, 1:1 with formatter preview).
        var html = BookPreviewPrintHtmlBuilder.Build(
            title, author, genre, details.Subtitle, coverSrc, opt.IncludeCoverPage,
            copyrightHtml, tocHtml, sections, opt, layout.PageSizeCss, bodyTpl, shellCls, wrapCls,
            _env.WebRootPath);

        if (ChapterContentNormalizer.LooksLikeJsonEnvelope(html))
            _logger.LogWarning("Export HTML still contains JSON wrapper after normalization for book {BookId}.", details.BookId);

        _logger.LogDebug(
            "PDF export book={BookId} engine={Engine} style={Style} interior={Interior} htmlLen={Len} hasPreviewSheet={Sheet}",
            details.BookId, PdfExportEngine.Resolve(_configuration), opt.InteriorStyle, wrapCls,
            html.Length, html.Contains("book-preview-sheet", StringComparison.Ordinal));

        var headerTemplate = BuildHeaderTemplate(title, author);
        var footerTemplate = BuildFooterTemplate();

        var engine = PdfExportEngine.Resolve(_configuration);
        if (engine == PdfExportEngine.DinkToPdf)
        {
            _logger.LogWarning("DinkToPdf engine is not wired yet; using Chromium for book {BookId}.", details.BookId);
            engine = PdfExportEngine.Chromium;
        }

        if (engine != PdfExportEngine.PdfSharp)
        {
            try
            {
                var chromium = new ChromiumPdfExporter(_logger, _configuration);
                var pdfBytes = await chromium.ExportAsync(html, layout, headerTemplate, footerTemplate, cancellationToken);
                EnsureValidPdf(pdfBytes);
                _logger.LogInformation(
                    "Chromium PDF: {Bytes} bytes, 6x9={W}x{H}, style={Style}, pageBg={PageBg}, book={BookId}",
                    pdfBytes.Length, layout.PdfWidth, layout.PdfHeight, opt.InteriorStyle,
                    opt.ResolvePageBackgroundColor(), details.BookId);
                return pdfBytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chromium CSS PDF failed for book {BookId}; falling back to PdfSharp.", details.BookId);
            }
        }

        try
        {
            var exporter = new PdfSharpBookExporter(_env, _logger);
            var fallback = exporter.Render(details, coverSrc, title, author, opt, opt.IncludeCoverPage);
            EnsureValidPdf(fallback);
            _logger.LogInformation("PDF generated via PdfSharp fallback: {Bytes} bytes, book={BookId}", fallback.Length, details.BookId);
            return fallback;
        }
        catch (Exception sharpEx)
        {
            _logger.LogError(sharpEx, "PdfSharp fallback failed for book {BookId}", details.BookId);
            throw new InvalidOperationException(
                "PDF generation failed. Install Google Chrome on the server (apt install google-chrome-stable) and set Puppeteer:ExecutablePath, or run Scripts/download-export-fonts.sh for PdfSharp.",
                sharpEx);
        }
    }

    private static void EnsureValidPdf(byte[]? pdfBytes)
    {
        if (pdfBytes == null || pdfBytes.Length < 128)
            throw new InvalidOperationException("PDF generation produced an empty document.");
        if (pdfBytes[0] != (byte)'%' || pdfBytes[1] != (byte)'P' || pdfBytes[2] != (byte)'D' || pdfBytes[3] != (byte)'F')
            throw new InvalidOperationException("PDF generation produced invalid output.");
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
}
