using System.Text.RegularExpressions;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>
/// Consistent chapter ordering and headings for EPUB, Word, and PDF export (no "Chapter 0" for narrative).
/// </summary>
public static class BookChapterExportHelper
{
    private static readonly Regex ChapterZeroOnlyRegex = new(
        @"^\s*Chapter\s*0\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Exact front-matter titles (no prefix match — avoids treating "Introduction to …" as front matter).
    /// </summary>
    private static readonly HashSet<string> FrontMatterExactTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        "preface", "foreword", "dedication", "epigraph",
        "acknowledgments", "acknowledgements",
        "introduction", "prologue",
        "copyright", "copyright page", "copyright page content",
        "table of contents", "contents", "toc",
        "title page", "half title",
        "proofreading notes", "editing notes", "ghostwriting notes",
        "other back matter"
    };

    /// <summary>Titles that may carry a subtitle after a colon/dash (e.g. "Preface: Why this book").</summary>
    private static readonly string[] FrontMatterPrefixTitles =
    [
        "preface", "foreword", "dedication", "epigraph",
        "acknowledgments", "acknowledgements", "prologue"
    ];

    public static bool IsFrontMatter(int chapterNumber) => chapterNumber <= 0;

    /// <summary>
    /// Front matter by storage number <b>or</b> known section title (imported "Preface" at chapter 1+).
    /// </summary>
    public static bool IsFrontMatter(int chapterNumber, string? title) =>
        chapterNumber <= 0 || IsFrontMatterSectionTitle(title);

    /// <summary>True when the title is a known front-matter section name (Preface, Foreword, …).</summary>
    public static bool IsFrontMatterSectionTitle(string? title)
    {
        var key = NormalizeFrontMatterTitleKey(title);
        if (string.IsNullOrEmpty(key)) return false;
        if (FrontMatterExactTitles.Contains(key)) return true;

        foreach (var prefix in FrontMatterPrefixTitles)
        {
            if (key.Length > prefix.Length
                && key.StartsWith(prefix, StringComparison.Ordinal)
                && (key[prefix.Length] is ' ' or ':' or '-' or '\u2013' or '\u2014'))
                return true;
        }

        return false;
    }

