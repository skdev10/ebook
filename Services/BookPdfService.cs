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

namespace EBookDashboard.Services;

public class BookPdfService : IBookPdfService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BookPdfService> _logger;
    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static bool _fetched;

    public BookPdfService(IWebHostEnvironment env, ILogger<BookPdfService> logger)
    {
        _env = env;
        _logger = logger;
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
        await EnsureChromiumAsync(cancellationToken);

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
        var sections = new StringBuilder();
        var narrativeOrdinal = 0;
        for (var i = 0; i < chapters.Count; i++)
        {
            var ch = chapters[i];
            if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                narrativeOrdinal++;
            var displayNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 0 : narrativeOrdinal;
            var phNum = displayNum > 0 ? displayNum : 1;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            var displayHeading = BookChapterExportHelper.GetExportHeading(ch.Title, ch.ChapterNumber, phNum);
            var chTitleHtml = BookManuscriptHtmlFormatter.EscapeHtml(displayHeading);
            var sectionId = i + 1;
            var metaHtml = string.IsNullOrWhiteSpace(ch.ExportMetaHtml) ? "" : ch.ExportMetaHtml;
            var bodyRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Content ?? "", ph);
            var bodyHtml = BookManuscriptHtmlFormatter.FormatBodyToHtml(bodyRaw);
            sections.Append(CultureInvariant($"""
                <section class="chapter" id="ch-{sectionId}">
                  <h2 class="chapter-heading">{chTitleHtml}</h2>
                  {metaHtml}
                  <div class="chapter-body">{bodyHtml}</div>
                </section>
                """));
        }

        if (sections.Length == 0)
            sections.Append("""<section class="chapter" id="ch-0"><p class="manuscript-p">No chapters in this book yet.</p></section>""");

        var tocHtml = BuildTocHtml(chapters, phBase);
        var copyrightHtml = BuildCopyrightPageHtml(title, author, publisherDisplayName, layout.BleedNoteHtml, layout.CmykNoteHtml);
        var themeCss = BuildThemeCss(opt);
        var bodyTpl = InteriorTemplateClass(opt.InteriorStyle);

        var html = BuildPrintDocumentHtml(
            title,
            author,
            genre,
            details.Subtitle,
            coverSrc,
            opt.IncludeCoverPage,
            copyrightHtml,
            tocHtml,
            sections.ToString(),
            themeCss,
            layout.PageSizeCss,
            bodyTpl);

        var headerTemplate = BuildHeaderTemplate(title, author);
        var footerTemplate = BuildFooterTemplate();

        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--font-render-hinting=none" }
        });
        await using var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Load } });

        return await page.PdfDataAsync(BuildPdfOptions(layout, headerTemplate, footerTemplate));
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

    private static string InteriorTemplateClass(string? interiorStyle)
    {
        var s = (interiorStyle ?? "Novel").Trim();
        if (s.Equals("Modern", StringComparison.OrdinalIgnoreCase)) return "tpl-modern";
        if (s.Equals("Minimalist", StringComparison.OrdinalIgnoreCase)) return "tpl-minimalist";
        if (s.Equals("Classic", StringComparison.OrdinalIgnoreCase)) return "tpl-classic";
        if (s.Equals("ElegantTrade", StringComparison.OrdinalIgnoreCase)) return "tpl-elegant-trade";
        return "tpl-novel";
    }

    private static string CultureInvariant(FormattableString fs) => FormattableString.Invariant(fs);

    private static string BuildCopyrightPageHtml(string title, string author, string? publisherDisplayName, string? bleedNoteEncoded, string? cmykNoteEncoded)
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
        sb.AppendLine("""<p class="cr-small">This document was generated from your saved manuscript and formatting preferences. Chapter breaks and page numbers follow standard print layout.</p>""");
        if (!string.IsNullOrWhiteSpace(bleedNoteEncoded))
            sb.AppendLine($"""<p class="cr-small">{bleedNoteEncoded}</p>""");
        if (!string.IsNullOrWhiteSpace(cmykNoteEncoded))
            sb.AppendLine($"""<p class="cr-small">{cmykNoteEncoded}</p>""");
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
            var chapterLine = BookChapterExportHelper.GetExportHeading(ch.Title, ch.ChapterNumber, phNum);

            var bodyRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Content ?? "", ph);
            var bodyHtml = BookManuscriptHtmlFormatter.FormatBodyToHtml(bodyRaw);
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

    private static string BuildThemeCss(BookPdfExportOptions opt)
    {
        var pt = opt.BodyFontSizePt();
        var lh = opt.BodyLineHeight();
        var interior = (opt.InteriorStyle ?? "Novel").Trim();
        var isModern = interior.Equals("Modern", StringComparison.OrdinalIgnoreCase);
        var isMinimal = interior.Equals("Minimalist", StringComparison.OrdinalIgnoreCase);
        var isClassic = interior.Equals("Classic", StringComparison.OrdinalIgnoreCase);
        var isElegantTrade = interior.Equals("ElegantTrade", StringComparison.OrdinalIgnoreCase);
        var isNovel = interior.Equals("Novel", StringComparison.OrdinalIgnoreCase);
        var isFallbackNovel = !isModern && !isMinimal && !isClassic && !isElegantTrade && !isNovel;
        if (isFallbackNovel) isNovel = true;

        var bodyFont = (isModern || isMinimal)
            ? "system-ui, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif"
            : isClassic
                ? "'Palatino Linotype', 'Book Antiqua', Palatino, 'Times New Roman', Times, serif"
                : isElegantTrade
                    ? "'EB Garamond', 'Palatino Linotype', Palatino, Georgia, 'Times New Roman', Times, serif"
                    : "'Palatino Linotype', Georgia, 'Times New Roman', Times, serif";

        var headingColor = isModern ? "#4f46e5" : isMinimal ? "#0a0a0a" : isClassic ? "#78350f" : isElegantTrade ? "#4a3728" : "#4c1d95";
        var chapterAlign = (isClassic || isNovel || isElegantTrade) ? "text-align:center;" : "";
        var justify = isMinimal || isModern ? "text-align:left;" : "text-align:justify;";

        var baseCss = BuildThemeCssInner(pt, lh, bodyFont, headingColor, chapterAlign, justify);

        // Per-template refinements (distinct interiors for export, not only a stored label).
        var tpl = string.Concat(
            ".book-pdf-body.tpl-novel .chapter-heading { font-size: 16pt; letter-spacing: 0.02em; border-bottom: 2px solid #c4b5fd; padding-bottom: 4mm; margin-top: 2mm; } ",
            ".book-pdf-body.tpl-novel .toc-title { font-family: Georgia, 'Times New Roman', serif; } ",
            ".book-pdf-body.tpl-classic .chapter-heading { font-size: 17pt; font-variant: small-caps; letter-spacing: 0.12em; border-bottom: 3px double #d6c4a8; padding-bottom: 5mm; margin-top: 4mm; } ",
            ".book-pdf-body.tpl-classic .toc-title { font-variant: small-caps; letter-spacing: 0.15em; } ",
            ".book-pdf-body.tpl-classic blockquote { border-left-color: #d6c4a8; } ",
            ".book-pdf-body.tpl-modern .chapter-heading { font-family: system-ui, 'Segoe UI', sans-serif; font-weight: 800; border-bottom: none; border-left: 4px solid #4f46e5; padding-left: 4mm; text-align: left; } ",
            ".book-pdf-body.tpl-modern .toc-title { font-family: system-ui, sans-serif; font-weight: 800; } ",
            ".book-pdf-body.tpl-minimalist .chapter-heading { font-size: 14pt; font-weight: 600; border-bottom: 1px solid #e5e5e5; letter-spacing: -0.02em; text-align: left; } ",
            ".book-pdf-body.tpl-minimalist .toc-title { font-weight: 600; letter-spacing: -0.03em; } ",
            ".book-pdf-body.tpl-elegant-trade .chapter-heading { font-family: 'Lora', Georgia, 'Times New Roman', Times, serif; font-size: 17pt; font-weight: 600; letter-spacing: 0.04em; color: #3d2914; border-bottom: 1px solid #c9b8a0; padding-bottom: 5mm; margin-top: 3mm; } ",
            ".book-pdf-body.tpl-elegant-trade .toc-title { font-family: 'Lora', Georgia, 'Times New Roman', Times, serif; font-weight: 600; letter-spacing: 0.03em; color: #4a3728; } ",
            ".book-pdf-body.tpl-elegant-trade blockquote { border-left-color: #c9b8a0; } "
        );

        return baseCss + tpl;
    }

    private static string BuildThemeCssInner(string pt, string lh, string bodyFont, string headingColor, string chapterAlign, string justify)
    {
        return string.Concat(
            ":root { ",
            "--body-pt: ", pt, "pt; ",
            "--body-lh: ", lh, "; ",
            "--body-font: ", bodyFont, "; ",
            "--heading-color: ", headingColor, "; ",
            "} ",
            ".book-pdf-body { font-family: var(--body-font); font-size: var(--body-pt); line-height: var(--body-lh); color: #0f172a; margin: 0; } ",
            ".front-matter-page { page-break-after: always; padding-top: 8mm; } ",
            ".copyright-page .cr-meta { font-size: 12pt; margin: 0 0 4mm; } ",
            ".copyright-page .cr-legal { font-size: 10pt; margin: 6mm 0 3mm; line-height: 1.5; } ",
            ".copyright-page .cr-small { font-size: 9pt; color: #64748b; margin-top: 4mm; line-height: 1.45; } ",
            ".toc-title { font-size: 20pt; margin: 0 0 6mm; color: var(--heading-color); font-weight: 600; } ",
            ".toc-hint { font-size: 9pt; color: #64748b; margin: 0 0 8mm; } ",
            ".toc-list { margin: 0; padding-left: 5mm; } ",
            ".toc-item { margin: 0 0 5mm; font-size: 11pt; list-style-position: outside; } ",
            ".toc-chapter-line { font-weight: 600; margin: 0 0 2mm; display: flex; align-items: baseline; gap: 6mm; } ",
            ".toc-chapter-line::after { content: ''; flex: 1 1 auto; border-bottom: 1px dotted #94a3b8; transform: translateY(-2px); } ",
            ".toc-page-ref { font-weight: 600; min-width: 9mm; text-align: right; color: #334155; } ",
            ".toc-subheadings { list-style: none; padding-left: 8mm; margin: 0 0 2mm; } ",
            ".toc-subheading-item { font-size: 10pt; color: #334155; margin: 0 0 1.8mm; font-weight: 400; line-height: 1.35; } ",
            ".toc-heading-prefix { font-weight: 600; color: #0f172a; margin-right: 2mm; } ",
            ".toc-link { color: #0f172a; text-decoration: none; } ",
            ".title-page .subtitle { font-size: 12pt; color: #64748b; margin-top: 4mm; } ",
            ".chapter-heading { font-size: 15pt; margin: 0 0 6mm; color: var(--heading-color); border-bottom: 1px solid #e2e8f0; padding-bottom: 2mm; ",
            chapterAlign, " } ",
            ".export-meta { font-size: 9pt; color: #64748b; margin: 0 0 4mm; line-height: 1.45; } ",
            ".chapter-body { ", justify, " } ",
            ".manuscript-h1 { font-size: 16pt; margin: 5mm 0 3mm; } ",
            ".manuscript-h2 { font-size: 14pt; margin: 4mm 0 2mm; } ",
            ".manuscript-h3 { font-size: 12pt; margin: 3mm 0 2mm; } ",
            ".manuscript-h4 { font-size: 11pt; margin: 2mm 0 1mm; } ",
            ".manuscript-p { margin: 0 0 3mm; orphans: 2; widows: 2; } ",
            ".manuscript-hr { border: none; border-top: 1px solid #cbd5e1; margin: 6mm 0; } ",
            "blockquote { margin: 3mm 0 3mm 6mm; padding-left: 4mm; border-left: 3px solid #c4b5fd; color: #334155; } ",
            /* In-chapter headings (h1–h6) must not start a new page — only a new <section class=\"chapter\"> does (KDP-style). */
            ".chapter-body h1, .chapter-body h2, .chapter-body h3, .chapter-body h4, .chapter-body h5, .chapter-body h6, ",
            ".chapter-body .manuscript-h1, .chapter-body .manuscript-h2, .chapter-body .manuscript-h3, .chapter-body .manuscript-h4, .chapter-body .manuscript-h5, .chapter-body .manuscript-h6 { ",
            "page-break-before: avoid !important; break-before: avoid !important; page-break-after: avoid; break-after: avoid; ",
            "page-break-inside: avoid; break-inside: avoid; } ",
            /* Chapter 2+ always start a new page; headings inside .chapter-body never force that. */
            ".manuscript-root > section.chapter { -webkit-region-break-inside: auto; } ",
            ".manuscript-root > section.chapter:first-of-type { break-before: auto; page-break-before: auto; } ",
            ".manuscript-root > section.chapter ~ section.chapter { break-before: page; page-break-before: always; } ",
            ".manuscript-root > section.chapter:last-of-type { break-after: auto; page-break-after: auto; } ");
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
        string bodyTemplateClass)
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
        if (bodyTemplateClass.Contains("tpl-elegant-trade", StringComparison.Ordinal))
        {
            doc.AppendLine("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\"/>");
            doc.AppendLine("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin/>");
            doc.AppendLine("<link href=\"https://fonts.googleapis.com/css2?family=EB+Garamond:ital,wght@0,400;0,500;0,600;1,400&family=Lora:wght@500;600;700&display=swap\" rel=\"stylesheet\"/>");
        }
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
        doc.AppendLine("<body class=\"book-pdf-body " + bodyTemplateClass + "\">");
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
