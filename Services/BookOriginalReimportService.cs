using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

public sealed class FlattenedBookRow
{
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public string ManuscriptPath { get; set; } = "";
}

public sealed class FlattenedImportScan
{
    public int BooksWithOriginal { get; set; }
    public int Affected { get; set; }
    public List<FlattenedBookRow> Books { get; set; } = new();
}

/// <summary>
/// Rebuilds a book's chapters from the stored original upload through the current importer.
/// </summary>
public sealed class BookOriginalReimportService
{
    private static readonly Regex InlineTag = new(
        @"<(em|i|b|strong|sup|sub|span)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IBookService _books;
    private readonly IChapterIterationService _chapters;

    public BookOriginalReimportService(
        ApplicationDbContext db,
        IWebHostEnvironment env,
        IBookService books,
        IChapterIterationService chapters)
    {
        _db = db;
        _env = env;
        _books = books;
        _chapters = chapters;
    }

    /// <summary>
    /// Books whose original file has inline formatting and whose saved chapter HTML has none.
    /// HTML saved before the import fix is flattened and needs a re-import.
    /// </summary>
    public async Task<FlattenedImportScan> ScanAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.Books.AsNoTracking()
            .Where(b => b.ManuscriptPath != null && b.ManuscriptPath != "")
            .Select(b => new { b.BookId, b.Title, b.ManuscriptPath, b.BookContentHtml })
            .ToListAsync(cancellationToken);

        var scan = new FlattenedImportScan { BooksWithOriginal = rows.Count };
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ToPhysical(row.ManuscriptPath);
            if (path == null || !File.Exists(path))
                continue;
            if (!SourceHasInlineMarkup(path))
                continue;

            var saved = row.BookContentHtml ?? "";
            if (!InlineTag.IsMatch(saved))
            {
                var bodies = await _db.Chapters.AsNoTracking()
                    .Where(c => c.BookId == row.BookId)
                    .Select(c => c.Content)
                    .ToListAsync(cancellationToken);
                saved = string.Concat(bodies);
            }

            if (InlineTag.IsMatch(saved))
                continue;

            scan.Affected++;
            scan.Books.Add(new FlattenedBookRow
            {
                BookId = row.BookId,
                Title = row.Title ?? "",
                ManuscriptPath = row.ManuscriptPath ?? ""
            });
        }

        return scan;
    }

    /// <summary>Replace saved chapters with a fresh import of <see cref="Books.ManuscriptPath"/>.</summary>
    public async Task<(bool Ok, string Message, int Chapters)> ReimportAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var book = await _db.Books.FirstOrDefaultAsync(b => b.BookId == bookId, cancellationToken);
        if (book == null)
            return (false, "Book not found.", 0);
        var path = ToPhysical(book.ManuscriptPath);
        if (path == null || !File.Exists(path))
            return (false, "No original upload is stored for this book.", 0);

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ChapterDocumentImportService.IsHeicUpload(path, null))
            return (false, ChapterDocumentImportService.HeicUploadMessage, 0);

        var (_, split) = ChapterDocumentImportService.ImportUploadedDocument(bytes, ext, cancellationToken);
        if (split.Count == 0)
            return (false, "The original file did not produce any chapters.", 0);

        var author = DocumentAuthorResolver.Resolve(bytes, ext);
        if (!string.IsNullOrWhiteSpace(author))
            await UpsertSettingAsync($"book:{bookId}:documentAuthor", author, cancellationToken);

        var existing = await _db.APIRawResponse.AsNoTracking()
            .Where(r => r.UserId == book.UserId && r.BookId == bookId)
            .Select(r => r.Chapter)
            .Distinct()
            .ToListAsync(cancellationToken);
        var tableNos = await _db.Chapters.AsNoTracking()
            .Where(c => c.BookId == bookId)
            .Select(c => c.ChapterNumber)
            .ToListAsync(cancellationToken);
        foreach (var no in existing.Concat(tableNos).Distinct().Where(n => n > 0).OrderByDescending(n => n))
            await _books.DeleteWriterChapterAsync(book.UserId, bookId, no, cancellationToken);

        var batch = new List<(int ChapterNo, string Title, string Body)>();
        var chapterNo = 0;
        foreach (var sc in split)
        {
            var title = string.IsNullOrWhiteSpace(sc.Title) ? null : sc.Title.Trim();
            if (InteriorFrontMatterBuilder.IsImportedContentsChapter(title))
                continue;
            if (BookChapterExportHelper.IsFrontMatterSectionTitle(title))
            {
                title ??= "Preface";
                var existingFm = await _db.Chapters.FirstOrDefaultAsync(
                    c => c.BookId == bookId && c.ChapterNumber == 0 && c.Title == title,
                    cancellationToken);
                if (existingFm != null)
                {
                    existingFm.Content = sc.Body;
                    existingFm.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _db.Chapters.Add(new Chapters
                    {
                        BookId = bookId,
                        ChapterNumber = 0,
                        Title = title,
                        Content = sc.Body,
                        Status = "Notes",
                        LanguageId = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                continue;
            }

            chapterNo++;
            batch.Add((chapterNo, title ?? $"Chapter {chapterNo}", sc.Body ?? ""));
        }

        await _db.SaveChangesAsync(cancellationToken);
        var saved = await _chapters.PersistImportedChaptersBulkAsync(book.UserId, bookId, batch, cancellationToken);
        book.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return (true, "Re-imported from the original upload.", saved.Count + (split.Count - batch.Count));
    }

    /// <summary>True when the upload contains inline formatting the saved HTML no longer has.</summary>
    public static bool SavedHtmlDroppedInlineTags(string? sourceMarkup, string? savedHtml)
        => InlineTag.IsMatch(sourceMarkup ?? "") && !InlineTag.IsMatch(savedHtml ?? "");

    private string? ToPhysical(string? manuscriptPath)
    {
        if (string.IsNullOrWhiteSpace(manuscriptPath))
            return null;
        var rel = manuscriptPath.Trim().TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var underWeb = Path.Combine(_env.WebRootPath ?? "", rel);
        if (File.Exists(underWeb))
            return underWeb;
        var underContent = Path.Combine(_env.ContentRootPath ?? "", rel);
        return File.Exists(underContent) ? underContent : underWeb;
    }

    private static bool SourceHasInlineMarkup(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".html" or ".htm" or ".xhtml")
            return InlineTag.IsMatch(File.ReadAllText(path));
        if (ext == ".docx")
            return ZipContains(path, "word/document.xml", xml =>
                xml.Contains("<w:i", StringComparison.OrdinalIgnoreCase)
                || xml.Contains("<w:b", StringComparison.OrdinalIgnoreCase)
                || xml.Contains("superscript", StringComparison.OrdinalIgnoreCase));
        if (ext == ".epub")
            return ZipContains(path, null, text => InlineTag.IsMatch(text));
        return false;
    }

    private static bool ZipContains(string path, string? entrySuffix, Func<string, bool> match)
    {
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries)
        {
            if (entry.Length <= 0 || entry.Length > 8_000_000)
                continue;
            var name = entry.FullName.Replace('\\', '/');
            if (entrySuffix != null && !name.EndsWith(entrySuffix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (entrySuffix == null && !name.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
                continue;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();
            if (match(text))
                return true;
        }

        return false;
    }

    private async Task UpsertSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        var row = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row == null)
        {
            _db.Settings.Add(new Settings
            {
                Key = key,
                Value = value,
                Category = "Book",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTime.UtcNow;
        }
    }
}
