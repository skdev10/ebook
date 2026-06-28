using System.Net;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// Builds Chromium (PuppeteerSharp) per-page header/footer templates for the interior PDF.
/// Chromium draws these in the reserved page margins on EVERY page, so they are used only for the
/// interior export (no cover). Templates may only use inline styles + the special
/// <c>pageNumber</c>/<c>title</c> classes — CSS variables and external fonts do not apply here,
/// so we use web-safe fonts that match each style's feel (header text is 8pt and secondary).
/// </summary>
public static class PdfRunningHeaderFooter
{
    private static (string Font, string Color, string Rule) HeaderStyle(string interior) => interior switch
    {
        "ElegantTrade" or "ElegantTradePOD" => ("Georgia, 'Times New Roman', serif", "#1c1c1c", "#C9A84C"),
        "FineBook" => ("Georgia, 'Times New Roman', serif", "#1a120b", "#C9A84C"),
        "Traditional" => ("Georgia, 'Times New Roman', serif", "#222222", "#aaaaaa"),
        "Novel" => ("Georgia, 'Times New Roman', serif", "#1a1a1a", "#dddddd"),
        "Classic" => ("Georgia, 'Times New Roman', serif", "#000000", "#aaaaaa"),
        "POD" => ("Georgia, 'Times New Roman', serif", "#111111", "#cccccc"),
        "Contemporary" => ("Arial, Helvetica, sans-serif", "#111827", "#3B82F6"),
        "Modern" => ("Arial, Helvetica, sans-serif", "#111827", "#e2e8f0"),
        "Clean" => ("Arial, Helvetica, sans-serif", "#374151", "#d1d5db"),
        _ => ("Georgia, 'Times New Roman', serif", "#222222", "#cccccc")
    };

    private static (string Font, string Color) FooterStyle(string interior) => interior switch
    {
        "ElegantTrade" or "ElegantTradePOD" or "FineBook" => ("Georgia, 'Times New Roman', serif", "#1c1c1c"),
        "Traditional" or "Classic" or "Novel" => ("Georgia, 'Times New Roman', serif", "#333333"),
        "Contemporary" or "Modern" or "Clean" => ("Arial, Helvetica, sans-serif", "#374151"),
        "Minimalist" => ("Arial, Helvetica, sans-serif", "#9ca3af"),
        "POD" => ("Georgia, 'Times New Roman', serif", "#555555"),
        _ => ("Georgia, 'Times New Roman', serif", "#555555")
    };

    /// <summary>Running header — book title centered with a thin per-style rule. Empty for Minimalist.</summary>
    public static string BuildHeader(string? interiorStyle, string bookTitle)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(interiorStyle);
        if (interior == "Minimalist")
            return "<span></span>"; // Chromium requires a non-empty template string

        var (font, color, rule) = HeaderStyle(interior);
        var title = WebUtility.HtmlEncode((bookTitle ?? string.Empty).Trim());
        if (title.Length == 0)
            return "<span></span>";

        return $@"<div style=""width:100%;font-family:{font};font-size:8pt;font-style:italic;color:{color};text-align:center;padding:0 12mm;box-sizing:border-box;"">"
             + $@"<span style=""display:inline-block;border-bottom:0.5pt solid {rule};padding:0 6pt 2pt;"">{title}</span></div>";
    }

    /// <summary>Folio — plain centered page number; Fine Book uses the ◆ N ◆ ornament.</summary>
    public static string BuildFooter(string? interiorStyle)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(interiorStyle);
        var (font, color) = FooterStyle(interior);
        var numeral = interior == "FineBook"
            ? "&#9670; <span class=\"pageNumber\"></span> &#9670;"
            : "<span class=\"pageNumber\"></span>";

        return $@"<div style=""width:100%;font-family:{font};font-size:9pt;color:{color};text-align:center;padding:0;box-sizing:border-box;"">{numeral}</div>";
    }
}
