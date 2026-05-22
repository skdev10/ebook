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
    byte[] BuildDocx(
        BookDetailsResponseDto details,
        string? displayTitle,
        string? displayAuthor);
}

/// <summary>Word (.docx) export for easy editing and EPUB conversion via Word / Pandoc / KDP.</summary>
public class DocxExportService : IDocxExportService
{
    public byte[] BuildDocx(
        BookDetailsResponseDto details,
        string? displayTitle,
        string? displayAuthor)
    {
        var title = (displayTitle ?? details.BookTitle ?? "Untitled").Trim();
        if (string.IsNullOrEmpty(title)) title = "Untitled";
        var author = (displayAuthor ?? details.AuthorName ?? "Author").Trim();
        if (string.IsNullOrEmpty(author)) author = "Author";

        var chapters = (details.Chapters ?? new List<ChapterDto>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Content))
            .OrderBy(c => c.ChapterNumber > 0 ? c.ChapterNumber : int.MaxValue)
            .ToList();

        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document(new Body());
            var body = mainPart.Document.Body!;

            body.Append(CreateParagraph(title, bold: true, fontSizeHalfPoints: 48, spacingAfter: 120));
            body.Append(CreateParagraph($"by {author}", italic: true, fontSizeHalfPoints: 24, spacingAfter: 360));
            body.Append(CreateParagraph("", spacingAfter: 240));

            var chapterIndex = 0;
            foreach (var ch in chapters)
            {
                chapterIndex++;
                var chTitle = (ch.Title ?? $"Chapter {chapterIndex}").Trim();
                body.Append(CreateParagraph(chTitle, bold: true, fontSizeHalfPoints: 32, spacingBefore: 360, spacingAfter: 180));

                foreach (var para in ExtractParagraphs(ch.Content))
                {
                    if (string.IsNullOrWhiteSpace(para)) continue;
                    body.Append(CreateParagraph(para, fontSizeHalfPoints: 22, spacingAfter: 120));
                }
            }

            mainPart.Document.Save();
        }

        return ms.ToArray();
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
        bool bold = false,
        bool italic = false,
        int fontSizeHalfPoints = 22,
        int spacingBefore = 0,
        int spacingAfter = 0)
    {
        var runProps = new RunProperties();
        if (bold) runProps.Append(new Bold());
        if (italic) runProps.Append(new Italic());
        runProps.Append(new FontSize { Val = fontSizeHalfPoints.ToString() });
        runProps.Append(new RunFonts { Ascii = "Georgia", HighAnsi = "Georgia" });

        var run = new Run(runProps, new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        var paraProps = new ParagraphProperties();
        if (spacingBefore > 0 || spacingAfter > 0)
        {
            paraProps.Append(new SpacingBetweenLines
            {
                Before = spacingBefore.ToString(),
                After = spacingAfter.ToString(),
                Line = "276",
                LineRule = LineSpacingRuleValues.Auto
            });
        }

        return new Paragraph(paraProps, run);
    }
}
