using System.Globalization;
using System.Text;
using EBookDashboard.Models.DTO;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace EBookDashboard.Services;

/// <summary>
/// PdfSharp fallback when headless Chromium is unavailable — preserves interior typography and chapter order.
/// </summary>
public static class BookPdfSharpRenderer
{
    private const double MarginPt = 54;
    private const double LineGapPt = 4;
    private const double A4WidthPt = 595.28;
    private const double A4HeightPt = 841.89;

    public static byte[] RenderInteriorPdf(
        BookDetailsResponseDto details,
        string displayTitle,
        string displayAuthor,
        BookPdfExportOptions opt)
    {
        var title = string.IsNullOrWhiteSpace(displayTitle) ? details.BookTitle ?? "Untitled" : displayTitle;
        var author = displayAuthor ?? details.AuthorName ?? "";
        var interior = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);
        var bodyPt = double.Parse(InteriorExportTheme.ResolveBodyFontSizePt(interior, opt.TextSize), CultureInfo.InvariantCulture);
        var lineHeight = double.Parse(InteriorExportTheme.ResolveLineHeight(opt.LineSpacing), CultureInfo.InvariantCulture);

        using var doc = new PdfDocument();
        doc.Info.Title = title;
        doc.Info.Author = author;

        var phBase = BookManuscriptHtmlFormatter.CreateBaseContext(
            title,
            details.Subtitle,
            details.Description,
            details.Genre,
            author);

        AddTitlePage(doc, title, author, bodyPt);
        var chapters = BookChapterExportHelper.OrderForExport(details.Chapters);
        var narrative = 0;
        foreach (var ch in chapters)
        {
            if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                narrative++;
            var phNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 1 : narrative;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            var heading = BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum);
            var bodyRaw = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(ch.Content, ph, heading);
            var plain = HtmlToPlain(bodyRaw);
            if (string.IsNullOrWhiteSpace(plain)) continue;
            AddChapterPages(doc, heading, plain, bodyPt, lineHeight, interior);
        }

        if (doc.PageCount == 0)
            AddChapterPages(doc, "Chapter", "No content available.", bodyPt, lineHeight, interior);

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private static void AddTitlePage(PdfDocument doc, string title, string author, double bodyPt)
    {
        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);
        var titleFont = new XFont("Georgia", bodyPt + 8, XFontStyleEx.Bold);
        var metaFont = new XFont("Georgia", bodyPt, XFontStyleEx.Regular);
        var pageWidth = page.Width.Point;
        var y = MarginPt + 80;
        gfx.DrawString(title, titleFont, XBrushes.Black, new XRect(MarginPt, y, pageWidth - MarginPt * 2, 40), XStringFormats.TopCenter);
        if (!string.IsNullOrWhiteSpace(author))
        {
            y += 48;
            gfx.DrawString(author, metaFont, XBrushes.DarkGray, new XRect(MarginPt, y, pageWidth - MarginPt * 2, 24), XStringFormats.TopCenter);
        }
    }

    private static void AddChapterPages(
        PdfDocument doc,
        string heading,
        string body,
        double bodyPt,
        double lineHeight,
        string interior)
    {
        var headingFont = new XFont(HeadingFace(interior), bodyPt + 2, HeadingStyle(interior));
        var bodyFont = new XFont(BodyFace(interior), bodyPt, XFontStyleEx.Regular);
        var allLines = new List<string>();
        foreach (var para in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var t = para.Trim();
            if (t.Length == 0) continue;
            allLines.AddRange(WrapText(t, bodyFont, A4WidthPt - MarginPt * 2));
            allLines.Add("");
        }

        var page = doc.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);
        var maxWidth = page.Width.Point - MarginPt * 2;
        var y = MarginPt;
        var headingDrawn = false;

        try
        {
            foreach (var line in allLines)
            {
                if (!headingDrawn)
                {
                    gfx.DrawString(heading, headingFont, XBrushes.Black, new XRect(MarginPt, y, maxWidth, 36), XStringFormats.TopCenter);
                    y += 36 + LineGapPt * 2;
                    headingDrawn = true;
                }

                if (string.IsNullOrEmpty(line))
                {
                    y += LineGapPt;
                    continue;
                }

                if (y > page.Height.Point - MarginPt - bodyPt * lineHeight)
                {
                    gfx.Dispose();
                    page = doc.AddPage();
                    page.Size = PdfSharp.PageSize.A4;
                    gfx = XGraphics.FromPdfPage(page);
                    y = MarginPt;
                }

                gfx.DrawString(line, bodyFont, XBrushes.Black, new XRect(MarginPt, y, maxWidth, bodyPt * lineHeight), XStringFormats.TopLeft);
                y += bodyPt * lineHeight;
            }
        }
        finally
        {
            gfx.Dispose();
        }
    }

    private static List<string> WrapText(string text, XFont font, double maxWidth)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(maxWidth, 1000), XGraphicsUnit.Point, XPageDirection.Downwards);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var w in words)
        {
            var trial = current.Length == 0 ? w : current + " " + w;
            if (measure.MeasureString(trial, font).Width > maxWidth && current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(w);
            }
            else
                current.Append(current.Length == 0 ? w : " " + w);
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    private static string HtmlToPlain(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);
            return HtmlAgilityPack.HtmlEntity.DeEntitize(doc.DocumentNode.InnerText ?? "")
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Trim();
        }
        catch
        {
            return html;
        }
    }

    private static string BodyFace(string interior) => interior switch
    {
        "Modern" or "Minimalist" => "Segoe UI",
        "Classic" => "Palatino Linotype",
        _ => "Georgia"
    };

    private static string HeadingFace(string interior) => interior switch
    {
        "Classic" => "Palatino Linotype",
        "Modern" or "Minimalist" => "Segoe UI",
        _ => "Georgia"
    };

    private static XFontStyleEx HeadingStyle(string interior) =>
        interior is "Classic" ? XFontStyleEx.Italic : XFontStyleEx.Bold;
}
