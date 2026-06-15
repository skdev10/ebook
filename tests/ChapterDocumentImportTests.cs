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
    public void ResolveExtension_detects_pdf_magic_bytes()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E };
        var ext = ChapterDocumentImportService.ResolveExtension("upload", null, bytes);
        Assert.Equal(".pdf", ext);
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
