using EBookDashboard.Models;
using EBookDashboard.Services;

namespace EBookDashboard.Models.DTO;

/// <summary>
/// Canonical formatting choices for a book — persisted via <see cref="BookFormatting"/> + draft JSON,
/// applied identically to preview iframe, PDF (Chromium), and EPUB typography.
/// </summary>
public sealed class BookFormattingSettings
{
    public string TrimSize { get; set; } = "6x9";
    public string InteriorStyle { get; set; } = "Novel";
    public string TextSize { get; set; } = "Medium";
    public string LineSpacing { get; set; } = "1.6";
    public string Format { get; set; } = "Ebook";
    public string? PublishingPlatform { get; set; }
    public string? PublishingPlatforms { get; set; }
    public string? PreviewAccent { get; set; }
    public string? PageBackgroundColor { get; set; }
    public bool IncludeCoverPage { get; set; } = true;
    public bool ChapterStartsNewPage { get; set; } = true;
    public bool ShowPageNumbers { get; set; } = true;
    public bool ShowRunningHeader { get; set; } = true;
    public string TextAlign { get; set; } = "justify";

    /// <summary>KDP gutter margins from <see cref="InteriorLayoutTokens"/> (inside &gt; outside).</summary>
    public double MarginTopIn { get; set; }
    public double MarginBottomIn { get; set; }
    public double MarginInsideIn { get; set; }
    public double MarginOutsideIn { get; set; }

    public string BodyFontSizePt { get; set; } = "12";
    public string BodyLineHeight { get; set; } = "1.6";

    /// <summary>Maps persisted rows + draft into export options (single settings pipeline).</summary>
    public static BookFormattingSettings FromPersistence(BookFormatting? row, string? draftJson, ExportBookPdfRequest? requestOverrides = null)
    {
        var opt = BookPdfExportOptions.LoadFromPersistence(row, draftJson);
        opt.ApplyRequestOverrides(requestOverrides);
        return FromExportOptions(opt);
    }

    public static BookFormattingSettings FromExportOptions(BookPdfExportOptions opt)
    {
        var margins = InteriorLayoutTokens.KdpTrim6x9Default;
        return new BookFormattingSettings
        {
            TrimSize = "6x9",
            InteriorStyle = opt.InteriorStyle,
            TextSize = opt.TextSize,
            LineSpacing = opt.LineSpacing,
            Format = opt.Format,
            PublishingPlatform = opt.PublishingPlatform,
            PublishingPlatforms = opt.PublishingPlatforms,
            PreviewAccent = opt.PreviewAccent,
            PageBackgroundColor = opt.PageBackgroundColor,
            IncludeCoverPage = opt.IncludeCoverPage,
            ChapterStartsNewPage = true,
            ShowPageNumbers = true,
            ShowRunningHeader = true,
            TextAlign = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle) is "Classic" or "ElegantTrade"
                ? "justify"
                : "justify",
            MarginTopIn = ParseInches(margins.Top) ?? 0.5,
            MarginBottomIn = ParseInches(margins.Bottom) ?? 0.5,
            MarginInsideIn = ParseInches(margins.Inside) ?? 0.75,
            MarginOutsideIn = ParseInches(margins.Outside) ?? 0.5,
            BodyFontSizePt = opt.BodyFontSizePt(),
            BodyLineHeight = opt.BodyLineHeight()
        };
    }

    public BookPdfExportOptions ToExportOptions()
    {
        var o = new BookPdfExportOptions
        {
            InteriorStyle = InteriorStyle,
            TextSize = TextSize,
            LineSpacing = LineSpacing,
            Format = Format,
            PublishingPlatform = PublishingPlatform ?? "",
            PublishingPlatforms = PublishingPlatforms,
            PreviewAccent = PreviewAccent,
            PageBackgroundColor = PageBackgroundColor,
            IncludeCoverPage = IncludeCoverPage
        };
        o.Normalize();
        return o;
    }

    private static double? ParseInches(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim().ToLowerInvariant();
        if (s.EndsWith("in", StringComparison.Ordinal)) s = s[..^2];
        return double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var inches)
            ? inches
            : null;
    }
}
