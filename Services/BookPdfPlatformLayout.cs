using System.Net;
using EBookDashboard.Models.DTO;
using PuppeteerSharp.Media;

namespace EBookDashboard.Services;

/// <summary>
/// Maps saved publishing platform + book format to PDF page dimensions, margins, and print notes (bleed / CMYK guidance).
/// Chromium outputs sRGB; CMYK conversion is documented for professional print workflows.
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
        bool PreferCssPageSize,
        string BleedNoteHtml,
        string CmykNoteHtml);

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
            return Trim6x9Print(bleedHeavy: false, cmyk: true,
                "Print layout (6×9\"). Select a publishing platform in Book Formatting for distributor-specific margin notes.");
        }

        return platform switch
        {
            "justprint" => Trim6x9Print(bleedHeavy: true, cmyk: true,
                "Bleed: this PDF uses a 6×9\" trim with extra safe margins. For full bleed, extend artwork 0.125\" beyond trim per your printer."),
            "ingram" => Trim6x9Print(bleedHeavy: true, cmyk: true,
                "Bleed: follow IngramSpark interior PDF specs (typically 0.125\" bleed outside trim). Safe area respected in this export."),
            "bn" => Trim6x9Print(bleedHeavy: false, cmyk: true,
                "Margins follow common Barnes & Noble Press paperback safe zones; confirm current trim requirements in your dashboard."),
            "kdp" => Trim6x9Print(bleedHeavy: false, cmyk: true,
                "Margins suit typical KDP paperback interiors; verify latest KDP margin minimums for your trim size."),
            _ => fmt.Equals("Ebook", StringComparison.OrdinalIgnoreCase)
                ? A4ScreenLayout()
                : Trim6x9Print(bleedHeavy: false, cmyk: true,
                    "Print layout (6×9\"). Confirm final specs with your chosen distributor.")
        };
    }

    private static PdfLayoutSpec A4ScreenLayout()
    {
        const string cmyk = "";
        const string bleed = "";
        return new PdfLayoutSpec(
            PageSizeCss: "A4",
            PdfWidth: null,
            PdfHeight: null,
            UseBuiltInFormat: true,
            BuiltInFormat: PaperFormat.A4,
            MarginTop: "22mm",
            MarginBottom: "24mm",
            MarginLeft: "16mm",
            MarginRight: "16mm",
            PreferCssPageSize: false,
            BleedNoteHtml: bleed,
            CmykNoteHtml: cmyk);
    }

    private static PdfLayoutSpec Trim6x9Print(bool bleedHeavy, bool cmyk, string bleedMsg)
    {
        var cmykNote = cmyk
            ? "Color: exported as sRGB. For offset or POD CMYK workflows, convert images and review in Acrobat or your printer’s preflight."
            : "";
        return new PdfLayoutSpec(
            PageSizeCss: "6in 9in",
            PdfWidth: "6in",
            PdfHeight: "9in",
            UseBuiltInFormat: false,
            BuiltInFormat: PaperFormat.A4,
            // Slightly wider inner margin for gutter on print targets
            MarginTop: bleedHeavy ? "20mm" : "19mm",
            MarginBottom: bleedHeavy ? "22mm" : "20mm",
            MarginLeft: bleedHeavy ? "20mm" : "18mm",
            MarginRight: bleedHeavy ? "18mm" : "17mm",
            PreferCssPageSize: false,
            BleedNoteHtml: WebUtility.HtmlEncode(bleedMsg),
            CmykNoteHtml: WebUtility.HtmlEncode(cmykNote));
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