    /// <summary>Normalize a title for front-matter identity / dedupe (lowercase, strip chapter prefix).</summary>
    public static string NormalizeFrontMatterTitleKey(string? title)
    {
        var t = (title ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(t)) return "";
        t = Regex.Replace(t, @"^(?:chapter|ch\.?)\s*[0-9ivxlcdm]+\s*[:.\-\u2013\u2014]?\s*", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t;
    }

    /// <summary>
    /// Keep one Preface/Foreword/… per title; coerce those rows to chapter 0 so TOC/export never treat them as Chapter N.
    /// </summary>
    public static List<ChapterDto> DeduplicateFrontMatterChapters(IEnumerable<ChapterDto>? chapters)
    {
        var list = (chapters ?? Enumerable.Empty<ChapterDto>()).ToList();
        if (list.Count == 0) return list;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ChapterDto>(list.Count);

        // Prefer chapter ≤ 0 rows first so Book Creation Notes win over imported duplicates.
        foreach (var ch in list
                     .OrderBy(c => IsFrontMatterSectionTitle(c.Title) ? 0 : 1)
                     .ThenBy(c => c.ChapterNumber)
                     .ThenBy(c => c.Title ?? "", StringComparer.OrdinalIgnoreCase))
        {
            if (!IsFrontMatterSectionTitle(ch.Title))
            {
                result.Add(ch);
                continue;
            }

            var key = NormalizeFrontMatterTitleKey(ch.Title);
            if (string.IsNullOrEmpty(key) || !seen.Add(key))
                continue;

            result.Add(new ChapterDto
            {
                ResponseId = ch.ResponseId,
                ChapterNumber = 0,
                Title = string.IsNullOrWhiteSpace(ch.Title) ? "Preface" : ch.Title.Trim(),
                Content = ch.Content,
                StatusCode = ch.StatusCode,
                CreatedAt = ch.CreatedAt,
                RequestData = ch.RequestData
            });
        }

        return result
            .OrderBy(c => IsFrontMatter(c.ChapterNumber, c.Title) ? 0 : 1)
            .ThenBy(c => c.ChapterNumber)
            .ThenBy(c => c.Title ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<ChapterDto> OrderForExport(IEnumerable<ChapterDto>? chapters) =>
        DeduplicateFrontMatterChapters(
                (chapters ?? Enumerable.Empty<ChapterDto>())
                    .Where(c => !string.IsNullOrWhiteSpace(c.Content)))
            .ToList();

    /// <summary>
    /// Chapter title for formatter preview and PDF/EPUB export — user-facing title without a redundant "Chapter N:" prefix.
    /// </summary>
    public static string GetPreviewStyleHeading(string? title, int storageChapterNumber, int narrativeOrdinal)
    {
        if (IsFrontMatter(storageChapterNumber, title))
            return GetExportHeading(title, 0, narrativeOrdinal);

        var t = (title ?? "").Trim();
        if (string.IsNullOrEmpty(t) || ChapterZeroOnlyRegex.IsMatch(t))
            return $"Chapter {narrativeOrdinal}";

        var display = BookChapterHeadingFormatter.GetDisplayTitle(t, narrativeOrdinal);
        if (!string.IsNullOrWhiteSpace(display)
            && !display.Equals($"Part {narrativeOrdinal}", StringComparison.OrdinalIgnoreCase))
            return display;

        return $"Chapter {narrativeOrdinal}";
    }

    /// <summary>Plain heading for export TOC / H1 (not HTML-encoded).</summary>
    public static string GetExportHeading(string? title, int storageChapterNumber, int narrativeOrdinal)
    {
        var t = (title ?? "").Trim();

        if (IsFrontMatter(storageChapterNumber, title))
        {
            if (string.IsNullOrEmpty(t)) return "Front matter";
            if (ChapterZeroOnlyRegex.IsMatch(t)) return t;
            var cleaned = BookChapterHeadingFormatter.GetDisplayTitle(t, 1);
            return string.IsNullOrWhiteSpace(cleaned) || cleaned.Equals("Part 1", StringComparison.OrdinalIgnoreCase)
                ? t
                : cleaned;
        }

        if (string.IsNullOrEmpty(t) || ChapterZeroOnlyRegex.IsMatch(t))
            return $"Chapter {narrativeOrdinal}";

        var phOrdinal = narrativeOrdinal;
        var stripped = BookChapterHeadingFormatter.GetDisplayTitle(t, phOrdinal);
        if (string.IsNullOrWhiteSpace(stripped) || stripped.Equals($"Part {phOrdinal}", StringComparison.OrdinalIgnoreCase))
            return $"Chapter {narrativeOrdinal}";

        if (ChapterPrefixAlreadyMatches(stripped, narrativeOrdinal))
            return stripped;

        return $"Chapter {narrativeOrdinal}: {stripped}";
    }

    /// <summary>Default title when loading chapters from DB (merge pipeline).</summary>
    public static string GetDefaultStoredTitle(string? storedTitle, int storageChapterNumber, int narrativeOrdinal)
    {
        var t = (storedTitle ?? "").Trim();
        if (!string.IsNullOrEmpty(t))
        {
            if (IsFrontMatter(storageChapterNumber, t))
                return ChapterZeroOnlyRegex.IsMatch(t) ? t : t;
            if (ChapterZeroOnlyRegex.IsMatch(t))
                return $"Chapter {narrativeOrdinal}";
            return t;
        }

        return IsFrontMatter(storageChapterNumber) ? "Front matter" : $"Chapter {narrativeOrdinal}";
    }

    private static bool ChapterPrefixAlreadyMatches(string stripped, int narrativeOrdinal)
    {
        var pattern = $@"^\s*Chapter\s*{narrativeOrdinal}\s*(:|\s|$)";
        return Regex.IsMatch(stripped, pattern, RegexOptions.IgnoreCase);
    }
}
