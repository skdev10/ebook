using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services.PdfExport;

/// <summary>Maps formatter interior style to PDFsharp font families (embedded TTF names).</summary>
public static class ExportPdfTypography
{
    public sealed record TypefaceSet(string BodyFamily, string HeadingFamily, bool JustifyBody);

    public static TypefaceSet ForInterior(BookPdfExportOptions opt)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);
        return interior switch
        {
            "Modern" or "Minimalist" => new("Inter", "Inter", false),
            "Classic" => new("Cormorant Garamond", "Cormorant Garamond", true),
            "ElegantTrade" => new("EB Garamond", "Lora", true),
            _ => new("Merriweather", "Playfair Display", true)
        };
    }

    public static double BodySizePt(BookPdfExportOptions opt) =>
        double.Parse(InteriorExportTheme.ResolveBodyFontSizePt(
            opt.InteriorStyle, opt.TextSize), System.Globalization.CultureInfo.InvariantCulture);

    public static double LineHeightMultiplier(BookPdfExportOptions opt) =>
        double.Parse(InteriorExportTheme.ResolveLineHeight(opt.LineSpacing),
            System.Globalization.CultureInfo.InvariantCulture);
}
