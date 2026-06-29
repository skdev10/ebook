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
}
