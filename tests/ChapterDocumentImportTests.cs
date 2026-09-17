using System.Text;
using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

public class ChapterDocumentImportTests
{
    [Fact]
    public void DecodeTextFile_reads_utf8_plain_text()
    {
        var bytes = Encoding.UTF8.GetBytes("Hello chapter import.");
        var text = ChapterDocumentImportService.DecodeTextFile(bytes);
        Assert.Equal("Hello chapter import.", text);
    }

    [Fact]
    public void DecodeTextFile_reads_utf16_le_windows_notepad_export()
    {
        var bytes = Encoding.Unicode.GetBytes("Windows Notepad text");
        var text = ChapterDocumentImportService.DecodeTextFile(bytes);
        Assert.Equal("Windows Notepad text", text);
    }

    [Fact]
    public void SanitizeImportedText_strips_null_chars()
    {
        var raw = "Line one\0Line two";
        var cleaned = ChapterDocumentImportService.SanitizeImportedText(raw);
        Assert.DoesNotContain('\0', cleaned);
        Assert.Contains("Line one", cleaned);
        Assert.Contains("Line two", cleaned);
    }

    [Fact]
    public void SuggestChapterFromBodyText_parses_chapter_heading()
    {
        var (no, title) = ChapterDocumentImportService.SuggestChapterFromBodyText(
            "Chapter 3: The Long Road\n\nBody text.");
        Assert.Equal(3, no);
        Assert.Equal("The Long Road", title);
    }

    [Fact]
    public void SanitizeImportedText_handles_unpaired_surrogates()
    {
        var raw = "Hello \uD800 world";
        var cleaned = ChapterDocumentImportService.SanitizeImportedText(raw);
        Assert.Contains("Hello", cleaned);
        Assert.DoesNotContain('\uD800', cleaned);
    }

    [Fact]
    public void ResolveSuggestedBookTitle_uses_markdown_h1_title()
    {
        var title = ChapterDocumentImportService.ResolveSuggestedBookTitle(
            null, ".md", "draft-final.md", "# The Hidden Passage\n\nChapter 1\n\nBody text.");
        Assert.Equal("The Hidden Passage", title);
    }

    [Fact]
    public void ResolveSuggestedBookTitle_never_uses_chapter_marker_as_title()
    {
        // First heading is a chapter marker, so it must fall back to the (cleaned) file name.
        var title = ChapterDocumentImportService.ResolveSuggestedBookTitle(
            null, ".txt", "my_novel.txt", "# Chapter 1\n\nBody text.");
        Assert.Equal("my novel", title);
    }

    [Fact]
    public void ResolveSuggestedBookTitle_falls_back_to_clean_file_name()
    {
        var title = ChapterDocumentImportService.ResolveSuggestedBookTitle(
            null, ".txt", "Traffics_in_Karachi.txt", "Just some opening prose with no heading at all.");
        Assert.Equal("Traffics in Karachi", title);
    }

