using System.Globalization;
using System.Net;
using EBookDashboard.Models.DTO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// Secondary PDF engine — PDFsharp with embedded fonts, 6×9 layout (fallback when Chromium unavailable).
/// </summary>
public sealed class PdfSharpBookExporter
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger _logger;

    public PdfSharpBookExporter(IWebHostEnvironment env, ILogger logger)
    {
        _env = env;
        _logger = logger;
    }

    public byte[] Render(
        BookDetailsResponseDto details,
        string? coverImageDataUrl,
        string displayTitle,
        string displayAuthor,
        BookPdfExportOptions opt,
        bool includeCoverPage)
    {
        var title = string.IsNullOrWhiteSpace(displayTitle) ? details.BookTitle ?? "Untitled" : displayTitle;
        var author = displayAuthor ?? details.AuthorName ?? "";
        var layout = ExportPdfPageLayout.ForOptions(opt);
        var faces = ExportPdfTypography.ForInterior(opt);
        var bodyPt = ExportPdfTypography.BodySizePt(opt);
        var lineHeight = ExportPdfTypography.LineHeightMultiplier(opt);
        var pageBg = opt.ResolvePageBackgroundColor();

        var fontDir = Path.Combine(_env.WebRootPath ?? "", "fonts", "pdf");
        Directory.CreateDirectory(fontDir);
        ExportPdfFontResolver.EnsureRegistered(fontDir);

        using var doc = new PdfDocument();
        doc.Info.Title = title;
        doc.Info.Author = author;

        if (includeCoverPage && !string.IsNullOrWhiteSpace(coverImageDataUrl))
            TryAddCoverPage(doc, layout, coverImageDataUrl);

        AddTitlePage(doc, layout, title, author, pageBg, faces, bodyPt);

        var phBase = BookManuscriptHtmlFormatter.CreateBaseContext(
            title, details.Subtitle, details.Description, details.Genre, author);

        var chapters = BookChapterExportHelper.OrderForExport(details.Chapters);
        var narrative = 0;
        ManuscriptHtmlPdfPainter? painter = null;

        try
        {
            foreach (var ch in chapters)
            {
                if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                    narrative++;
                var phNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 1 : narrative;
                var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
                var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
                var heading = BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum);
                var bodyHtml = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(ch.Content, ph, heading);
                if (string.IsNullOrWhiteSpace(bodyHtml)) continue;

                painter?.DisposeGfx();
                painter = new ManuscriptHtmlPdfPainter(doc, layout, faces, bodyPt, lineHeight, pageBg);
                painter.PaintChapter(heading, bodyHtml);
            }

            if (doc.PageCount == 0)
            {
                painter = new ManuscriptHtmlPdfPainter(doc, layout, faces, bodyPt, lineHeight, pageBg);
                painter.PaintChapter("Chapter", "<p class=\"manuscript-p\">No content available.</p>");
            }

            painter?.DisposeGfx();

            using var ms = new MemoryStream();
            doc.Save(ms, false);
            var bytes = ms.ToArray();
            _logger.LogInformation(
                "PdfSharp export: {Pages} pages, {Bytes} bytes, 6x9={W}x{H}pt, style={Style}, pageBg={Bg}",
                doc.PageCount, bytes.Length, layout.PageWidthPt, layout.PageHeightPt, opt.InteriorStyle, pageBg);
            return bytes;
        }
        catch
        {
            painter?.DisposeGfx();
            throw;
        }
    }

    private static void AddTitlePage(
        PdfDocument doc,
        ExportPdfPageLayout layout,
        string title,
        string author,
        string pageBg,
        ExportPdfTypography.TypefaceSet faces,
        double bodyPt)
    {
        var page = doc.AddPage();
        page.Width = XUnit.FromPoint(layout.PageWidthPt);
        page.Height = XUnit.FromPoint(layout.PageHeightPt);
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(new XSolidBrush(ParseHexColor(pageBg)), 0, 0, page.Width.Point, page.Height.Point);

        var titleFont = new XFont(faces.HeadingFamily, bodyPt + 10, XFontStyleEx.Bold);
        var metaFont = new XFont(faces.BodyFamily, bodyPt, XFontStyleEx.Regular);
        var y = layout.PageHeightPt * 0.32;
        var rect = new XRect(layout.MarginLeftPt, y, layout.ContentWidthPt, 80);
        gfx.DrawString(title, titleFont, XBrushes.Black, rect, XStringFormats.TopCenter);
        if (!string.IsNullOrWhiteSpace(author))
        {
            y += 52;
            gfx.DrawString(author, metaFont, XBrushes.DarkGray, new XRect(layout.MarginLeftPt, y, layout.ContentWidthPt, 30), XStringFormats.TopCenter);
        }
    }

    private static XColor ParseHexColor(string hex)
    {
        var h = (hex ?? "#ffffff").Trim().TrimStart('#');
        if (h.Length < 6) return XColors.White;
        return XColor.FromArgb(
            int.Parse(h[..2], NumberStyles.HexNumber),
            int.Parse(h.Substring(2, 2), NumberStyles.HexNumber),
            int.Parse(h.Substring(4, 2), NumberStyles.HexNumber));
    }

    private static void TryAddCoverPage(PdfDocument doc, ExportPdfPageLayout layout, string coverSrc)
    {
        try
        {
            XImage? image = null;
            if (coverSrc.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = coverSrc.IndexOf(',');
                if (comma > 0)
                    image = XImage.FromStream(new MemoryStream(Convert.FromBase64String(coverSrc[(comma + 1)..])));
            }
            if (image == null) return;
            var page = doc.AddPage();
            page.Width = XUnit.FromPoint(layout.PageWidthPt);
            page.Height = XUnit.FromPoint(layout.PageHeightPt);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            image.Dispose();
        }
        catch
        {
            /* optional cover */
        }
    }
}
