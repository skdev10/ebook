using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using EBookDashboard.Models.DTO;
using HtmlAgilityPack;

namespace EBookDashboard.Services;

public interface IDocxExportService
{
    /// <summary>Build a Word document using saved / preview formatting options when provided.</summary>
    byte[] BuildDocx(
        BookDetailsResponseDto details,
        string? displayTitle,
        string? displayAuthor,
        BookPdfExportOptions? formatting = null);
}

/// <summary>Word (.docx) export — typography follows InteriorExportTheme / formatter prefs when supplied.</summary>
public class DocxExportService : IDocxExportService
{
    public byte[] BuildDocx(
        BookDetailsResponseDto details,
        string? displayTitle,
        string? displayAuthor,
        BookPdfExportOptions? formatting = null)
    {
        var title = (displayTitle ?? details.BookTitle ?? "Untitled").Trim();
        if (string.IsNullOrEmpty(title)) title = "Untitled";
        var author = (displayAuthor ?? details.AuthorName ?? "Author").Trim();
        if (string.IsNullOrEmpty(author)) author = "Author";

        var opt = formatting ?? new BookPdfExportOptions();
        opt.Normalize();
        var typo = InteriorTypographyPresets.Resolve(opt);
        var bodyHalfPts = PtToHalfPoints(typo.BodyFontSizePt);
        var titleHalfPts = Math.Max(bodyHalfPts + 26, 36);
        var chapterHalfPts = Math.Max(bodyHalfPts + 10, 28);
        var authorHalfPts = Math.Max(bodyHalfPts + 2, 20);
        var lineTwips = LineHeightToTwips(typo.LineHeight);
        var fontName = FirstFontFamily(typo.BodyFontStack);

        var chapters = BookChapterExportHelper.OrderForExport(details.Chapters);

        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            body.Append(CreateParagraph(title, fontName, bold: true, fontSizeHalfPoints: titleHalfPts, spacingAfter: 120, lineTwips: lineTwips));
            body.Append(CreateParagraph($"by {author}", fontName, italic: true, fontSizeHalfPoints: authorHalfPts, spacingAfter: 360, lineTwips: lineTwips));
            body.Append(CreateParagraph("", fontName, spacingAfter: 240, lineTwips: lineTwips));

            var narrativeOrdinal = 0;
            foreach (var ch in chapters)
            {
                if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                    narrativeOrdinal++;
                var displayOrd = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber)
                    ? 1
                    : narrativeOrdinal;
                var chTitle = BookChapterExportHelper.GetExportHeading(ch.Title, ch.ChapterNumber, displayOrd).Trim();
                body.Append(CreateParagraph(chTitle, fontName, bold: true, fontSizeHalfPoints: chapterHalfPts, spacingBefore: 360, spacingAfter: 180, lineTwips: lineTwips));

                foreach (var para in ExtractParagraphs(ch.Content))
                {
                    if (string.IsNullOrWhiteSpace(para)) continue;
                    body.Append(CreateParagraph(para, fontName, fontSizeHalfPoints: bodyHalfPts, spacingAfter: 120, lineTwips: lineTwips));
                }
            }

            mainPart.Document.Save();
        }

        return ms.ToArray();
    }

    private static int PtToHalfPoints(string ptStr)
    {
        if (!double.TryParse(ptStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var pt) || pt <= 0)
            pt = 11;
        return Math.Max(16, (int)Math.Round(pt * 2));
    }

    private static string LineHeightToTwips(string lhStr)
    {
        if (!double.TryParse(lhStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var lh) || lh <= 0)
            lh = 1.6;
        // Open XML auto line spacing: 240 = single
        return Math.Max(200, (int)Math.Round(lh * 240)).ToString(CultureInfo.InvariantCulture);
    }

    private static string FirstFontFamily(string stack)
    {
        if (string.IsNullOrWhiteSpace(stack)) return "Georgia";
        var first = stack.Split(',')[0].Trim().Trim('\'', '"');
        return string.IsNullOrWhiteSpace(first) ? "Georgia" : first;
    }

    private static IEnumerable<string> ExtractParagraphs(string? content)
    {
        var s = (content ?? "").Trim();
        if (string.IsNullOrEmpty(s)) yield break;

        if (Regex.IsMatch(s, @"<\s*[a-z]", RegexOptions.IgnoreCase))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(s);
            var nodes = doc.DocumentNode.SelectNodes("//p|//h1|//h2|//h3|//li|//div");
            if (nodes != null && nodes.Count > 0)
            {
                foreach (var node in nodes)
                {
                    var text = WebUtility.HtmlDecode(node.InnerText ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                        yield return text;
                }
                yield break;
            }

            var plain = WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]+>", " ")).Trim();
            if (!string.IsNullOrWhiteSpace(plain))
            {
                foreach (var p in plain.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries))
                    yield return p.Trim();
            }
            yield break;
        }

        foreach (var p in s.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = p.Trim();
            if (!string.IsNullOrEmpty(t)) yield return t;
        }
    }

    private static Paragraph CreateParagraph(
        string text,
        string fontName,
        bool bold = false,
        bool italic = false,
        int fontSizeHalfPoints = 22,
        int spacingBefore = 0,
        int spacingAfter = 0,
        string lineTwips = "384")
    {
        var runProps = new RunProperties();
        if (bold) runProps.Append(new Bold());
        if (italic) runProps.Append(new Italic());
        runProps.Append(new FontSize { Val = fontSizeHalfPoints.ToString(CultureInfo.InvariantCulture) });
        runProps.Append(new RunFonts { Ascii = fontName, HighAnsi = fontName });

        var run = new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        var paraProps = new ParagraphProperties();
        paraProps.Append(new SpacingBetweenLines
        {
            Before = spacingBefore.ToString(CultureInfo.InvariantCulture),
            After = spacingAfter.ToString(CultureInfo.InvariantCulture),
            Line = lineTwips,
            LineRule = LineSpacingRuleValues.Auto
        });

        return new Paragraph(paraProps, run);
    }
}
