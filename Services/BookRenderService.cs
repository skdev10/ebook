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

        var title = (request.DisplayTitle ?? details.BookTitle ?? "").Trim();
        if (string.IsNullOrEmpty(title)) title = "Untitled";
        var author = (request.DisplayAuthor ?? details.AuthorName ?? "").Trim();
        var genre = (request.DisplayGenre ?? details.Genre ?? "").Trim();
        var coverSrc = await ResolveCoverSrcAsync(request.CoverImageDataUrl, details.CoverImagePath, cancellationToken);

        var phBase = BookManuscriptHtmlFormatter.CreateBaseContext(
            title, details.Subtitle, details.Description, genre, author);

        var layout = BookPdfPlatformLayout.Resolve(opt, BookPdfLayoutOptions.FromConfiguration(_configuration));
        var chapters = BookChapterExportHelper.OrderForExport(details.Chapters);
        var sections = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(chapters, phBase, opt);
        var copyrightHtml = InteriorFrontMatterBuilder.BuildCopyrightPageHtml(
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
            contentHeightPx);

        var chapterTitles = new List<string>(chapters.Count);
        var tocNarrative = 0;
        for (var i = 0; i < chapters.Count; i++)
        {
            var ch = chapters[i];
            if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                tocNarrative++;
            var phNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 1 : tocNarrative;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            chapterTitles.Add(BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum));
        }

        var chapterStartPages = await _tocPageNumberMeasurer.MeasureChapterStartPagesAsync(
            htmlForMeasure, opt, layout, title, chapterTitles, chapters.Count, cancellationToken);

        var tocHtml = InteriorFrontMatterBuilder.BuildTocHtml(chapters, phBase, chapterStartPages, pdfTargetCounters: false);
        var html = BookPreviewPrintHtmlBuilder.Build(
            title, author, genre, details.Subtitle, coverSrc, opt.IncludeCoverPage,
            copyrightHtml, tocHtml, sections, opt, layout.PageSizeCss, bodyTpl, shellCls, wrapCls,
            _env.WebRootPath,
            contentHeightPx);

        if (ChapterContentNormalizer.LooksLikeJsonEnvelope(html))
            _logger.LogWarning("Render HTML still contains JSON wrapper for book {BookId}.", details.BookId);

        return new BookRenderResult
        {
            Html = html,
            Layout = layout,
            Settings = BookFormattingSettings.FromExportOptions(opt)
        };
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
