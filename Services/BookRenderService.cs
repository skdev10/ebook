using System.Globalization;
using System.Net;
using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services.PdfExport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EBookDashboard.Services;

/// <summary>
/// Produces canonical book HTML for preview iframe and Chromium PDF — one document, one CSS pipeline.
/// </summary>
public sealed class BookRenderService : IBookRenderService
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BookRenderService> _logger;
    private readonly ITocPageNumberMeasurer _tocPageNumberMeasurer;

    public BookRenderService(
        IWebHostEnvironment env,
        IConfiguration configuration,
        ILogger<BookRenderService> logger,
        ITocPageNumberMeasurer tocPageNumberMeasurer)
    {
        _env = env;
        _configuration = configuration;
        _logger = logger;
        _tocPageNumberMeasurer = tocPageNumberMeasurer;
    }

    /// <inheritdoc />
    public async Task<BookRenderResult> BuildBookHtmlAsync(BookRenderRequest request, CancellationToken cancellationToken = default)
    {
        var details = request.Details ?? throw new ArgumentNullException(nameof(request.Details));
        var opt = request.ExportOptions ?? new BookPdfExportOptions();
        opt.Normalize();

        var title = FirstNonEmpty(request.DisplayTitle, details.BookTitle);
        // Do not invent "Untitled" — title page / metadata only render when the user provided a title.
        var author = FirstNonEmpty(request.DisplayAuthor, details.AuthorName);
        var genre = (request.DisplayGenre ?? details.Genre ?? "").Trim();
        var coverSrc = await ResolveCoverSrcAsync(request.CoverImageDataUrl, details.CoverImagePath, cancellationToken);

        var phBase = BookManuscriptHtmlFormatter.CreateBaseContext(
            string.IsNullOrEmpty(title) ? " " : title, details.Subtitle, details.Description, genre, author);

        // Estimate page count for KDP gutter tiers. Prefer a conservative (higher) estimate so
        // gutters are not undersized when images/headings inflate the real page count.
        // Count text only — ignore data-URI / attribute bloat so image-heavy books don't jump
        // into the wrong gutter bracket.
        var bodyChars = details.Chapters?.Sum(c =>
        {
            var html = c.Content ?? "";
            html = System.Text.RegularExpressions.Regex.Replace(
                html, @"\bsrc\s*=\s*""[^""]+""", " ",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
            html = System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ");
            return html.Length + (c.Title?.Length ?? 0);
        }) ?? 0;
        var imgCount = details.Chapters?.Sum(c =>
            System.Text.RegularExpressions.Regex.Matches(c.Content ?? "", "<img\\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count) ?? 0;
        var estimatedPages = Math.Max(24, bodyChars / 1400 + imgCount * 1 + 8);
        KdpInteriorMarginCalculator.ApplyDefaults(opt, estimatedPages);

        var layout = BookPdfPlatformLayout.Resolve(opt, BookPdfLayoutOptions.FromConfiguration(_configuration));
        var arranged = BookChapterExportHelper.OrderForExport(details.Chapters).ToList();
        BookHtmlNormalizer.TakePrintedFrontMatter(
            arranged,
            opt.UseSourceTitlePage,
            opt.KeepOriginalCopyrightPage,
            out var sourceTitleHtml,
            out var sourceCopyrightHtml);
        var chapters = BookHtmlNormalizer.OmitDuplicateFrontMatter(
            arranged,
            opt.KeepOriginalCopyrightPage,
            title);
        sourceCopyrightHtml ??= BookHtmlNormalizer.PullCopyrightPage(chapters, opt.KeepOriginalCopyrightPage);
        var sections = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(chapters, phBase, opt);
        var copyrightHtml = sourceCopyrightHtml
            ?? InteriorFrontMatterBuilder.BuildCopyrightPageHtml(
                title, author, request.PublisherDisplayName);
        var bodyTpl = InteriorExportTheme.PdfBodyTemplateClass(opt.InteriorStyle);
        var shellCls = InteriorPrintDocumentBuilder.PreviewShellClass(opt.InteriorStyle);
        var wrapCls = InteriorPrintDocumentBuilder.PreviewInteriorWrapClass(opt.InteriorStyle);
        var contentHeightPx = ComputeContentHeightPx(layout);

        // Pass 1: placeholder TOC (ellipsis page refs — same layout, numbers filled after Chromium measurement).
        var tocPlaceholder = InteriorFrontMatterBuilder.BuildTocHtml(chapters, phBase);
        var htmlForMeasure = BookPreviewPrintHtmlBuilder.Build(
            title, author, genre, details.Subtitle, coverSrc, opt.IncludeCoverPage,
            copyrightHtml, tocPlaceholder, sections, opt, layout.PageSizeCss, bodyTpl, shellCls, wrapCls,
            _env.WebRootPath,
            contentHeightPx,
            sourceTitleHtml);

        var chapterTitles = new List<string>(chapters.Count);
        var tocNarrative = 0;
        for (var i = 0; i < chapters.Count; i++)
        {
            var ch = chapters[i];
            if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber, ch.Title))
                tocNarrative++;
            var phNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber, ch.Title) ? 1 : tocNarrative;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            chapterTitles.Add(BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum));
        }

        var chapterStartPages = await _tocPageNumberMeasurer.MeasureChapterStartPagesAsync(
            htmlForMeasure, opt, layout, title, chapterTitles, chapters.Count, cancellationToken);

        var frontMatterPageCount = EstimateFrontMatterPageCount(chapters, chapterStartPages);
        var tocHtml = InteriorFrontMatterBuilder.BuildTocHtml(
            chapters, phBase, chapterStartPages, pdfTargetCounters: false, frontMatterPageCount);
        var html = BookPreviewPrintHtmlBuilder.Build(
            title, author, genre, details.Subtitle, coverSrc, opt.IncludeCoverPage,
            copyrightHtml, tocHtml, sections, opt, layout.PageSizeCss, bodyTpl, shellCls, wrapCls,
            _env.WebRootPath,
            contentHeightPx,
            sourceTitleHtml);

        if (ChapterContentNormalizer.LooksLikeJsonEnvelope(html))
            _logger.LogWarning("Render HTML still contains JSON wrapper for book {BookId}.", details.BookId);

        return new BookRenderResult
        {
            Html = html,
            Layout = layout,
            Settings = BookFormattingSettings.FromExportOptions(opt),
            FrontMatterPageCount = frontMatterPageCount,
            ChapterStartPagesPhysical = chapterStartPages
        };
    }

    /// <summary>Physical pages before the first non-front-matter chapter.</summary>
    internal static int EstimateFrontMatterPageCount(
        IReadOnlyList<ChapterDto> chapters,
        IReadOnlyList<int> chapterStartPages)
    {
        // Title + copyright + TOC occupy pages before chapter sections; chapterStartPages are section starts.
        // Front-matter page count = first *body* chapter physical page − 1.
        // Preface is front matter even when ChapterNumber was remapped; never treat it as body start.
        for (var i = 0; i < chapters.Count && i < chapterStartPages.Count; i++)
        {
            var ch = chapters[i];
            if (BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber, ch.Title))
                continue;
            // Guard: fill-forward / soft-fail can leave early zeros that would inflate front matter.
            var bodyStart = chapterStartPages[i];
            if (bodyStart <= 0)
                continue;
            // Typical title+copyright+toc is 2–6 pages; clamp wild estimates from bad TOC measure.
            var estimate = Math.Max(0, bodyStart - 1);
            return Math.Clamp(estimate, 2, 12);
        }

        if (chapterStartPages.Count > 0)
        {
            var first = chapterStartPages.FirstOrDefault(p => p > 0);
            if (first > 0)
                return Math.Clamp(first - 1, 2, 12);
        }
        return 3;
    }

    private static double? ComputeContentHeightPx(BookPdfPlatformLayout.PdfLayoutSpec layout)
    {
        var pageIn = layout.PdfHeight != null && layout.PdfHeight.Contains("in", StringComparison.OrdinalIgnoreCase)
            ? ParseInches(layout.PdfHeight)
            : layout.UseBuiltInFormat ? 11.69 : 9.0;
        var top = ParseInches(layout.MarginTop);
        var bottom = ParseInches(layout.MarginBottom);
        if (pageIn is null || top is null || bottom is null) return null;
        var content = (pageIn.Value - top.Value - bottom.Value) * 96.0;
        return content > 100 ? content : null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var text = (value ?? "").Trim();
            if (text.Length > 0)
                return text;
        }

        return "";
    }

    private static double? ParseInches(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim().ToLowerInvariant();
        if (s.EndsWith("in", StringComparison.Ordinal)) s = s[..^2];
        else if (s.EndsWith("mm", StringComparison.Ordinal))
            return double.TryParse(s[..^2], NumberStyles.Any, CultureInfo.InvariantCulture, out var mm) ? mm / 25.4 : null;
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var inches) ? inches : null;
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
                if (!File.Exists(physical)) return null;
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not embed cover from {Path}", path);
        }

        return null;
    }
}
