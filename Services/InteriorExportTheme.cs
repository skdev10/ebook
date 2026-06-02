using System.Globalization;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>Interior typography for EPUB/PDF export — mirrors Book Formatter preview styles.</summary>
public static class InteriorExportTheme
{
    public static string BuildEpubStylesheet(BookPdfExportOptions opt)
    {
        var interior = (opt.InteriorStyle ?? "Novel").Trim();
        var fontSize = ResolveBodyFontSizePx(opt.TextSize);
        var lineHeight = opt.BodyLineHeight();
        var theme = ResolveTheme(interior);

        return string.Concat(
            "body { ",
            "font-family: ", theme.BodyFont, "; ",
            "font-size: ", fontSize, "px; ",
            "line-height: ", lineHeight, "; ",
            "margin: 0; padding: 12px 18px; ",
            "color: ", theme.BodyColor, "; ",
            "background: ", theme.PageBackground, "; ",
            "orphans: 3; widows: 3; ",
            "} ",
            "h1, h2, h3, h4, h5, h6 { ",
            "font-family: ", theme.HeadingFont, "; ",
            "color: ", theme.HeadingColor, "; ",
            theme.HeadingRules,
            "} ",
            "p { ",
            theme.ParagraphRules,
            "margin-top: 0; orphans: 3; widows: 3; page-break-inside: avoid; ",
            "} ",
            "p:first-of-type, h1 + p, h2 + p, h4 + p { text-indent: 0; } ",
            "img { max-width: 100%; height: auto; } ",
            "::selection { background: #F5C842; color: #1a1200; } ",
            "::-moz-selection { background: #F5C842; color: #1a1200; } ");
    }

    private static string ResolveBodyFontSizePx(string? textSize)
    {
        if (string.Equals(textSize, "Small", StringComparison.OrdinalIgnoreCase)) return "14";
        if (string.Equals(textSize, "Large", StringComparison.OrdinalIgnoreCase)) return "20";
        return "16";
    }

    private static ThemeSpec ResolveTheme(string interior)
    {
        if (interior.Equals("Modern", StringComparison.OrdinalIgnoreCase))
        {
            return new ThemeSpec(
                "'Source Serif 4', Georgia, serif",
                "'Source Serif 4', Georgia, serif",
                "#111111",
                "#1a1a1a",
                "#ffffff",
                "font-weight: 700; font-size: 1.35em; border-bottom: 2px solid #e0e0e0; padding-bottom: 0.35em; margin: 1.5em 0 1em; text-align: left;",
                "margin-bottom: 0.9em; text-indent: 0; text-align: left;");
        }

        if (interior.Equals("Minimalist", StringComparison.OrdinalIgnoreCase))
        {
            return new ThemeSpec(
                "'Lato', Arial, sans-serif",
                "'Lato', Arial, sans-serif",
                "#111111",
                "#1a1a1a",
                "#ffffff",
                "font-weight: 700; font-size: 1.4em; letter-spacing: 0.06em; text-transform: uppercase; margin: 1.5em 0 1em; text-align: left;",
                "margin-bottom: 0.8em; text-indent: 0; text-align: left;");
        }

        if (interior.Equals("Classic", StringComparison.OrdinalIgnoreCase))
        {
            return new ThemeSpec(
                "'EB Garamond', Georgia, serif",
                "'Playfair Display', Georgia, serif",
                "#111111",
                "#111111",
                "#fdfcf8",
                "font-weight: 400; font-size: 1.5em; font-style: italic; text-align: center; letter-spacing: 0.03em; margin: 2em 0 1.5em;",
                "text-indent: 2em; margin-bottom: 0; text-align: justify;");
        }

        if (interior.Equals("ElegantTrade", StringComparison.OrdinalIgnoreCase))
        {
            return new ThemeSpec(
                "'Merriweather', Georgia, serif",
                "'Merriweather', Georgia, serif",
                "#111111",
                "#1a1a1a",
                "#ffffff",
                "font-weight: 700; font-size: 1.3em; letter-spacing: 0.04em; margin: 1.5em 0 1em; text-align: left;",
                "text-indent: 1.5em; margin-bottom: 0; text-align: justify;");
        }

        // Novel / Traditional
        return new ThemeSpec(
            "'EB Garamond', Georgia, serif",
            "'EB Garamond', Georgia, serif",
            "#111111",
            "#1a1a1a",
            "#fffef9",
            "font-weight: 700; font-size: 1.5em; text-align: center; letter-spacing: 0.02em; margin: 2em 0 1.5em;",
            "text-indent: 2em; margin-bottom: 0; text-align: justify;");
    }

    private sealed record ThemeSpec(
        string BodyFont,
        string HeadingFont,
        string HeadingColor,
        string BodyColor,
        string PageBackground,
        string HeadingRules,
        string ParagraphRules);
}
