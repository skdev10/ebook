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

        // Explicit Formatting workspace trim wins over style-based trim.
        string tw;
        string th;
        if (opt.TrimWidthIn is > 0 && opt.TrimHeightIn is > 0)
        {
            tw = FormatIn(opt.TrimWidthIn.Value);
            th = FormatIn(opt.TrimHeightIn.Value);
        }
        else
        {
            (tw, th) = TrimForInterior(opt.InteriorStyle);
        }

        // POD styles or explicit bleed → page grows by 0.25in per dimension.
        var style = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);
        var isPodBleed = style is "POD" or "ElegantTradePOD";
        if (opt.UseBleed || isPodBleed)
        {
            tw = AddBleed(tw);
            th = AddBleed(th);
        }

        var effectiveMargins = marginOverrides;
        if (opt.MarginTopIn is > 0 || opt.MarginBottomIn is > 0 || opt.MarginInsideIn is > 0 || opt.MarginOutsideIn is > 0)
        {
            effectiveMargins = new BookPdfLayoutOptions
            {
                MarginTop = opt.MarginTopIn is > 0 ? FormatIn(opt.MarginTopIn.Value) : effectiveMargins?.MarginTop,
                MarginBottom = opt.MarginBottomIn is > 0 ? FormatIn(opt.MarginBottomIn.Value) : effectiveMargins?.MarginBottom,
                MarginInside = opt.MarginInsideIn is > 0 ? FormatIn(opt.MarginInsideIn.Value) : effectiveMargins?.MarginInside,
                MarginOutside = opt.MarginOutsideIn is > 0 ? FormatIn(opt.MarginOutsideIn.Value) : effectiveMargins?.MarginOutside
            };
        }

        spec = ApplyMarginOverrides(spec, effectiveMargins);

        spec = spec with
        {
            PageSizeCss = $"{tw} {th}",
            PdfWidth = tw,
            PdfHeight = th,
            UseBuiltInFormat = false,
            PreferCssPageSize = !(opt.UseBleed || isPodBleed)
        };
        return spec;
    }

    private static string FormatIn(double inches) =>
        string.Concat(inches.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture), "in");

    /// <summary>Adds bleed on both sides (2 × BleedIn from KdpSpecs) to an inches CSS dimension like "6in".</summary>
    private static string AddBleed(string inches)
    {
        var bleedTotal = 2 * EBookDashboard.Configuration.KdpSpecsAccessor.Current.BleedIn;
        var raw = inches.Replace("in", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? string.Concat((v + bleedTotal).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), "in")
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
