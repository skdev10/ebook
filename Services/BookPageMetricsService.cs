using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>
/// Single-source pagination estimator for publish workflows.
/// </summary>
public sealed class BookPageMetricsService : IBookPageMetricsService
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WordRegex = new(@"\b[\p{L}\p{N}_']+\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ImgRegex = new(@"<img\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public BookPageMetricsDto Estimate(BookDetailsResponseDto? details, BookPdfExportOptions? options)
    {
        var chapters = details?.Chapters ?? new List<ChapterDto>();
        var chapterCount = chapters.Count;
        var words = 0;
        var images = 0;

        foreach (var ch in chapters)
        {
            words += CountWords(ch.Title);
            words += CountWords(ch.Content);
            images += CountImages(ch.Content);
        }

        var baseWordsPerPage = ResolveBaseWordsPerPage(options);
        var imagePenaltyPages = images * 0.45;
        var chapterBreakPages = chapterCount > 0 ? chapterCount * 0.35 : 0;
        var frontMatterPages = chapterCount > 0 ? 4.0 : 1.0; // title + copyright + TOC + section breaks
        var prosePages = words > 0 ? (words / Math.Max(120.0, baseWordsPerPage)) : 0.0;

        var estimated = (int)Math.Ceiling(prosePages + imagePenaltyPages + chapterBreakPages + frontMatterPages);
        var isPrintLike = IsPrintLayout(options);
        var minPages = isPrintLike ? 24 : 1;
        if (estimated < minPages) estimated = minPages;

        return new BookPageMetricsDto
        {
            PageCount = estimated,
            WordCount = words,
            ChapterCount = chapterCount,
            ImageCount = images,
            Basis = "estimated_from_saved_manuscript_and_formatting"
        };
    }

    private static bool IsPrintLayout(BookPdfExportOptions? options)
    {
        var f = (options?.Format ?? "").Trim();
        if (f.Equals("Paperback", StringComparison.OrdinalIgnoreCase)
            || f.Equals("Print", StringComparison.OrdinalIgnoreCase)
            || f.Equals("Both", StringComparison.OrdinalIgnoreCase))
            return true;

        var p = (options?.PrimaryPlatformToken() ?? "").Trim();
        return p.Contains("print", StringComparison.OrdinalIgnoreCase)
               || p.Contains("kdp", StringComparison.OrdinalIgnoreCase)
               || p.Contains("ingram", StringComparison.OrdinalIgnoreCase);
    }

    private static double ResolveBaseWordsPerPage(BookPdfExportOptions? options)
    {
        var isPrint = IsPrintLayout(options);
        var baseWpp = isPrint ? 285.0 : 360.0;

        var textSize = (options?.TextSize ?? "Medium").Trim();
        var sizeFactor = textSize.Equals("Small", StringComparison.OrdinalIgnoreCase)
            ? 1.15
            : textSize.Equals("Large", StringComparison.OrdinalIgnoreCase)
                ? 0.86
                : 1.0;

        var spacing = 1.6;
        if (double.TryParse(options?.LineSpacing, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            spacing = Math.Clamp(parsed, 1.15, 2.4);
        var spacingFactor = Math.Clamp(1.6 / spacing, 0.72, 1.32);

        return baseWpp * sizeFactor * spacingFactor;
    }

    private static int CountWords(string? htmlOrText)
    {
        if (string.IsNullOrWhiteSpace(htmlOrText)) return 0;
        var plain = HtmlTagRegex.Replace(htmlOrText, " ");
        return WordRegex.Matches(plain).Count;
    }

    private static int CountImages(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return 0;
        return ImgRegex.Matches(html).Count;
    }
}
