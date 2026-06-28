using EBookDashboard.Models.DTO;
using PuppeteerSharp.Media;

namespace EBookDashboard.Services;

/// <summary>
/// Maps saved publishing platform + book format to PDF page dimensions and margins.
/// </summary>
public static class BookPdfPlatformLayout
{
    public sealed record PdfLayoutSpec(
        string PageSizeCss,
        string? PdfWidth,
        string? PdfHeight,
        bool UseBuiltInFormat,
        PuppeteerSharp.Media.PaperFormat BuiltInFormat,
        string MarginTop,
        string MarginBottom,
        string MarginLeft,
        string MarginRight,
        bool PreferCssPageSize);

    public static PdfLayoutSpec Resolve(BookPdfExportOptions opt, BookPdfLayoutOptions? marginOverrides = null)
    {
        var platform = NormalizePlatform(opt.PrimaryPlatformToken());
        var bleedHeavy = platform is "justprint" or "ingram";

        // Start from the standard print spec (full-trim page; margin 0 because the per-style
        // insets are provided by .book-preview-sheet padding, identical to the formatter preview).
        var spec = ApplyMarginOverrides(Trim6x9Print(bleedHeavy), marginOverrides);

        // Page SHAPE follows the selected interior style so the exported PDF trim matches what
        // the user sees in the per-style preview (Novel 5×8, Elegant/Traditional/Classic/Contemporary/
        // Minimalist/ElegantTradePOD 5.5×8.5, Modern/FineBook/Clean/POD 6×9).
        var (tw, th) = TrimForInterior(opt.InteriorStyle);

        // POD styles carry a 0.125in bleed on every side → page grows by 0.25in per dimension.
        // The interior CSS adds matching bleed padding so text stays in the safe zone inside trim.
        var style = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);
        var isPodBleed = style is "POD" or "ElegantTradePOD";
        if (isPodBleed)
        {
            tw = AddBleed(tw);
            th = AddBleed(th);
        }

        spec = spec with
        {
            PageSizeCss = $"{tw} {th}",
            PdfWidth = tw,
            PdfHeight = th,
            UseBuiltInFormat = false,
            // POD bleed → explicit Width/Height takes priority over CSS @page size; all other
            // styles keep PreferCSSPageSize=true so the CSS @page trim is authoritative.
            PreferCssPageSize = !isPodBleed
        };
        return spec;
    }

    /// <summary>Adds a 0.125in bleed per side (0.25in total) to an inches CSS dimension like "6in".</summary>
    private static string AddBleed(string inches)
    {
        var raw = inches.Replace("in", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? string.Concat((v + 0.25).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "in")
            : inches;
    }

    /// <summary>Trim size (inches CSS) for each interior style — keeps PDF shape == preview shape.</summary>
    private static (string Width, string Height) TrimForInterior(string? interiorStyle)
    {
        var style = InteriorExportTheme.NormalizeInteriorStyle(interiorStyle);
        return style switch
        {
            "Novel" => ("5in", "8in"),
            "ElegantTrade" or "Traditional" or "Contemporary" or "Classic"
                or "Minimalist" or "ElegantTradePOD" => ("5.5in", "8.5in"),
            "Modern" or "FineBook" or "Clean" or "POD" => ("6in", "9in"),
            _ => ("6in", "9in")
        };
    }

    private static PdfLayoutSpec ApplyMarginOverrides(PdfLayoutSpec spec, BookPdfLayoutOptions? o)
    {
        if (o == null) return spec;
        return spec with
        {
            MarginTop = string.IsNullOrWhiteSpace(o.MarginTop) ? spec.MarginTop : o.MarginTop.Trim(),
            MarginBottom = string.IsNullOrWhiteSpace(o.MarginBottom) ? spec.MarginBottom : o.MarginBottom.Trim(),
            MarginLeft = string.IsNullOrWhiteSpace(o.MarginInside) ? spec.MarginLeft : o.MarginInside.Trim(),
            MarginRight = string.IsNullOrWhiteSpace(o.MarginOutside) ? spec.MarginRight : o.MarginOutside.Trim()
        };
    }

    private static PdfLayoutSpec A4ScreenLayout() => new(
        PageSizeCss: "A4",
        PdfWidth: null,
        PdfHeight: null,
        UseBuiltInFormat: true,
        BuiltInFormat: PaperFormat.A4,
        MarginTop: "22mm",
        MarginBottom: "24mm",
        MarginLeft: "16mm",
        MarginRight: "16mm",
        PreferCssPageSize: false);

    /// <summary>
    /// 6×9 print — full trim page; KDP margins live in HTML (<c>.book-preview-sheet</c> padding + in-page running head).
    /// </summary>
    private static PdfLayoutSpec Trim6x9Print(bool bleedHeavy)
    {
        _ = bleedHeavy; // reserved for future bleed-specific margin bumps
        return new PdfLayoutSpec(
            PageSizeCss: "6in 9in",
            PdfWidth: "6in",
            PdfHeight: "9in",
            UseBuiltInFormat: false,
            BuiltInFormat: PaperFormat.A4,
            MarginTop: "0",
            MarginBottom: "0",
            MarginLeft: "0",
            MarginRight: "0",
            PreferCssPageSize: true);
    }

    private static string NormalizePlatform(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim().ToLowerInvariant();
        if (s.Contains("just print", StringComparison.OrdinalIgnoreCase) || s.Contains("print ready", StringComparison.OrdinalIgnoreCase))
            return "justprint";
        if (s.Contains("ingram", StringComparison.OrdinalIgnoreCase))
            return "ingram";
        if (s.Contains("barnes", StringComparison.OrdinalIgnoreCase) || s.Contains("b&n", StringComparison.OrdinalIgnoreCase))
            return "bn";
        if (s.Contains("kdp", StringComparison.OrdinalIgnoreCase) || s.Contains("amazon", StringComparison.OrdinalIgnoreCase))
            return "kdp";
        return "";
    }
}
