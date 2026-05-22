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

    public static bool IsFrontMatter(int chapterNumber) => chapterNumber <= 0;

    public static List<ChapterDto> OrderForExport(IEnumerable<ChapterDto>? chapters) =>
        (chapters ?? Enumerable.Empty<ChapterDto>())
            .Where(c => !string.IsNullOrWhiteSpace(c.Content))
            .OrderBy(c => c.ChapterNumber)
            .ThenBy(c => c.Title ?? "")
            .ToList();

    /// <summary>Plain heading for export TOC / H1 (not HTML-encoded).</summary>
    public static string GetExportHeading(string? title, int storageChapterNumber, int narrativeOrdinal)
    {
        var t = (title ?? "").Trim();

        if (IsFrontMatter(storageChapterNumber))
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
            if (IsFrontMatter(storageChapterNumber))
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
