using System.Globalization;
using System.Text.Json;
using EBookDashboard.Models;
using EBookDashboard.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

/// <summary>
/// Tracks manuscript upload versions per book (Settings JSON + timestamped files on disk).
/// Keeps the last <see cref="MaxVersions"/> uploads; marks the newest as active.
/// </summary>
public static class ManuscriptVersionStore
{
    public const int MaxVersions = 8;
    public const string SettingsKeySuffix = "manuscriptVersions";

    public static string SettingsKey(int bookId) => $"book:{bookId}:{SettingsKeySuffix}";

    public static async Task<List<ManuscriptVersionItem>> LoadAsync(
        ApplicationDbContext db, int bookId, CancellationToken cancellationToken = default)
    {
        var key = SettingsKey(bookId);
        var raw = await db.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return new List<ManuscriptVersionItem>();
        try
        {
            var list = JsonSerializer.Deserialize<List<ManuscriptVersionItem>>(raw)
                       ?? new List<ManuscriptVersionItem>();
            return list.OrderByDescending(v => v.UploadedAtUtc).ToList();
        }
        catch
        {
            return new List<ManuscriptVersionItem>();
        }
    }

    public static async Task<List<ManuscriptVersionItem>> RecordAsync(
        ApplicationDbContext db,
        int bookId,
        string relativePath,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var key = SettingsKey(bookId);
        var list = await LoadAsync(db, bookId, cancellationToken);
        foreach (var v in list) v.Active = false;

        list.Insert(0, new ManuscriptVersionItem
        {
            Path = relativePath,
            FileName = fileName,
            UploadedAtUtc = DateTime.UtcNow,
            Active = true
        });

        if (list.Count > MaxVersions)
            list = list.Take(MaxVersions).ToList();

        // Keep JSON within Settings.Value VARCHAR(1000) compat limit when LONGTEXT migration not applied.
        string json;
        while (true)
        {
            json = JsonSerializer.Serialize(list);
            if (json.Length <= Settings.DbCompatMaxValueLength || list.Count <= 1)
                break;
            list = list.Take(list.Count - 1).ToList();
        }
        json = Settings.ClampValueLength(json, Settings.DbCompatMaxValueLength) ?? json;

        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row == null)
        {
            db.Settings.Add(new Settings
            {
                SettingId = await db.NextSettingIdAsync(cancellationToken),
                Key = key,
                Value = json,
                Category = "Book",
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            row.Value = json;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return list;
    }

    /// <summary>Build TOC + pages from chapter list (word-count estimate ≈ 250 words/page @ 6×9).</summary>
    public static (List<FormattingChapterItem> Chapters, List<FormattingTocItem> Toc, List<FormattingPageItem> Pages)
        BuildStructure(IEnumerable<(int No, string Title, string Body)> source)
    {
        var chapters = new List<FormattingChapterItem>();
        var toc = new List<FormattingTocItem>();
        var pages = new List<FormattingPageItem>();
        var page = 1;

        foreach (var (no, title, body) in source)
        {
            var plain = StripTags(body);
            var words = CountWords(plain);
            var pageCount = Math.Max(1, (int)Math.Ceiling(words / 250.0));
            var matter = ClassifyMatter(title, no);
            var safeTitle = string.IsNullOrWhiteSpace(title) ? $"Chapter {no}" : title.Trim();

            chapters.Add(new FormattingChapterItem
            {
                ChapterNo = no,
                Title = safeTitle,
                Matter = matter,
                ContentHtml = body ?? "",
                WordCount = words,
                StartPage = page,
                PageCount = pageCount
            });
            toc.Add(new FormattingTocItem
            {
                ChapterNo = no,
                Title = safeTitle,
                StartPage = page,
                Matter = matter
            });
            for (var p = 0; p < pageCount; p++)
            {
                pages.Add(new FormattingPageItem
                {
                    PageNumber = page + p,
                    ChapterNo = no,
                    Label = p == 0 ? safeTitle : $"… {safeTitle}"
                });
            }

            page += pageCount;
        }

        return (chapters, toc, pages);
    }

    private static string ClassifyMatter(string title, int chapterNo)
    {
        var t = (title ?? "").Trim().ToLowerInvariant();
        if (t.Contains("copyright") || t.Contains("dedication") || t.Contains("acknowledg")
            || t.Contains("foreword") || t.Contains("preface") || t.Contains("introduction")
            || t.Contains("table of contents") || t == "toc")
            return "front";
        if (t.Contains("about the author") || t.Contains("bibliography") || t.Contains("index")
            || t.Contains("appendix") || t.Contains("epilogue") || t.Contains("afterword"))
            return "back";
        return "body";
    }

    private static string StripTags(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    public static string FormatUploadedLabel(DateTime? utc)
    {
        if (utc == null) return "";
        return utc.Value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
    }
}
