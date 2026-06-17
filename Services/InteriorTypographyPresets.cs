using System.Globalization;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>
/// Single mapping for InteriorStyle + TextSize + LineSpacing → print typography.
/// Preview CSS vars and PDF export both read values from here (via <see cref="InteriorLayoutTokens"/>).
/// </summary>
public static class InteriorTypographyPresets
{
    /// <summary>Approximate print point sizes for formatter pills (Small / Medium / Large).</summary>
    public const double PtSmall = 10.0;

    public const double PtMedium = 11.0;

    public const double PtLarge = 12.0;

    public sealed record TypographyValues(
        string BodyFontSizePt,
        string BodyFontSizePx,
        string LineHeight,
        string TextIndent,
        string ParagraphSpacing,
        string TextAlign,
        string BodyFontStack);

    /// <summary>Resolve body typography for preview + PDF from export options.</summary>
    public static TypographyValues Resolve(BookPdfExportOptions opt)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);
        var size = InteriorExportTheme.NormalizeTextSize(opt.TextSize);
        var lh = InteriorLayoutTokens.ResolveLineHeightExact(opt.LineSpacing);
        var pt = BodyPointSize(interior, size);
        var px = PxFromPt(pt);

        return interior switch
        {
            "Minimalist" => new TypographyValues(
                FormatPt(pt),
                px,
                lh,
                "0",
                InteriorSpacingTheme.Mm(InteriorSpacingTheme.MinimalistParagraphSpacingMm),
                "left",
                "'Inter', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"),
            "Classic" => new TypographyValues(
                FormatPt(ClassicPointSize(size)),
                PxFromPt(ClassicPointSize(size)),
                lh,
                "1.25rem",
                "0.28em",
                "justify",
                "'Cormorant Garamond', 'Palatino Linotype', Palatino, Georgia, serif"),
            "ElegantTrade" => new TypographyValues(
                FormatPt(pt),
                px,
                lh,
                InteriorSpacingTheme.Mm(InteriorSpacingTheme.FirstLineIndentMm),
                "0",
                "justify",
                "'EB Garamond', Baskerville, 'Palatino Linotype', Palatino, Georgia, 'Times New Roman', serif"),
            _ => new TypographyValues(
                FormatPt(pt),
                px,
                lh,
                InteriorSpacingTheme.Mm(InteriorSpacingTheme.FirstLineIndentMm),
                InteriorSpacingTheme.Mm(InteriorSpacingTheme.ParagraphSpacingMm),
                "justify",
                "'Merriweather', Georgia, 'Times New Roman', Times, serif")
        };
    }

    /// <summary>Body size in px for formatter JS — same numbers as <see cref="Resolve"/>.</summary>
    public static string ResolveBodyFontSizePx(string? interiorStyle, string? textSize) =>
        Resolve(new BookPdfExportOptions
        {
            InteriorStyle = interiorStyle,
            TextSize = textSize
        }).BodyFontSizePx;

    /// <summary>Body size in pt for print CSS.</summary>
    public static string ResolveBodyFontSizePt(string? interiorStyle, string? textSize) =>
        Resolve(new BookPdfExportOptions
        {
            InteriorStyle = interiorStyle,
            TextSize = textSize
        }).BodyFontSizePt;

    private static double BodyPointSize(string interior, string size)
    {
        if (interior == "Classic")
            return ClassicPointSize(size);
        return size switch
        {
            "Small" => PtSmall,
            "Large" => PtLarge,
            _ => PtMedium
        };
    }

    private static double ClassicPointSize(string size) =>
        size switch
        {
            "Small" => 14.0,
            "Large" => 19.0,
            _ => 17.0
        };

    private static string FormatPt(double pt) =>
        pt.ToString("0.##", CultureInfo.InvariantCulture);

    private static string PxFromPt(double pt) =>
        (pt * 96.0 / 72.0).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Payload for formatter preview JS (font sizes per size tier).</summary>
    public static object ClientTypographyPayload() => new
    {
        ptSmall = PtSmall,
        ptMedium = PtMedium,
        ptLarge = PtLarge,
        pxSmall = PxFromPt(PtSmall),
        pxMedium = PxFromPt(PtMedium),
        pxLarge = PxFromPt(PtLarge)
    };
}