    [Fact]
    public void ResolveExtension_detects_pdf_magic_bytes()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E };
        var ext = ChapterDocumentImportService.ResolveExtension("upload", null, bytes);
        Assert.Equal(".pdf", ext);
    }

    [Fact]
    public void FullPipeline_text_path_never_throws_generic_error()
    {
        var bytes = Encoding.UTF8.GetBytes("# My Imported Book\n\nChapter 1: The Start\n\nFirst paragraph of prose.\n\nSecond paragraph.");
        var ext = ChapterDocumentImportService.ResolveExtension("draft.txt", "text/plain", bytes);
        string text = ChapterDocumentImportService.ExtractText(bytes, ext);
        text = ChapterDocumentImportService.SanitizeImportedText(text);
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        var bookTitle = ChapterDocumentImportService.ResolveSuggestedBookTitle(bytes, ext, "draft.txt", text);
        var (no, chTitle) = ChapterDocumentImportService.SuggestChapterFromBodyText(text);

        Assert.NotEmpty(chapters);
        Assert.False(string.IsNullOrWhiteSpace(bookTitle));
        Assert.True(no >= 1);
        Assert.False(string.IsNullOrWhiteSpace(chTitle));
    }

    [Theory]
    [InlineData("8", "41", "book_20260122195446.pdf")]
    [InlineData("2", "37", "book_20251203203416.pdf")]
    public void FullPipeline_real_pdf_runs_without_generic_failure(string user, string book, string name)
    {
        var pdfPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "wwwroot", "uploads", user, "books", book, "output", name));
        if (!File.Exists(pdfPath))
            return;

        var bytes = File.ReadAllBytes(pdfPath);
        var ext = ChapterDocumentImportService.ResolveExtension(name, "application/pdf", bytes);
        Assert.Equal(".pdf", ext);

        // Mirror ImportChapterFile: surface the exact friendly message if anything throws.
        try
        {
            string text = ChapterDocumentImportService.ExtractText(bytes, ext);
            text = ChapterDocumentImportService.SanitizeImportedText(text);
            var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
            var bookTitle = ChapterDocumentImportService.ResolveSuggestedBookTitle(bytes, ext, name, text);
            var (no, chTitle) = ChapterDocumentImportService.SuggestChapterFromBodyText(
                string.IsNullOrWhiteSpace(text) ? (chapters.FirstOrDefault()?.Title ?? "Imported chapter") : text);

            Assert.True(chapters.Count > 0 || !string.IsNullOrWhiteSpace(text),
                "No readable text and no chapters — would show the scanned-PDF message.");
        }
        catch (Exception ex)
        {
            Assert.Fail($"PDF import threw -> user message would be: '{ChapterDocumentImportService.MapImportExceptionMessage(ex)}'. Raw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact]
    public void ExtractPdfText_reads_sample_book_pdf_when_present()
    {
        var pdfPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "wwwroot", "uploads", "8", "books", "41", "output", "book_20260122195446.pdf"));
        if (!File.Exists(pdfPath))
            return;

        var bytes = File.ReadAllBytes(pdfPath);
        var text = ChapterDocumentImportService.ExtractPdfTextAsPlain(bytes);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void SplitIntoChapters_splits_inline_chapter_markers_without_newlines()
    {
        // Mimics PdfPig output: almost no line breaks between chapters.
        var text = "Preface text here. Chapter 1 The Beginning Once upon a time there was a story. Chapter 2 The Middle More story continues here. Chapter 3 The End Final words.";
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.True(chapters.Count >= 3, $"Expected >= 3 chapters, got {chapters.Count}");
    }

    [Fact]
    public void NormalizeInlineChapterHeadings_inserts_breaks()
    {
        var raw = "Hello Chapter 2 World";
        var norm = ChapterDocumentImportService.NormalizeInlineChapterHeadings(raw);
        Assert.Contains("\n\nChapter 2", norm);
    }

    [Fact]
    public void SplitIntoChapters_uses_blank_line_headings_as_chapter_titles()
    {
        var text = "THE HIDDEN ROAD\n\nOnce upon a time a traveler left home.\n\nTHE RIVER CROSSING\n\nThe water was cold and fast.";
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.True(chapters.Count >= 2, $"Expected 2 heading chapters, got {chapters.Count}");
        Assert.Contains("HIDDEN", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RIVER", chapters[1].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatImportedBodyAsHtml_keeps_markdown_headings()
    {
        var html = ChapterDocumentImportService.FormatImportedBodyAsHtml("## A Quiet Town\n\nPeople lived simply there.");
        Assert.Contains("<h2", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A Quiet Town", html);
        Assert.Contains("<p", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatBodyToHtml_promotes_isolated_title_case_line()
    {
        var html = BookManuscriptHtmlFormatter.FormatBodyToHtml("A Quiet Town\n\nPeople lived simply there.");
        Assert.Contains("<h2", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A Quiet Town", html);
        Assert.Contains("People lived simply there", html);
    }

    [Fact]
    public void PreferRicherChapterSplit_keeps_structured_headings_and_images()
    {
        var structured = new List<ChapterDocumentImportService.ImportedChapter>
        {
            new(1, "Dawn", "<h2 class=\"manuscript-heading\">Dawn</h2><p>Light.</p><img src=\"x\" />")
        };
        var fallback = new List<ChapterDocumentImportService.ImportedChapter>
        {
            new(1, "Chapter 1", "Light.")
        };
        var chosen = ChapterDocumentImportService.PreferRicherChapterSplit(structured, fallback);
        Assert.Same(structured, chosen);
    }

    [Fact]
    public void SplitIntoChapters_heading_without_trailing_blank_line()
    {
        var text = "THE HIDDEN ROAD\nOnce upon a time a traveler left home.\n\nTHE RIVER CROSSING\nThe water was cold and fast.";
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.True(chapters.Count >= 2, $"Expected 2 heading chapters, got {chapters.Count}");
        Assert.Contains("HIDDEN", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RIVER", chapters[1].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traveler", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("water", chapters[1].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatImportedBodyAsHtml_promotes_title_case_paragraphs()
    {
        var html = ChapterDocumentImportService.FormatImportedBodyAsHtml("<p>A Quiet Town</p><p>People lived simply there.</p>");
        Assert.Contains("<h2", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A Quiet Town", html);
        Assert.Contains("<p", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PromoteHeadingParagraphs_converts_heading_like_paragraphs()
    {
        var html = ChapterDocumentImportService.PromoteHeadingParagraphs(
            "<p>THE MARKET SQUARE</p><p>Vendors shouted over the crowd.</p>");
        Assert.Contains("<h2", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("THE MARKET SQUARE", html);
        Assert.DoesNotContain("<p>THE MARKET SQUARE</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractDocxChapters_uses_heading1_as_chapter_title()
    {
        var bytes = BuildSimpleDocx(
            ("Heading1", "Chapter 1 The Beginning"),
            (null, "Once upon a time the story started."),
            ("Heading1", "Chapter 2 The Middle"),
            (null, "And then more things happened."));
        var chapters = ChapterDocumentImportService.ExtractDocxChapters(bytes, out _);
        Assert.True(chapters.Count >= 2, $"Expected 2 chapters, got {chapters.Count}");
        Assert.Contains("Beginning", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Middle", chapters[1].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractDocxChapters_treats_bold_centered_line_as_heading()
    {
        var bytes = BuildBoldCenteredDocx("A Hidden Door", "The wall opened onto a stair.");
        var chapters = ChapterDocumentImportService.ExtractDocxChapters(bytes, out var plain);
        Assert.NotEmpty(chapters);
        Assert.True(
            chapters[0].Title.Contains("Hidden", StringComparison.OrdinalIgnoreCase)
            || chapters[0].Body.Contains("manuscript-heading", StringComparison.OrdinalIgnoreCase)
            || plain.Contains("#"),
            "Bold centered title should become a chapter title or in-body heading.");
    }

    private static byte[] BuildSimpleDocx(params (string? StyleId, string Text)[] paragraphs)
    {
        using var ms = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
                   ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new DocumentFormat.OpenXml.Wordprocessing.Body();
            foreach (var (styleId, text) in paragraphs)
            {
                var para = new DocumentFormat.OpenXml.Wordprocessing.Paragraph();
                if (!string.IsNullOrEmpty(styleId))
                {
                    para.ParagraphProperties = new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
                        new DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId { Val = styleId });
                }
                para.Append(new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.Text(text)));
                body.Append(para);
            }
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static byte[] BuildBoldCenteredDocx(string heading, string body)
    {
        using var ms = new MemoryStream();
        using (var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
                   ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var titlePara = new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties(
                    new DocumentFormat.OpenXml.Wordprocessing.Justification { Val = DocumentFormat.OpenXml.Wordprocessing.JustificationValues.Center }),
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                        new DocumentFormat.OpenXml.Wordprocessing.Bold(),
                        new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "32" }),
                    new DocumentFormat.OpenXml.Wordprocessing.Text(heading)));
            var bodyPara = new DocumentFormat.OpenXml.Wordprocessing.Paragraph(
                new DocumentFormat.OpenXml.Wordprocessing.Run(
                    new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                        new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "22" }),
                    new DocumentFormat.OpenXml.Wordprocessing.Text(body)));
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
                new DocumentFormat.OpenXml.Wordprocessing.Body(titlePara, bodyPara));
            main.Document.Save();
        }
        return ms.ToArray();
    }
}
