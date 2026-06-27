using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace EBookDashboard.Services;

/// <summary>Reads plain text from uploaded chapter documents for AI Writer import.</summary>
public static class ChapterDocumentImportService
{
    private static readonly string[] SupportedExtensions = [".pdf", ".txt", ".text", ".md", ".markdown", ".docx"];

    private const int MaxImportedChars = 2_000_000;

    public static bool IsSupportedExtension(string ext)
        => SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);

    public static string SupportedFormatsMessage()
        => "Supported formats: PDF, Word (.docx), plain text (.txt), and Markdown (.md).";

    /// <summary>Guess extension from filename, content type, or file signature.</summary>
    public static string ResolveExtension(string? fileName, string? contentType, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!string.IsNullOrEmpty(ext))
            return ext;

        var ct = contentType ?? string.Empty;
        if (ct.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return ".pdf";
        if (ct.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase)
            || ct.Contains("msword", StringComparison.OrdinalIgnoreCase))
            return ".docx";
        if (ct.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || ct.Contains("markdown", StringComparison.OrdinalIgnoreCase))
            return ".txt";

        if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50 && bytes[2] == 0x44 && bytes[3] == 0x46)
            return ".pdf";
        if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B)
            return ".docx";

        return string.Empty;
    }

    /// <summary>Extract plain text from uploaded bytes based on resolved extension.</summary>
    public static string ExtractText(byte[] bytes, string ext, CancellationToken cancellationToken = default)
    {
        ext = ext.ToLowerInvariant();
        if (ext == ".doc")
            throw new InvalidOperationException("Old Word .doc files are not supported. Open the file in Word and Save As .docx, then upload again.");

        if (ext == ".pdf")
            return ExtractPdfTextAsPlain(bytes, cancellationToken);
        if (ext == ".docx")
            return ExtractDocxTextAsPlain(bytes);
        if (ext is ".txt" or ".text" or ".md" or ".markdown")
            return DecodeTextFile(bytes);

        throw new InvalidOperationException(SupportedFormatsMessage());
    }

    /// <summary>Decode uploaded text/markdown bytes (UTF-8/16, BOM, common Windows encodings).</summary>
    public static string DecodeTextFile(byte[] bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (LooksLikeUtf16LeText(bytes))
            return Encoding.Unicode.GetString(bytes);

        var utf8 = Encoding.UTF8.GetString(bytes);
        if (!utf8.Contains('\uFFFD', StringComparison.Ordinal))
            return utf8;

        TryRegisterCodePages();
        try
        {
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
        catch
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Extract readable text from a PDF byte array.</summary>
    public static string ExtractPdfTextAsPlain(byte[] bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var document = PdfDocument.Open(
                new MemoryStream(bytes, writable: false),
                new ParsingOptions { UseLenientParsing = true });

            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var pageText = (page.Text ?? string.Empty).Trim();
                    if (pageText.Length > 0)
                    {
                        sb.AppendLine(pageText);
                        continue;
                    }

                    var words = page.GetWords();
                    if (words != null && words.Any())
                    {
                        sb.AppendLine(string.Join(" ", words.Select(w => w.Text)));
                        continue;
                    }

                    if (page.Letters is { Count: > 0 })
                    {
                        foreach (var letter in page.Letters)
                            sb.Append(letter.Value);
                        sb.AppendLine();
                    }
                }
                catch
                {
                    /* skip unreadable page */
                }
            }

            return sb.ToString();
        }
        catch (PdfDocumentEncryptedException)
        {
            throw new InvalidOperationException("This PDF is password-protected. Export a copy without a password or paste the text instead.");
        }
    }

    /// <summary>Extract paragraph text from a Word .docx file.</summary>
    public static string ExtractDocxTextAsPlain(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var para in body.Descendants<Paragraph>())
            {
                var line = para.InnerText?.Trim();
                if (!string.IsNullOrEmpty(line))
                    sb.AppendLine(line);
            }

            return sb.ToString();
        }
        catch (Exception ex) when (ex is FileFormatException or InvalidDataException or OpenXmlPackageException)
        {
            throw new InvalidOperationException("This Word file could not be read. Re-save it as .docx in Microsoft Word or Google Docs, then upload again.");
        }
    }

    /// <summary>Normalize imported text for JSON + preview (strip nulls, invalid Unicode, cap length).</summary>
    public static string SanitizeImportedText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var cleaned = text.Replace("\0", string.Empty, StringComparison.Ordinal);
        cleaned = RemoveInvalidXmlChars(cleaned);

        try
        {
            cleaned = cleaned.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            cleaned = new string(cleaned.Where(ch => !char.IsSurrogate(ch)).ToArray());
        }

        cleaned = cleaned.Trim();
        if (cleaned.Length > MaxImportedChars)
            cleaned = cleaned.Substring(0, MaxImportedChars) + "\n...[truncated]";
        return cleaned;
    }

    public static string SuggestBookTitleFromFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(fileName.Trim());
        if (string.IsNullOrWhiteSpace(baseName))
            return string.Empty;
        baseName = Regex.Replace(baseName.Replace('_', ' '), @"\s+", " ").Trim();
        return baseName.Length > 200 ? baseName.Substring(0, 197) + "..." : baseName;
    }

    public static (int chapterNo, string chapterTitle) SuggestChapterFromBodyText(string text)
    {
        var chapterNo = 1;
        var chapterTitle = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return (chapterNo, "Imported chapter");

        var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var hm = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (hm.Success)
            {
                chapterTitle = TruncateSuggestedTitle(hm.Groups[2].Value.Trim());
                break;
            }

            var chMatch = Regex.Match(line, @"^(?:Chapter|CHAPTER)\s+(\d+)\s*[:\.\-]?\s*(.*)$", RegexOptions.IgnoreCase);
            if (chMatch.Success)
            {
                if (int.TryParse(chMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
                    chapterNo = n;
                var rest = chMatch.Groups[2].Value.Trim();
                if (!string.IsNullOrEmpty(rest))
                    chapterTitle = TruncateSuggestedTitle(rest);
                break;
            }

            if (line.Length <= 120)
            {
                chapterTitle = TruncateSuggestedTitle(line);
                break;
            }

            break;
        }

        if (string.IsNullOrEmpty(chapterTitle))
            chapterTitle = "Imported chapter";
        return (chapterNo, chapterTitle);
    }

    /// <summary>A single chapter detected inside an imported document.</summary>
    public sealed record ImportedChapter(int ChapterNo, string Title, string Body);

    /// <summary>
    /// Split imported text into chapters by detecting headings:
    /// markdown headings (<c># ...</c>), "Chapter N[: Title]" lines, or short ALL-CAPS / Title-Case
    /// heading-like lines. Returns a single chapter when no reliable boundaries are found.
    /// </summary>
    public static List<ImportedChapter> SplitIntoChapters(string text)
    {
        var result = new List<ImportedChapter>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        // Collect indices of lines that look like a chapter heading.
        var boundaries = new List<(int lineIdx, string title)>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;

            // Markdown heading: #, ##, ### ...
            var hm = Regex.Match(line, @"^(#{1,3})\s+(.+?)\s*#*$");
            if (hm.Success)
            {
                boundaries.Add((i, TruncateSuggestedTitle(hm.Groups[2].Value.Trim())));
                continue;
            }

            // "Chapter 1", "CHAPTER 2 - The Fall", "Chapter Three: ..."
            var chMatch = Regex.Match(line, @"^(?:Chapter|CHAPTER|Part|PART|Section|SECTION)\s+([0-9]+|[IVXLC]+|[A-Za-z]+)\s*[:\.\-–—]?\s*(.*)$", RegexOptions.IgnoreCase);
            if (chMatch.Success && line.Length <= 120)
            {
                var rest = chMatch.Groups[2].Value.Trim();
                var title = string.IsNullOrEmpty(rest) ? line : $"{line}";
                boundaries.Add((i, TruncateSuggestedTitle(string.IsNullOrEmpty(rest) ? line : rest)));
                continue;
            }
        }

        // Need at least 2 boundaries to justify a multi-chapter split, otherwise treat as one chapter.
        if (boundaries.Count < 2)
        {
            var (no, title) = SuggestChapterFromBodyText(text);
            result.Add(new ImportedChapter(no, title, text.Trim()));
            return result;
        }

        // Capture any preface text before the first heading as chapter content prepended to chapter 1.
        var chapterNo = 0;
        for (var b = 0; b < boundaries.Count; b++)
        {
            var startLine = boundaries[b].lineIdx + 1;
            var endLine = b + 1 < boundaries.Count ? boundaries[b + 1].lineIdx : lines.Length;
            var bodyBuilder = new StringBuilder();
            for (var l = startLine; l < endLine; l++)
                bodyBuilder.AppendLine(lines[l]);

            var body = bodyBuilder.ToString().Trim();
            // Skip empty chapters (heading with no content).
            if (body.Length == 0)
                continue;

            chapterNo++;
            var title = boundaries[b].title;
            if (string.IsNullOrWhiteSpace(title))
                title = $"Chapter {chapterNo}";
            result.Add(new ImportedChapter(chapterNo, title, body));
        }

        if (result.Count == 0)
        {
            var (no, title) = SuggestChapterFromBodyText(text);
            result.Add(new ImportedChapter(no, title, text.Trim()));
        }

        return result;
    }

    public static string MapImportExceptionMessage(Exception ex)
    {
        if (ex is OperationCanceledException)
            return "Upload was cancelled or timed out. Try a smaller file or paste the text instead.";
        if (ex is InvalidOperationException ioe && !string.IsNullOrWhiteSpace(ioe.Message))
            return ioe.Message;
        if (ex is PdfDocumentEncryptedException)
            return "This PDF is password-protected. Remove the password or paste the text instead.";

        var msg = ex.Message ?? string.Empty;
        if (msg.Contains("password", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
            return "This PDF is password-protected. Remove the password or paste the text instead.";
        if (msg.Contains("not a PDF", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Invalid PDF", StringComparison.OrdinalIgnoreCase))
            return "This file does not look like a valid PDF. Try exporting as .txt or paste the text.";
        if (msg.Contains("OpenXml", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("zip", StringComparison.OrdinalIgnoreCase))
            return "This Word file could not be read. Re-save it as .docx and upload again.";

        return "Could not read this file. Please try uploading a different export or paste the text instead.";
    }

    private static string TruncateSuggestedTitle(string s, int max = 150)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;
        return s.Length <= max ? s : s.Substring(0, max - 3) + "...";
    }

    private static string RemoveInvalidXmlChars(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    private static bool LooksLikeUtf16LeText(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
            return false;
        var zeroCount = 0;
        var check = Math.Min(bytes.Length, 64);
        for (var i = 1; i < check; i += 2)
        {
            if (bytes[i] == 0)
                zeroCount++;
        }
        return zeroCount >= check / 4;
    }

    private static void TryRegisterCodePages()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
        catch
        {
            /* already registered */
        }
    }
}
