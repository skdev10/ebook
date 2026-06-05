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

    public static PdfLayoutSpec Resolve(BookPdfExportOptions opt)
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
            return Trim6x9Print(bleedHeavy: false);
        }

        return platform switch
        {
            "justprint" => Trim6x9Print(bleedHeavy: true),
            "ingram" => Trim6x9Print(bleedHeavy: true),
            "bn" => Trim6x9Print(bleedHeavy: false),
            "kdp" => Trim6x9Print(bleedHeavy: false),
            _ => fmt.Equals("Ebook", StringComparison.OrdinalIgnoreCase)
                ? A4ScreenLayout()
                : Trim6x9Print(bleedHeavy: false)
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

    private static PdfLayoutSpec Trim6x9Print(bool bleedHeavy) => new(
        PageSizeCss: "6in 9in",
        PdfWidth: "6in",
        PdfHeight: "9in",
        UseBuiltInFormat: false,
        BuiltInFormat: PaperFormat.A4,
        MarginTop: bleedHeavy ? "20mm" : "19mm",
        MarginBottom: bleedHeavy ? "22mm" : "20mm",
        MarginLeft: bleedHeavy ? "20mm" : "18mm",
        MarginRight: bleedHeavy ? "18mm" : "17mm",
        PreferCssPageSize: false);

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
