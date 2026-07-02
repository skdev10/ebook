using EBookDashboard.Services;

namespace EBookDashboard.Models.DTO;

/// <summary>
/// Single source-of-truth theme for preview, PDF export, and EPUB — derived from persisted formatter settings.
/// </summary>
public sealed class BookTheme
{
    public string PrimaryColor { get; set; } = "#111827";
    public string AccentColor { get; set; } = "#5b21b6";
    public string HeadingFont { get; set; } = "Georgia, serif";
    public string BodyFont { get; set; } = "Georgia, serif";
    public string HeadingColor { get; set; } = "#1c1917";
    public string BodyTextColor { get; set; } = "#334155";
    public string PageBackgroundColor { get; set; } = "#ffffff";
    public string TocStyle { get; set; } = "minimal";
    public string TrimSize { get; set; } = "6x9";
    public string InteriorStyle { get; set; } = "Novel";

    /// <summary>Maps export options + resolved theme tokens into a portable theme model.</summary>
    public static BookTheme FromExportOptions(BookPdfExportOptions opt)
    {
        opt.Normalize();
        var interior = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);
        var spec = InteriorExportTheme.ResolveExportThemeTokens(interior);
        var pageBg = opt.ResolvePageBackgroundColor();
        var accent = opt.PreviewAccent;
        if (string.IsNullOrWhiteSpace(accent)) accent = "#5b21b6";

        return new BookTheme
        {
            InteriorStyle = interior,
            PrimaryColor = spec.HeadingColor,
            AccentColor = accent,
            HeadingFont = spec.HeadingFont,
            BodyFont = spec.BodyFont,
            HeadingColor = spec.HeadingColor,
            BodyTextColor = spec.BodyColor,
            PageBackgroundColor = pageBg,
            TocStyle = InteriorLayoutTokens.ResolveTocStyle(interior),
            TrimSize = "6x9"
        };
    }

    public BookPdfExportOptions ToExportOptions()
    {
        return new BookPdfExportOptions
        {
            InteriorStyle = InteriorStyle,
            PreviewAccent = AccentColor,
            PageBackgroundColor = PageBackgroundColor
        };
    }
}
