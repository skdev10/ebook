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

    /// <summary>Exact multiplier shared with formatter <c>--fmt-line-height</c> (1.4 / 1.6 / 1.8 / 2).</summary>
    public static string ResolveLineHeight(string? lineSpacing) =>
        InteriorLayoutTokens.ResolveLineHeightExact(lineSpacing);

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

    /// <summary>CSS keep-with-next: in-chapter headings stay with the following block (preview + Chromium PDF).</summary>
    public static string BuildHeadingKeepWithNextCss() =>
        string.Concat(
            ".reader-page-body .manuscript-heading, .reader-page-body .manuscript-h1, .reader-page-body .manuscript-h2, ",
            ".reader-page-body .manuscript-h3, .reader-page-body .manuscript-h4, .reader-page-body .manuscript-h5, .reader-page-body .manuscript-h6, ",
            ".reader-page-body h1, .reader-page-body h2, .reader-page-body h3, .reader-page-body h4, .reader-page-body h5, .reader-page-body h6, ",
            ".chapter-body .manuscript-heading, .chapter-body h1, .chapter-body h2, .chapter-body h3, .chapter-body h4, .chapter-body h5, .chapter-body h6 { ",
            "break-after: avoid !important; page-break-after: avoid !important; break-inside: avoid; page-break-inside: avoid; } ",
            ".reader-page-body .manuscript-heading + *, .reader-page-body h1 + *, .reader-page-body h2 + *, .reader-page-body h3 + *, ",
            ".reader-page-body h4 + *, .reader-page-body h5 + *, .reader-page-body h6 + *, ",
            ".chapter-body .manuscript-heading + *, .chapter-body h1 + *, .chapter-body h2 + *, .chapter-body h3 + *, ",
            ".chapter-body h4 + *, .chapter-body h5 + *, .chapter-body h6 + * { ",
            "break-before: avoid !important; page-break-before: avoid !important; } ");

    /// <summary>PDF interior CSS (embedded in print HTML) — layout numbers from <see cref="InteriorLayoutTokens"/>.</summary>
    public static string BuildPdfThemeCss(BookPdfExportOptions opt)
    {
        var interior = NormalizeInteriorStyle(opt.InteriorStyle);
        var pageBg = opt.ResolvePageBackgroundColor();
        var theme = ResolveTheme(interior) with { PageBackground = pageBg };
        var accent = NormalizeAccentHex(opt.PreviewAccent);
        var accentCss = string.IsNullOrEmpty(accent)
            ? ""
            : string.Concat("--fmt-accent: ", accent, "; ");

        var fontVars = string.Concat(
            "--body-font: ", theme.BodyFont, "; ",
            "--heading-font: ", theme.HeadingFont, "; ",
            "--heading-color: ", theme.HeadingColor, "; ",
            "--body-color: ", theme.BodyColor, "; ",
            "--page-bg: ", pageBg, "; ",
            accentCss);

        var baseCss = string.Concat(
            InteriorLayoutTokens.BuildCssCustomProperties(opt),
            ":root { ", fontVars, "} ",
            "html, body { -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            "@page { background-color: var(--page-bg); } ",
            "html { background-color: var(--page-bg); margin: 0; padding: 0; } ",
            ".book-pdf-body { font-family: var(--body-font); font-size: var(--ilt-body-pt); line-height: var(--ilt-body-lh); ",
            "color: var(--body-color); background: var(--page-bg); margin: 0; min-height: 100%; -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            ".front-matter-page, .title-page, .copyright-page, .manuscript-root > section.chapter, ",
            ".reader-chapter-block, .reader-page-title, .reader-page-body { background-color: var(--page-bg) !important; box-sizing: border-box; } ",
            InteriorLayoutTokens.BuildSharedReaderLayoutCss(),
            InteriorLayoutTokens.BuildTocCss(),
            ".front-matter-page { background: var(--page-bg); min-height: 100vh; } ",
            ".title-page { min-height: 100vh; background: var(--page-bg); padding: var(--ilt-title-page-pad-top) var(--ilt-pad-right) var(--ilt-pad-bottom) var(--ilt-pad-left); ",
            "display: flex; flex-direction: column; justify-content: center; text-align: center; page-break-after: always; } ",
            ".title-page h1 { max-width: var(--ilt-text-max); margin-inline: auto; } ",
            ".title-page .subtitle { font-size: 12pt; color: #64748b; margin-top: 4mm; } ",
            ".title-page-author { font-size: 13pt; margin: 16mm 0 3mm; letter-spacing: 0.06em; } ",
            ".title-page-genre { font-size: 9pt; color: #8a8175; text-transform: uppercase; letter-spacing: 0.22em; margin-top: 3mm; } ",
            ".reader-page-title { font-family: var(--heading-font); color: var(--heading-color); font-weight: 600; } ",
            ".export-meta { font-size: 9pt; color: #64748b; margin: 0 0 4mm; line-height: 1.45; } ",
            ".manuscript-h1 { font-size: 16pt; margin: 5mm 0 3mm; font-family: var(--heading-font); color: var(--heading-color); } ",
            ".manuscript-h2 { font-size: 14pt; margin: 4mm 0 2mm; font-family: var(--heading-font); color: var(--heading-color); } ",
            ".manuscript-h3 { font-size: 12pt; margin: 3mm 0 2mm; font-family: var(--heading-font); color: var(--heading-color); } ",
            ".manuscript-h4 { font-size: 11pt; margin: 2mm 0 1mm; font-family: var(--heading-font); color: var(--heading-color); } ",
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
            "img { max-width: 100%; height: auto; page-break-inside: avoid; break-inside: avoid; } ",
            "[style] { -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            ".book-pdf-body.interior-modern .reader-page-body p { border-left-color: var(--fmt-accent, #6366f1); } ",
            ".book-pdf-body blockquote { border-left-color: var(--fmt-accent, #c4b5fd); } ",
            ".reader-content-wrap { background: var(--page-bg); -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            /* Sheet padding is folded into Chromium margins — must not apply only on the first page fragment. */
            ".book-pdf-body .book-preview-sheet { padding: 0 !important; } ",
            ".book-pdf-body .title-page { padding-left: 0; padding-right: 0; } ",
            ".book-pdf-body .copyright-page, .book-pdf-body .toc-page { padding-left: 0; padding-right: 0; } ");

        var tplCss = interior switch
        {
            "Modern" => ".book-pdf-body.tpl-modern .reader-page-title { font-weight: 800; border-left: 4px solid #6366f1; padding-left: 4mm; } ",
            "Classic" => ".book-pdf-body.tpl-classic .reader-page-title { font-variant: small-caps; font-style: italic; letter-spacing: 0.12em; border-bottom: 3px double #d6c4a8; } ",
            _ => ""
        };

        return baseCss + tplCss + InteriorLayoutTokens.BuildFrameAndSheetCss()
               + InteriorLayoutTokens.BuildPerInteriorCss(interior)
               + BuildFormatterInteriorCss() + BuildHeadingKeepWithNextCss();
    }

    /// <summary>Legacy hook — sheet padding now lives in <see cref="InteriorLayoutTokens"/>.</summary>
    public static string BuildBookPreviewSheetPrintCss(string interior, string pageBg) =>
        InteriorLayoutTokens.BuildSharedReaderLayoutCss();

    /// <summary>Interior typography — identical in Book Formatter preview and PDF export.</summary>
    public static string BuildFormatterInteriorCss() =>
        string.Concat(
            BuildInteriorTypographyCss(".book-pdf-body"),
            BuildInteriorTypographyCss("#book-formatter-root #paginatedReaderShell"));

    private static string BuildInteriorTypographyCss(string scope) =>
        string.Concat(
            scope, ".interior-novel .reader-page-title { font-family: 'Playfair Display', Georgia, serif; color: #6f2f10; font-weight: 700; text-align: center; border-bottom: 1px solid rgba(111, 47, 16, 0.18); } ",
            scope, ".interior-novel .reader-page-body { font-family: 'Merriweather', Georgia, serif; color: #2c2118; } ",
            scope, ".interior-novel .reader-page-body p { text-indent: var(--ilt-text-indent); margin-bottom: var(--ilt-para-space); } ",
            scope, ".interior-modern .reader-page-title { font-family: 'Inter', system-ui, sans-serif; text-transform: uppercase; letter-spacing: 0.18em; font-weight: 800; color: #334155; text-align: left; border-bottom: none; } ",
            scope, ".interior-modern .reader-page-body { font-family: 'Inter', system-ui, sans-serif; color: #334155; } ",
            scope, ".interior-modern .reader-page-body p { text-indent: 0; border-left: 3px solid #6366f1; padding-left: 0.9em; margin-bottom: 0.85em; } ",
            scope, ".interior-classic .reader-page-title { font-family: 'Cormorant Garamond', 'Times New Roman', Times, serif; font-style: italic; font-variant: small-caps; letter-spacing: 0.12em; text-align: center; border-bottom: 3px double #d6c4a8; } ",
            scope, ".interior-classic .reader-page-body { font-family: 'Cormorant Garamond', 'Times New Roman', Times, serif; text-align: justify; color: #231f1a; } ",
            scope, ".interior-classic .reader-page-body.classic-body .manuscript-p:first-of-type::first-letter, ",
            scope, ".interior-classic .reader-page-body p.classic-first-para::first-letter { float: left; font-size: 3.4em; line-height: 0.8; padding-right: 0.1em; font-weight: 600; color: #111; font-family: 'Cormorant Garamond', 'Times New Roman', Times, serif; } ",
            scope, ".interior-minimalist .reader-page-title { font-family: 'Inter', system-ui, sans-serif; font-weight: 600; color: #111827; text-align: left; border-bottom: 1px solid #ececec; } ",
            scope, ".interior-minimalist .reader-page-body { font-family: 'Inter', system-ui, sans-serif; color: #3f3f46; } ",
            scope, ".interior-minimalist .reader-page-body p { text-indent: 0; margin-bottom: 1.1em; } ",
            scope, ".interior-elegant-trade .reader-page-title { font-family: 'Lora', 'Times New Roman', serif; font-weight: 600; letter-spacing: 0.06em; color: #3d2914; text-align: center; border-bottom: 1px solid #c8b08e; } ",
            scope, ".interior-elegant-trade .reader-page-body { font-family: 'EB Garamond', Palatino, Georgia, serif; text-align: justify; color: #29211b; } ",
            scope, ".interior-elegant-trade .reader-page-body p { text-indent: var(--ilt-text-indent); margin-bottom: var(--ilt-para-space); } ",
            scope, ".interior-elegant-trade .reader-page-body .manuscript-heading { font-family: 'Lora', serif; color: #4a3728; text-indent: 0; } ");

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
