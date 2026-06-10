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
        var fmt = (opt.Format ?? "Ebook").Trim();

        // Ebook-only workflow without a print distributor → screen-friendly A4.
        if (string.IsNullOrEmpty(platform) &&
            fmt.Equals("Ebook", StringComparison.OrdinalIgnoreCase))
            return A4ScreenLayout();

        // Print / dual format without explicit platform → standard 6×9 interior.
        if (string.IsNullOrEmpty(platform) &&
            (fmt.Equals("Paperback", StringComparison.OrdinalIgnoreCase) ||
             fmt.Equals("Print", StringComparison.OrdinalIgnoreCase) ||
             fmt.Equals("Both", StringComparison.OrdinalIgnoreCase)))
        {
            return ApplyMarginOverrides(Trim6x9Print(bleedHeavy: false), marginOverrides);
        }

        var spec = platform switch
        {
            "justprint" => Trim6x9Print(bleedHeavy: true),
            "ingram" => Trim6x9Print(bleedHeavy: true),
            "bn" => Trim6x9Print(bleedHeavy: false),
            "kdp" => Trim6x9Print(bleedHeavy: false),
            _ => fmt.Equals("Ebook", StringComparison.OrdinalIgnoreCase)
                ? A4ScreenLayout()
                : Trim6x9Print(bleedHeavy: false)
        };
        return spec.PageSizeCss.Contains("6in", StringComparison.Ordinal)
            ? ApplyMarginOverrides(spec, marginOverrides)
            : spec;
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
    /// 6×9 print margins from <see cref="InteriorLayoutTokens"/> — Chromium header/footer zone;
    /// inner text inset comes from <c>--ilt-pad-*</c> on <c>.book-preview-sheet</c> (preview parity).
    /// </summary>
    private static PdfLayoutSpec Trim6x9Print(bool bleedHeavy)
    {
        var m = InteriorLayoutTokens.KdpTrim6x9Default;
        var bleedBump = bleedHeavy ? 0.05 : 0.0;
        return new PdfLayoutSpec(
            PageSizeCss: "6in 9in",
            PdfWidth: "6in",
            PdfHeight: "9in",
            UseBuiltInFormat: false,
            BuiltInFormat: PaperFormat.A4,
            MarginTop: InchesWithBump(m.Top, bleedBump),
            MarginBottom: InchesWithBump(m.Bottom, bleedBump),
            MarginLeft: InchesWithBump(m.Inside, bleedBump * 0.6),
            MarginRight: InchesWithBump(m.Outside, bleedBump * 0.4),
            PreferCssPageSize: true);
    }

    private static string InchesWithBump(string inches, double bump)
    {
        if (bump <= 0 || !inches.EndsWith("in", StringComparison.OrdinalIgnoreCase)) return inches;
        if (!double.TryParse(inches[..^2], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
            return inches;
        return FormattableString.Invariant($"{v + bump:0.##}in");
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
