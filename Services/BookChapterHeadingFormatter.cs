using System.Text.RegularExpressions;

namespace EBookDashboard.Services;

/// <summary>
/// Produces clean chapter headings for PDF/TOC: user title only when present, no duplicate "Chapter N" prefixes.
/// </summary>
public static class BookChapterHeadingFormatter
{
    private static readonly Regex ChapterPrefixRegex = new(
        @"^\s*(chapter|ch\.?)\s*[0-9IVXLCMDivxlcdm]+\s*[\s:\.\-\u2013\u2014–—]*\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// After placeholders are applied, strips redundant leading "Chapter 3:" style labels and returns display text for H2/TOC.
    /// </summary>
    public static string GetDisplayTitle(string placeholderAppliedTitle, int ordinal)
    {
        var t = (placeholderAppliedTitle ?? "").Trim();
        if (string.IsNullOrEmpty(t))
            return $"Part {ordinal}";

        var stripped = ChapterPrefixRegex.Replace(t, "").Trim();
        if (string.IsNullOrEmpty(stripped))
            return $"Part {ordinal}";

        return stripped;
    }

    /// <summary>
    /// Plain-text TOC label (HTML encoding is caller's responsibility).
    /// </summary>
    public static string GetTocLabel(string placeholderAppliedTitle, int ordinal) =>
        GetDisplayTitle(placeholderAppliedTitle, ordinal);
}
