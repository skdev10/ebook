using System.Globalization;
using System.Net;
using System.Text;
using EBookDashboard.Interfaces;
using EBookDashboard.Services.PdfExport;
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
    private readonly PdfHtmlExportServiceResolver _pdfEngineResolver;

    public BookPdfService(
        IWebHostEnvironment env,
        ILogger<BookPdfService> logger,
        IConfiguration configuration,
        PdfHtmlExportServiceResolver pdfEngineResolver)
    {
        _env = env;
        _logger = logger;
        _configuration = configuration;
        _pdfEngineResolver = pdfEngineResolver;
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
        var tocHtml = InteriorFrontMatterBuilder.BuildTocHtml(chapters, phBase);
        var copyrightHtml = InteriorFrontMatterBuilder.BuildCopyrightPageHtml(title, author, publisherDisplayName);
        var bodyTpl = InteriorExportTheme.PdfBodyTemplateClass(opt.InteriorStyle);
        var shellCls = InteriorPrintDocumentBuilder.PreviewShellClass(opt.InteriorStyle);
        var wrapCls = InteriorPrintDocumentBuilder.PreviewInteriorWrapClass(opt.InteriorStyle);

        // BookPreview HTML = PDF input (CSS-based, 1:1 with formatter preview).
        var html = BookPreviewPrintHtmlBuilder.Build(
            title, author, genre, details.Subtitle, coverSrc, opt.IncludeCoverPage,
            copyrightHtml, tocHtml, sections, opt, layout.PageSizeCss, bodyTpl, shellCls, wrapCls,
            _env.WebRootPath,
            ComputeContentHeightPx(layout));

        if (ChapterContentNormalizer.LooksLikeJsonEnvelope(html))
            _logger.LogWarning("Export HTML still contains JSON wrapper after normalization for book {BookId}.", details.BookId);

        _logger.LogDebug(
            "PDF export book={BookId} engine={Engine} style={Style} interior={Interior} htmlLen={Len} hasPreviewSheet={Sheet}",
            details.BookId, PdfExportEngine.Resolve(_configuration), opt.InteriorStyle, wrapCls,
            html.Length, html.Contains("book-preview-sheet", StringComparison.Ordinal));

        var headerTemplate = BuildHeaderTemplate(title, author);
        var footerTemplate = BuildFooterTemplate();

        var configuredEngine = PdfExportEngine.Resolve(_configuration);
        if (configuredEngine != PdfExportEngine.PdfSharp)
        {
            try
            {
                var htmlEngine = _pdfEngineResolver.ResolvePrimary();
                var pdfBytes = await htmlEngine.ExportHtmlAsync(new PdfHtmlExportRequest
                {
                    Html = html,
                    Layout = layout,
                    HeaderTemplate = headerTemplate,
                    FooterTemplate = footerTemplate,
                    BookId = details.BookId
                }, cancellationToken);
                EnsureValidPdf(pdfBytes);
                _logger.LogInformation(
                    "{Engine} PDF: {Bytes} bytes, 6x9={W}x{H}, style={Style}, pageBg={PageBg}, book={BookId}",
                    htmlEngine.EngineName, pdfBytes.Length, layout.PdfWidth, layout.PdfHeight, opt.InteriorStyle,
                    opt.ResolvePageBackgroundColor(), details.BookId);
                return pdfBytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HTML PDF engine failed for book {BookId}; falling back to PdfSharp.", details.BookId);
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

    /// <summary>Flowed content height per printed page (page height minus top/bottom print margins) at 96 dpi.</summary>
    private static double? ComputeContentHeightPx(BookPdfPlatformLayout.PdfLayoutSpec layout)
    {
        var pageIn = layout.PdfHeight != null && layout.PdfHeight.Contains("in", StringComparison.OrdinalIgnoreCase)
            ? ParseInches(layout.PdfHeight)
            : (layout.UseBuiltInFormat ? 11.69 : 9.0); // A4 height fallback
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

    private static void EnsureValidPdf(byte[]? pdfBytes)
    {
        if (pdfBytes == null || pdfBytes.Length < 128)
            throw new InvalidOperationException("PDF generation produced an empty document.");
        if (pdfBytes[0] != (byte)'%' || pdfBytes[1] != (byte)'P' || pdfBytes[2] != (byte)'D' || pdfBytes[3] != (byte)'F')
            throw new InvalidOperationException("PDF generation produced invalid output.");
    }

    /// <summary>
    /// Book-style running head: small letterspaced caps, centered, floated into the top margin
    /// with breathing room above and below — no web-style rule line.
    /// </summary>
    private static string BuildHeaderTemplate(string title, string author)
    {
        var t = WebUtility.HtmlEncode(TruncateForHeader(title, 52));
        return "<div style=\"width:100%;box-sizing:border-box;padding:" + InteriorLayoutTokens.RunningHeadPadTop +
               " " + InteriorLayoutTokens.RunningHeadPadSides + " 0 " + InteriorLayoutTokens.RunningHeadPadInside + ";" +
               "font-family:Georgia,'Times New Roman',serif;font-size:7.5px;color:#7c7368;" +
               "letter-spacing:0.22em;text-transform:uppercase;text-align:center;" +
               "overflow:hidden;text-overflow:ellipsis;white-space:nowrap;\">" + t + "</div>";
    }

    /// <summary>Folio only (centered page number) — “Page X of Y” reads like a report, not a book.</summary>
    private static string BuildFooterTemplate()
    {
        return "<div style=\"width:100%;box-sizing:border-box;padding:0 " + InteriorLayoutTokens.RunningHeadPadSides +
               " " + InteriorLayoutTokens.FolioPadBottom + " " + InteriorLayoutTokens.RunningHeadPadInside + ";" +
               "font-family:Georgia,'Times New Roman',serif;font-size:8.5px;color:#7c7368;" +
               "letter-spacing:0.12em;text-align:center;\"><span class=\"pageNumber\"></span></div>";
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
