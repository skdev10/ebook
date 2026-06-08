using System.Globalization;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>Single source of truth for interior typography in PDF + EPUB — mirrors Book Formatter preview.</summary>
public static class InteriorExportTheme
{
    public static string NormalizeInteriorStyle(string? style)
    {
        var s = (style ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return "Novel";
        if (s.Equals("Traditional", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Novel", StringComparison.OrdinalIgnoreCase))
            return "Novel";
        if (s.Equals("Contemporary", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Modern", StringComparison.OrdinalIgnoreCase))
            return "Novel";
        if (s.Equals("Fine book", StringComparison.OrdinalIgnoreCase)
            || s.Equals("FineBook", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Classic", StringComparison.OrdinalIgnoreCase))
            return "Classic";
        if (s.Equals("Clean", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Minimalist", StringComparison.OrdinalIgnoreCase))
            return "Minimalist";
        if (s.Equals("POD", StringComparison.OrdinalIgnoreCase)
            || s.Equals("Elegant trade", StringComparison.OrdinalIgnoreCase)
            || s.Equals("ElegantTrade", StringComparison.OrdinalIgnoreCase))
            return "ElegantTrade";
        return s;
    }

    public static string NormalizeTextSize(string? textSize)
    {
        var s = (textSize ?? "").Trim();
        if (s.Equals("Small", StringComparison.OrdinalIgnoreCase)) return "Small";
        if (s.Equals("Large", StringComparison.OrdinalIgnoreCase)) return "Large";
        return "Medium";
    }

    public static string NormalizeLineSpacing(string? lineSpacing)
    {
        if (!double.TryParse((lineSpacing ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lh))
            lh = 1.6;
        if (lh <= 1.45) return "1.4";
        if (lh <= 1.7) return "1.6";
        if (lh <= 1.9) return "1.8";
        return "2";
    }

    /// <summary>Body font size in px — matches formatter <c>applyPreviewStyles</c>.</summary>
    public static string ResolveBodyFontSizePx(string? interiorStyle, string? textSize)
    {
        var interior = NormalizeInteriorStyle(interiorStyle);
        var size = NormalizeTextSize(textSize);
        if (interior is "Classic" or "ElegantTrade")
        {
            return size switch
            {
                "Small" => "14",
                "Large" => "19",
                _ => interior == "ElegantTrade" ? "16" : "17"
            };
        }

        return size switch
        {
            "Small" => "14",
            "Large" => "20",
            _ => "16"
        };
    }

    public static string ResolveBodyFontSizePt(string? interiorStyle, string? textSize)
    {
        if (!double.TryParse(ResolveBodyFontSizePx(interiorStyle, textSize), NumberStyles.Integer, CultureInfo.InvariantCulture, out var px))
            px = 16;
        var pt = px * 0.75;
        return pt.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string ResolveLineHeight(string? lineSpacing) =>
        NormalizeLineSpacing(lineSpacing);

    public static string PdfBodyTemplateClass(string? interiorStyle) =>
        NormalizeInteriorStyle(interiorStyle) switch
        {
            "Modern" => "tpl-modern",
            "Minimalist" => "tpl-minimalist",
            "Classic" => "tpl-classic",
            "ElegantTrade" => "tpl-elegant-trade",
            _ => "tpl-novel"
        };

    public static string BuildEpubStylesheet(BookPdfExportOptions opt)
    {
        var interior = NormalizeInteriorStyle(opt.InteriorStyle);
        var fontSize = ResolveBodyFontSizePx(interior, opt.TextSize);
        var lineHeight = ResolveLineHeight(opt.LineSpacing);
        var theme = ResolveTheme(interior);

        var tplCss = interior switch
        {
            "Minimalist" => "body.tpl-minimalist h1 { font-size: 1.15em; font-weight: 600; letter-spacing: -0.02em; border-bottom: 1px solid #e5e5e5; } ",
            "Classic" => "body.tpl-classic h1 { font-variant: small-caps; font-style: italic; letter-spacing: 0.12em; border-bottom: 3px double #d6c4a8; padding-bottom: 0.4em; } body.tpl-classic p:first-of-type::first-letter { float: left; font-size: 3.2em; line-height: 0.85; padding-right: 0.08em; font-weight: 600; color: #78350f; } ",
            "ElegantTrade" => "body.tpl-elegant-trade h1 { font-weight: 600; letter-spacing: 0.04em; color: #3d2914; border-bottom: 1px solid #c9b8a0; padding-bottom: 0.35em; } ",
            _ => "body.tpl-novel h1 { font-weight: 700; color: #6f2f10; border-bottom: 1px solid rgba(111, 47, 16, 0.18); padding-bottom: 0.35em; text-align: center; } "
        };

        return string.Concat(
            "body { ",
            "font-family: ", theme.BodyFont, "; ",
            "font-size: ", fontSize, "px; ",
            "line-height: ", lineHeight, "; ",
            "margin: 0; padding: 0; ",
            "color: ", theme.BodyColor, "; ",
            "background: ", theme.PageBackground, "; ",
            "min-height: 100%; ",
            "orphans: 3; widows: 3; ",
            "} ",
            "html { background: ", theme.PageBackground, "; margin: 0; padding: 0; } ",
            "section, article { background: ", theme.PageBackground, "; } ",
            "h1, h2, h3, h4, h5, h6, .chapter-heading { ",
            "font-family: ", theme.HeadingFont, "; ",
            "color: ", theme.HeadingColor, "; ",
            theme.HeadingRules,
            "} ",
            "p { ",
            theme.ParagraphRules,
            "margin-top: 0; orphans: 3; widows: 3; page-break-inside: avoid; ",
            "} ",
            "p:first-of-type, h1 + p, h2 + p, h4 + p { text-indent: 0; } ",
            theme.ExtraRules,
            "img { max-width: 100%; height: auto; page-break-inside: avoid; break-inside: avoid; } ",
            "[style] { -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            "blockquote { ", theme.BlockquoteRules, " } ",
            tplCss);
    }

    /// <summary>Default book-preview-sheet background per interior (matches Book Formatter).</summary>
    public static string ResolveDefaultPageBackground(string? interiorStyle) =>
        NormalizeInteriorStyle(interiorStyle) switch
        {
            "Classic" => "#fdfcfa",
            "ElegantTrade" => "#fcf9f3",
            "Novel" => "#fffdf8",
            "Modern" => "#ffffff",
            "Minimalist" => "#ffffff",
            _ => "#ffffff"
        };

    /// <summary>PDF interior CSS (embedded in print HTML).</summary>
    public static string BuildPdfThemeCss(BookPdfExportOptions opt)
    {
        var interior = NormalizeInteriorStyle(opt.InteriorStyle);
        var pt = ResolveBodyFontSizePt(interior, opt.TextSize);
        var lh = ResolveLineHeight(opt.LineSpacing);
        var pageBg = opt.ResolvePageBackgroundColor();
        var theme = ResolveTheme(interior) with { PageBackground = pageBg };
        var tpl = PdfBodyTemplateClass(interior);
        var justify = interior is "Modern" or "Minimalist" ? "text-align:left;" : "text-align:justify;";
        var chapterAlign = interior is "Classic" or "Novel" or "ElegantTrade" ? "text-align:center;" : "text-align:left;";

        var accent = NormalizeAccentHex(opt.PreviewAccent);
        var accentCss = string.IsNullOrEmpty(accent)
            ? ""
            : string.Concat("--fmt-accent: ", accent, "; ");

        var baseCss = string.Concat(
            ":root { ",
            "--body-pt: ", pt, "pt; ",
            "--body-lh: ", lh, "; ",
            "--body-font: ", theme.BodyFont, "; ",
            "--heading-font: ", theme.HeadingFont, "; ",
            "--heading-color: ", theme.HeadingColor, "; ",
            "--body-color: ", theme.BodyColor, "; ",
            "--page-bg: ", theme.PageBackground, "; ",
            accentCss,
            "} ",
            "html, body { -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            "@page { background-color: var(--page-bg); } ",
            "html { background-color: var(--page-bg); margin: 0; padding: 0; } ",
            ".book-pdf-body { font-family: var(--body-font); font-size: var(--body-pt); line-height: var(--body-lh); color: var(--body-color); background: var(--page-bg); margin: 0; min-height: 100%; -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            ".front-matter-page, .title-page, .copyright-page, .manuscript-root > section.chapter, ",
            ".reader-chapter-block, .reader-page-title, .reader-page-body { background-color: var(--page-bg) !important; box-sizing: border-box; } ",
            ".manuscript-root > section.chapter { box-sizing: border-box; } ",
            ".manuscript-root > section.chapter { min-height: auto; padding-top: 2mm; padding-bottom: 2mm; } ",
            ".front-matter-page { page-break-after: always; padding-top: 8mm; background: var(--page-bg); min-height: 100vh; } ",
            ".title-page { min-height: 100vh; background: var(--page-bg); } ",
            ".copyright-page .cr-meta { font-size: 12pt; margin: 0 0 4mm; } ",
            ".copyright-page .cr-legal { font-size: 10pt; margin: 6mm 0 3mm; line-height: 1.5; } ",
            ".copyright-page .cr-small { font-size: 9pt; color: #64748b; margin-top: 4mm; line-height: 1.45; } ",
            ".toc-title { font-family: var(--heading-font); font-size: 20pt; margin: 0 0 6mm; color: var(--heading-color); font-weight: 600; } ",
            ".toc-hint { font-size: 9pt; color: #64748b; margin: 0 0 8mm; } ",
            ".toc-list { margin: 0; padding-left: 5mm; } ",
            ".toc-item { margin: 0 0 5mm; font-size: 11pt; list-style-position: outside; } ",
            ".toc-chapter-line { font-weight: 600; margin: 0 0 2mm; display: flex; align-items: baseline; gap: 6mm; } ",
            ".toc-chapter-line::after { content: ''; flex: 1 1 auto; border-bottom: 1px dotted #94a3b8; transform: translateY(-2px); } ",
            ".toc-page-ref { font-weight: 600; min-width: 9mm; text-align: right; color: #334155; } ",
            ".toc-subheadings { list-style: none; padding-left: 8mm; margin: 0 0 2mm; } ",
            ".toc-subheading-item { font-size: 10pt; color: #334155; margin: 0 0 1.8mm; font-weight: 400; line-height: 1.35; } ",
            ".toc-heading-prefix { font-weight: 600; color: #0f172a; margin-right: 2mm; } ",
            ".toc-link { color: #0f172a; text-decoration: none; } ",
            ".title-page .subtitle { font-size: 12pt; color: #64748b; margin-top: 4mm; } ",
            ".chapter-heading { font-family: var(--heading-font); font-size: 15pt; margin: 0 0 6mm; color: var(--heading-color); padding-bottom: 2mm; ",
            chapterAlign, " ", theme.ChapterHeadingExtra, " } ",
            ".export-meta { font-size: 9pt; color: #64748b; margin: 0 0 4mm; line-height: 1.45; } ",
            ".chapter-body { ", justify, " } ",
            ".manuscript-h1 { font-size: 16pt; margin: 5mm 0 3mm; font-family: var(--heading-font); color: var(--heading-color); } ",
            ".manuscript-h2 { font-size: 14pt; margin: 4mm 0 2mm; font-family: var(--heading-font); color: var(--heading-color); } ",
            ".manuscript-h3 { font-size: 12pt; margin: 3mm 0 2mm; font-family: var(--heading-font); color: var(--heading-color); } ",
            ".manuscript-h4 { font-size: 11pt; margin: 2mm 0 1mm; font-family: var(--heading-font); color: var(--heading-color); } ",
            ".manuscript-p { margin: 0 0 3mm; orphans: 3; widows: 3; page-break-inside: avoid; ", theme.ParagraphRules, " } ",
            ".manuscript-hr { border: none; border-top: 1px solid #cbd5e1; margin: 6mm 0; } ",
            "blockquote { ", theme.BlockquoteRules, " } ",
            ".chapter-body h1, .chapter-body h2, .chapter-body h3, .chapter-body h4, .chapter-body h5, .chapter-body h6, ",
            ".chapter-body .manuscript-h1, .chapter-body .manuscript-h2, .chapter-body .manuscript-h3, .chapter-body .manuscript-h4 { ",
            "page-break-before: avoid !important; break-before: avoid !important; page-break-after: avoid; break-after: avoid; ",
            "page-break-inside: avoid; break-inside: avoid; } ",
            ".manuscript-root > section.chapter:first-of-type { break-before: auto; page-break-before: auto; } ",
            ".manuscript-root > section.chapter ~ section.chapter { break-before: page; page-break-before: always; } ",
            ".manuscript-root > section.chapter:last-of-type { break-after: auto; page-break-after: auto; } ",
            ".chapter-body .manuscript-chapter-heading, .chapter-body .manuscript-heading { font-family: var(--heading-font); color: var(--heading-color); } ",
            ".chapter-body .manuscript-h1 { font-size: 16pt; margin: 5mm 0 3mm; } ",
            ".chapter-body .manuscript-h2 { font-size: 14pt; margin: 4mm 0 2mm; } ",
            ".chapter-body .manuscript-h3 { font-size: 12pt; margin: 3mm 0 2mm; } ",
            ".chapter-body .manuscript-h4 { font-size: 11pt; margin: 2mm 0 1mm; } ",
            "img { max-width: 100%; height: auto; page-break-inside: avoid; break-inside: avoid; } ",
            "[style] { -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            ".book-pdf-body.interior-modern .reader-page-body p { border-left-color: var(--fmt-accent, #6366f1); } ",
            ".book-pdf-body blockquote { border-left-color: var(--fmt-accent, #c4b5fd); } ",
            BuildPreviewMatchedReaderCss(interior, justify, chapterAlign, theme));

        var tplCss = interior switch
        {
            "Modern" => ".book-pdf-body.tpl-modern .reader-page-title { font-size: 11pt; font-weight: 800; text-transform: uppercase; letter-spacing: 0.18em; border-bottom: none; border-left: 4px solid #6366f1; padding-left: 4mm; text-align: left; } ",
            "Minimalist" => ".book-pdf-body.tpl-minimalist .reader-page-title { font-size: 13pt; font-weight: 600; border-bottom: 1px solid #e5e5e5; letter-spacing: -0.02em; text-align: left; } ",
            "Classic" => ".book-pdf-body.tpl-classic .reader-page-title { font-size: 17pt; font-variant: small-caps; font-style: italic; letter-spacing: 0.12em; border-bottom: 3px double #d6c4a8; padding-bottom: 5mm; text-align: center; } ",
            "ElegantTrade" => ".book-pdf-body.tpl-elegant-trade .reader-page-title { font-size: 16pt; font-weight: 600; letter-spacing: 0.04em; color: #3d2914; border-bottom: 1px solid #c9b8a0; padding-bottom: 5mm; text-align: center; } ",
            _ => ".book-pdf-body.tpl-novel .reader-page-title { font-size: 16pt; font-weight: 700; letter-spacing: 0.015em; border-bottom: 1px solid rgba(111, 47, 16, 0.18); color: #6f2f10; padding-bottom: 3mm; text-align: center; } "
        };

        return baseCss + tplCss + BuildFormatterInteriorCss();
    }

    /// <summary>Interior shell rules — mirrors Book Formatter preview (<c>interior-*</c> on body).</summary>
    public static string BuildFormatterInteriorCss() =>
        string.Concat(
            ".book-pdf-body.interior-novel .reader-page-title { font-family: 'Playfair Display', Georgia, serif; color: #6f2f10; font-weight: 700; text-align: center; border-bottom: 1px solid rgba(111, 47, 16, 0.18); } ",
            ".book-pdf-body.interior-novel .reader-page-body { font-family: 'Merriweather', Georgia, serif; color: #2c2118; } ",
            ".book-pdf-body.interior-novel .reader-page-body p { text-indent: 1.5em; margin-bottom: 0.95em; } ",
            ".book-pdf-body.interior-modern .reader-page-title { font-family: 'Inter', system-ui, sans-serif; text-transform: uppercase; letter-spacing: 0.18em; font-weight: 800; color: #334155; text-align: left; border-bottom: none; } ",
            ".book-pdf-body.interior-modern .reader-page-body { font-family: 'Inter', system-ui, sans-serif; color: #334155; } ",
            ".book-pdf-body.interior-modern .reader-page-body p { text-indent: 0; border-left: 3px solid #6366f1; padding-left: 0.9em; margin-bottom: 0.85em; } ",
            ".book-pdf-body.interior-classic .reader-page-title { font-family: 'Cormorant Garamond', 'Times New Roman', Times, serif; font-style: italic; font-variant: small-caps; letter-spacing: 0.12em; text-align: center; border-bottom: 3px double #d6c4a8; } ",
            ".book-pdf-body.interior-classic .reader-page-body { font-family: 'Cormorant Garamond', 'Times New Roman', Times, serif; text-align: justify; color: #231f1a; } ",
            ".book-pdf-body.interior-classic .reader-page-body.classic-body .manuscript-p:first-of-type::first-letter { float: left; font-size: 3.4em; line-height: 0.8; padding-right: 0.1em; font-weight: 600; color: #111; font-family: 'Cormorant Garamond', 'Times New Roman', Times, serif; } ",
            ".book-pdf-body.interior-minimalist .reader-page-title { font-family: 'Inter', system-ui, sans-serif; font-weight: 600; color: #111827; text-align: left; border-bottom: 1px solid #ececec; } ",
            ".book-pdf-body.interior-minimalist .reader-page-body { font-family: 'Inter', system-ui, sans-serif; color: #3f3f46; } ",
            ".book-pdf-body.interior-minimalist .reader-page-body p { text-indent: 0; margin-bottom: 1.1em; } ",
            ".book-pdf-body.interior-elegant-trade .reader-page-title { font-family: 'Lora', 'Times New Roman', serif; font-weight: 600; letter-spacing: 0.06em; color: #3d2914; text-align: center; border-bottom: 1px solid #c8b08e; } ",
            ".book-pdf-body.interior-elegant-trade .reader-page-body { font-family: 'EB Garamond', Palatino, Georgia, serif; text-align: justify; color: #29211b; } ",
            ".book-pdf-body.interior-elegant-trade .reader-page-body p { text-indent: 1.35em; margin-bottom: 0.95em; } ",
            ".book-pdf-body.interior-elegant-trade .reader-page-body .manuscript-heading { font-family: 'Lora', serif; color: #4a3728; text-indent: 0; } ");

    private static string BuildPreviewMatchedReaderCss(string interior, string justify, string chapterAlign, ThemeSpec theme) =>
        string.Concat(
            ".reader-chapter-block { margin: 0; padding: 0; } ",
            ".reader-page-title { font-family: var(--heading-font); color: var(--heading-color); margin: 0 0 6mm; padding-bottom: 2mm; ",
            chapterAlign, " ", theme.ChapterHeadingExtra, " } ",
            ".reader-page-body { ", justify, " margin: 0; padding: 0; } ",
            ".reader-page-body p, .reader-page-body .manuscript-p { margin: 0 0 3mm; orphans: 3; widows: 3; ",
            theme.ParagraphRules, " } ",
            ".reader-page-body p:first-of-type, .reader-page-title + .reader-page-body p:first-of-type { text-indent: 0; } ",
            interior == "Classic"
                ? ".book-pdf-body.tpl-classic .reader-page-body.classic-body .manuscript-p:first-of-type::first-letter { float: left; font-size: 3.2em; line-height: 0.85; padding-right: 0.08em; font-weight: 600; color: #78350f; } "
                : "");

    private static string? NormalizeAccentHex(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        return System.Text.RegularExpressions.Regex.IsMatch(s, @"^#[0-9A-Fa-f]{6}$") ? s : null;
    }

    private static ThemeSpec ResolveTheme(string interior) => interior switch
    {
        "Modern" => new ThemeSpec(
            "'Inter', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif",
            "'Inter', system-ui, Roboto, 'Helvetica Neue', Arial, sans-serif",
            "#334155", "#334155", "#ffffff",
            "font-weight: 800; margin: 1.2em 0 0.8em;",
            "text-indent: 0; margin-bottom: 0.85em; padding-left: 0.9em; border-left: 3px solid #6366f1;",
            "border-left: 3px solid #6366f1; margin: 3mm 0 3mm 4mm; padding-left: 4mm; color: #334155;",
            "border-bottom: none;",
            ""),
        "Minimalist" => new ThemeSpec(
            "'Inter', system-ui, Roboto, Arial, sans-serif",
            "'Inter', system-ui, Roboto, Arial, sans-serif",
            "#111111", "#1a1a1a", "#ffffff",
            "font-weight: 600; font-size: 1.15em; letter-spacing: -0.02em; margin: 1.2em 0 0.8em;",
            "text-indent: 0; margin-bottom: 0.8em;",
            "border-left: 2px solid #e5e5e5; margin: 3mm 0; padding-left: 4mm; color: #334155;",
            "border-bottom: 1px solid #e5e5e5;",
            ""),
        "Classic" => new ThemeSpec(
            "'Cormorant Garamond', 'Palatino Linotype', Palatino, Georgia, 'Times New Roman', Times, serif",
            "'Cormorant Garamond', 'Palatino Linotype', Palatino, Georgia, 'Times New Roman', Times, serif",
            "#78350f", "#111111", "#fdfcfa",
            "font-weight: 400; font-size: 1.25em; font-style: italic; letter-spacing: 0.03em; margin: 1.5em 0 1em;",
            "text-indent: 2em; margin-bottom: 0;",
            "border-left: 3px solid #d6c4a8; margin: 3mm 0 3mm 6mm; padding-left: 4mm; color: #334155;",
            "border-bottom: 3px double #d6c4a8;",
            ".book-pdf-body.tpl-classic .manuscript-p:first-of-type::first-letter { float: left; font-size: 3.2em; line-height: 0.85; padding-right: 0.08em; font-weight: 600; color: #78350f; } "),
        "ElegantTrade" => new ThemeSpec(
            "'EB Garamond', 'Palatino Linotype', Palatino, Georgia, 'Times New Roman', Times, serif",
            "'Lora', 'Merriweather', Georgia, 'Times New Roman', Times, serif",
            "#3d2914", "#1a1a1a", "#fcf9f3",
            "font-weight: 600; font-size: 1.2em; letter-spacing: 0.04em; margin: 1.3em 0 0.9em;",
            "text-indent: 1.5em; margin-bottom: 0;",
            "border-left: 3px solid #c9b8a0; margin: 3mm 0 3mm 6mm; padding-left: 4mm; color: #334155;",
            "border-bottom: 1px solid #c9b8a0;",
            ""),
        _ => new ThemeSpec(
            "'Merriweather', Georgia, 'Times New Roman', Times, serif",
            "'Playfair Display', Georgia, 'Times New Roman', Times, serif",
            "#6f2f10", "#2c2118", "#fffdf8",
            "font-weight: 700; font-size: 1.2em; letter-spacing: 0.015em; margin: 1.5em 0 0.8em;",
            "text-indent: 1.5em; margin-bottom: 0.95em;",
            "border-left: 3px solid #c4b5fd; margin: 3mm 0 3mm 6mm; padding-left: 4mm; color: #334155;",
            "border-bottom: 1px solid rgba(111, 47, 16, 0.18);",
            "")
    };

    private sealed record ThemeSpec(
        string BodyFont,
        string HeadingFont,
        string HeadingColor,
        string BodyColor,
        string PageBackground,
        string HeadingRules,
        string ParagraphRules,
        string BlockquoteRules,
        string ChapterHeadingExtra,
        string ExtraRules);
}
