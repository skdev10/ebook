using System.Text;
using System.Text.RegularExpressions;
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
        Assert.Contains("Preface", chapters[0].Title + chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(chapters, c => c.Title.Contains("Beginning", StringComparison.OrdinalIgnoreCase)
                                      || c.Body.Contains("Once upon a time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RepairPdfImportText_joins_hyphen_space_and_drop_caps()
    {
        Assert.Equal("billion-dollar", ChapterDocumentImportService.RepairPdfImportText("billion- dollar"));
        Assert.Equal("co-founder", ChapterDocumentImportService.RepairPdfImportText("co- founder"));
        Assert.Equal("START WITH A THOUGHT", ChapterDocumentImportService.RepairPdfImportText("S TART WITH A THOUGHT"));
        Assert.Equal("AS MATURE INDUSTRIES", ChapterDocumentImportService.RepairPdfImportText("A S MATURE INDUSTRIES"));
        Assert.Equal("EVERY GREAT COMPANY", ChapterDocumentImportService.RepairPdfImportText("E VERY GREAT COMPANY"));
    }

    [Fact]
    public void SplitIntoChapters_keeps_numbered_allcaps_chapter_banners()
    {
        var text = """
            Preface

            This book opens with a note to the reader.

            1 THE CHALLENGE OF THE FUTURE
            The first chapter explains the problem.

            10 THE MECHANICS OF MAFIA
            Start with a thought experiment.
            """;
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.True(chapters.Count >= 3, $"Expected preface + 2 numbered chapters, got {chapters.Count}");
        Assert.Contains("Preface", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHALLENGE", chapters[1].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first chapter", chapters[1].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MECHANICS", chapters[2].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SplitIntoChapters_keeps_preface_and_chapter_one()
    {
        var text = """
            Preface

            This book opens with a note to the reader about the diagrams inside.

            Chapter 1: Getting Started

            The first chapter explains the problem and the method.

            Chapter 2: Next Steps

            The second chapter continues the argument.
            """;
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.True(chapters.Count >= 3, $"Expected preface + 2 chapters, got {chapters.Count}");
        Assert.Contains("Preface", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagrams", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Getting Started", chapters[1].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("first chapter", chapters[1].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Next Steps", chapters[2].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SplitHtmlDocumentIntoChapters_keeps_headings_and_figures()
    {
        var html =
            "<h1 class=\"manuscript-heading manuscript-h1\">Preface</h1>" +
            "<p class=\"manuscript-p\">Opening note.</p>" +
            "<p class=\"manuscript-figure\"><img src=\"data:image/png;base64,aaa\" alt=\"\" /></p>" +
            "<h1 class=\"manuscript-heading manuscript-h1\">Chapter 1: The Start</h1>" +
            "<h2 class=\"manuscript-heading manuscript-h2\">A Quiet Town</h2>" +
            "<p class=\"manuscript-p\">People lived simply there.</p>";
        var chapters = ChapterDocumentImportService.SplitHtmlDocumentIntoChapters(html);
        Assert.True(chapters.Count >= 2, $"Expected preface + chapter 1, got {chapters.Count}");
        Assert.Contains("Preface", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<img", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Start", chapters[1].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("A Quiet Town", chapters[1].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manuscript-heading", chapters[1].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreferRicherChapterSplit_keeps_images_over_text_only_split()
    {
        var withImages = new List<ChapterDocumentImportService.ImportedChapter>
        {
            new(1, "Preface", "<p>Hi</p><img src=\"data:image/png;base64,x\" />"),
            new(2, "Chapter 1", "<p>Body</p>")
        };
        var textOnly = new List<ChapterDocumentImportService.ImportedChapter>
        {
            new(1, "Chapter 1", "Hi"),
            new(2, "Chapter 2", "Body"),
            new(3, "Chapter 3", "More")
        };
        var chosen = ChapterDocumentImportService.PreferRicherChapterSplit(withImages, textOnly);
        Assert.Same(withImages, chosen);
    }

    [Fact]
    public void NormalizeInlineChapterHeadings_inserts_breaks()
    {
        var raw = "Hello Chapter 2 World";
        var norm = ChapterDocumentImportService.NormalizeInlineChapterHeadings(raw);
        Assert.Contains("\n\nChapter 2", norm);
    }

    [Fact]
    public void SplitIntoChapters_keeps_section_points_inside_one_chapter()
    {
        var text = "THE HIDDEN ROAD\n\nOnce upon a time a traveler left home.\n\nTHE RIVER CROSSING\n\nThe water was cold and fast.";
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.Single(chapters);
        Assert.Contains("HIDDEN", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<h2 class=\"manuscript-heading manuscript-h2\">THE HIDDEN ROAD</h2>", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RIVER", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manuscript-heading", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
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
    public void SplitIntoChapters_heading_without_trailing_blank_line_stays_in_body()
    {
        var text = "THE HIDDEN ROAD\nOnce upon a time a traveler left home.\n\nTHE RIVER CROSSING\nThe water was cold and fast.";
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.Single(chapters);
        // First banner becomes the chapter title (shown as opener) — must not also lead the body.
        Assert.Contains("HIDDEN", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<h2 class=\"manuscript-heading manuscript-h2\">THE HIDDEN ROAD</h2>", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RIVER", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("traveler", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("water", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SplitIntoChapters_does_not_duplicate_chapter_title_in_body()
    {
        var text = """
            Chapter 1: Disadvantages of Technology

            Technology also creates new problems for families and schools.
            """;
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.Single(chapters);
        Assert.Contains("Disadvantages", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("families", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        // Chapter opener owns the title — body must not start with the same <h2>.
        var body = chapters[0].Body ?? "";
        Assert.False(
            Regex.IsMatch(body, @"^\s*<h[1-6][^>]*>\s*Disadvantages of Technology\s*</h[1-6]>", RegexOptions.IgnoreCase),
            "Body must not reopen with the chapter title heading.");
    }

    [Fact]
    public void SplitIntoChapters_treats_named_chapter_two_as_its_own_chapter()
    {
        var chapter1 = new string('x', 320);
        var text = $"""
            Chapter 1
            {chapter1} first chapter continues with real paragraphs.

            Chapter 2: Disadvantages of Technology
            Technology also creates new problems for families and schools.
            """;
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.True(chapters.Count >= 2, $"Expected Chapter 2 split, got {chapters.Count}");
        Assert.Contains("Disadvantages", chapters[1].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("families", chapters[1].Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Disadvantages of Technology", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SplitIntoChapters_treats_plain_chapter_title_after_body_as_chapter_two()
    {
        var chapter1 = "People gained speed and convenience from modern tools. " + new string('a', 260);
        var text = $"""
            Chapter 1
            {chapter1}

            Disadvantages of Technology
            The same tools can isolate people from each other.
            """;
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.True(chapters.Count >= 2, $"Expected named Chapter 2, got {chapters.Count}");
        Assert.Contains("Disadvantages", chapters[1].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("isolate", chapters[1].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SplitIntoChapters_empty_manuscript_returns_no_chapters()
    {
        Assert.Empty(ChapterDocumentImportService.SplitIntoChapters(""));
        Assert.Empty(ChapterDocumentImportService.SplitIntoChapters("   \n\n  "));
    }

    [Fact]
    public void SplitIntoChapters_single_chapter_without_second_title_stays_one()
    {
        var chapters = ChapterDocumentImportService.SplitIntoChapters(
            "Chapter 1 Opening\n\nOnly one chapter lives in this short book.");
        Assert.Single(chapters);
        Assert.Contains("Opening", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Only one chapter", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SplitIntoChapters_keeps_numbered_points_in_first_chapter()
    {
        var text = """
            Chapter 1 Getting Started

            1. What this book is
            This chapter explains the idea.

            2. How to use the points
            Read each point, then move on.

            3. Next steps
            Keep the same chapter open.
            """;
        var chapters = ChapterDocumentImportService.SplitIntoChapters(text);
        Assert.Single(chapters);
        Assert.Contains("Getting Started", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What this book is", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("How to use the points", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manuscript-heading", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractDocxChapters_keeps_heading2_points_inside_chapter()
    {
        var bytes = BuildSimpleDocx(
            ("Heading1", "Chapter 1 Getting Started"),
            (null, "This chapter has three points."),
            ("Heading2", "1. What this book is"),
            (null, "The idea is simple."),
            ("Heading2", "2. How to use the points"),
            (null, "Read them in order."),
            ("Heading2", "3. Next steps"),
            (null, "Stay in this chapter."));
        var chapters = ChapterDocumentImportService.ExtractDocxChapters(bytes, out _);
        Assert.Single(chapters);
        Assert.Contains("Getting Started", chapters[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("manuscript-heading", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What this book is", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Next steps", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public void CountPdfPages_returns_zero_for_non_pdf()
    {
        Assert.Equal(0, ChapterDocumentImportService.CountPdfPages(Encoding.UTF8.GetBytes("not a pdf")));
        Assert.Equal(0, ChapterDocumentImportService.CountPdfPages(Array.Empty<byte>()));
    }

    [Fact]
    public void ShrinkImportedImage_keeps_tiny_buffer()
    {
        var tiny = new byte[] { 1, 2, 3 };
        var ct = "image/png";
        var outBytes = ChapterDocumentImportService.ShrinkImportedImage(tiny, ref ct);
        Assert.Equal(tiny, outBytes);
    }

    [Fact]
    public void MaterializeDataUriImages_rewrites_data_uri_to_file()
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "ebook-fig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(webRoot);
        try
        {
            var payload = new byte[600];
            new Random(1).NextBytes(payload);
            var html = "<p class=\"manuscript-figure\"><img src=\"data:image/jpeg;base64,"
                       + Convert.ToBase64String(payload) + "\" alt=\"\" /></p><p>Body</p>";
            var chapters = new List<ChapterDocumentImportService.ImportedChapter>
            {
                new(1, "One", html)
            };
            var n = ChapterDocumentImportService.MaterializeDataUriImages(chapters, webRoot, "uploads/figs");
            Assert.Equal(1, n);
            Assert.DoesNotContain("data:image", chapters[0].Body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/uploads/figs/fig-0001.jpg", chapters[0].Body, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(webRoot, "uploads", "figs", "fig-0001.jpg")));
        }
        finally
        {
            try { Directory.Delete(webRoot, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void ImportUploadedDocument_ninety_page_pdf_keeps_page_count_and_chapters()
    {
        var bytes = BuildLongTestPdf(90);
        Assert.Equal(90, ChapterDocumentImportService.CountPdfPages(bytes));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (text, chapters) = ChapterDocumentImportService.ImportUploadedDocument(bytes, ".pdf");
        sw.Stop();

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.True(chapters.Count >= 1, "Expected at least one imported chapter");
        Assert.Contains("Page 1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Page 90", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(45), $"Import hung: {sw.Elapsed}");
    }

    private static byte[] BuildLongTestPdf(int pageCount)
    {
        var bodies = new List<string>(pageCount);
        for (var i = 1; i <= pageCount; i++)
        {
            string heading;
            if (i == 1) heading = "Preface";
            else if ((i - 2) % 10 == 0) heading = "Chapter " + (((i - 2) / 10) + 1);
            else heading = "";
            var body = "Page " + i + " of the uploaded book. " + string.Join(" ", Enumerable.Repeat("story", 30));
            var stream = "BT /F1 16 Tf 48 560 Td (" + PdfLiteral(heading) + ") Tj T* /F1 11 Tf (" + PdfLiteral(body) + ") Tj ET\n";
            bodies.Add(stream);
        }

        var pageObj = new int[pageCount];
        var streamObj = new int[pageCount];
        var next = 4;
        for (var i = 0; i < pageCount; i++)
        {
            pageObj[i] = next++;
            streamObj[i] = next++;
        }

        var chunks = new List<string>
        {
            "%PDF-1.4\n",
            "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n",
            "2 0 obj << /Type /Pages /Count " + pageCount + " /Kids [" +
                string.Join(" ", pageObj.Select(n => n + " 0 R")) + "] >> endobj\n",
            "3 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> endobj\n"
        };
        for (var i = 0; i < pageCount; i++)
        {
            chunks.Add(pageObj[i] + " 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 432 648] /Contents "
                       + streamObj[i] + " 0 R /Resources << /Font << /F1 3 0 R >> >> >> endobj\n");
            var stream = bodies[i];
            chunks.Add(streamObj[i] + " 0 obj << /Length " + stream.Length + " >> stream\n" + stream + "endstream endobj\n");
        }

        var pos = 0;
        var offsets = new int[next];
        for (var i = 0; i < chunks.Count; i++)
        {
            if (i > 0)
                offsets[i] = pos;
            pos += Encoding.ASCII.GetByteCount(chunks[i]);
        }

        var xref = new StringBuilder();
        xref.Append("xref\n0 ").Append(next).Append('\n');
        xref.Append("0000000000 65535 f \n");
        for (var i = 1; i < next; i++)
            xref.Append(offsets[i].ToString("D10")).Append(" 00000 n \n");

        var bodyBytes = Encoding.ASCII.GetBytes(string.Concat(chunks));
        var trailer = "trailer << /Size " + next + " /Root 1 0 R >>\nstartxref\n" + bodyBytes.Length + "\n%%EOF\n";
        var tail = Encoding.ASCII.GetBytes(xref + trailer);
        var pdf = new byte[bodyBytes.Length + tail.Length];
        Buffer.BlockCopy(bodyBytes, 0, pdf, 0, bodyBytes.Length);
        Buffer.BlockCopy(tail, 0, pdf, bodyBytes.Length, tail.Length);
        return pdf;
    }

    private static string PdfLiteral(string text) =>
        (text ?? "").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}

public class ManuscriptEmptyBlockTests
{
    [Fact]
    public void StripEmptyHtmlBlocks_removes_blank_paragraphs_and_nbsp()
    {
        var html = "<p class=\"manuscript-p\">Hello</p><p></p><p>&nbsp;</p><p><br/></p><h2>  </h2><p>World</p>";
        var cleaned = BookManuscriptHtmlFormatter.StripEmptyHtmlBlocks(html);
        Assert.Contains("Hello", cleaned);
        Assert.Contains("World", cleaned);
        Assert.DoesNotContain("<p></p>", cleaned);
        Assert.DoesNotContain("&nbsp;", cleaned);
        Assert.DoesNotContain("<h2>", cleaned);
    }
}

