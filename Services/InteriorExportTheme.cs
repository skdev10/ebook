using System.Globalization;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>Single source of truth for interior typography in PDF + EPUB — mirrors Book Formatter preview.</summary>
public static class InteriorExportTheme
{
    public static string NormalizeInteriorStyle(string? style)
    {
        var s = (style ?? "").Trim().Replace(" ", "");
        if (string.IsNullOrEmpty(s)) return "Novel";

        // 11 distinct Canva-inspired interior styles (each visually unique).
        if (s.Equals("Traditional", StringComparison.OrdinalIgnoreCase))
            return "Traditional";
        if (s.Equals("Novel", StringComparison.OrdinalIgnoreCase))
            return "Novel";
        if (s.Equals("Contemporary", StringComparison.OrdinalIgnoreCase))
            return "Contemporary";
        if (s.Equals("Modern", StringComparison.OrdinalIgnoreCase))
            return "Modern";
        if (s.Equals("Finebook", StringComparison.OrdinalIgnoreCase))
            return "FineBook";
        if (s.Equals("Classic", StringComparison.OrdinalIgnoreCase))
            return "Classic";
        if (s.Equals("Clean", StringComparison.OrdinalIgnoreCase))
            return "Clean";
        if (s.Equals("Minimalist", StringComparison.OrdinalIgnoreCase)
            || s.Equals("CleanMinimalist", StringComparison.OrdinalIgnoreCase))
            return "Minimalist";
        // Order matters: match the POD Elegant Trade variant before plain Elegant Trade / POD.
        if (s.Equals("ElegantTradePOD", StringComparison.OrdinalIgnoreCase)
            || s.Equals("ElegantTrade(POD)", StringComparison.OrdinalIgnoreCase)
            || s.Equals("PODElegantTrade", StringComparison.OrdinalIgnoreCase)
            || s.Equals("PODElegant Trade", StringComparison.OrdinalIgnoreCase))
            return "ElegantTradePOD";
        if (s.Equals("POD", StringComparison.OrdinalIgnoreCase))
            return "POD";
        if (s.Equals("ElegantTrade", StringComparison.OrdinalIgnoreCase))
            return "ElegantTrade";
        return "Novel";
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
    public static string ResolveBodyFontSizePx(string? interiorStyle, string? textSize) =>
        InteriorTypographyPresets.ResolveBodyFontSizePx(interiorStyle, textSize);

    public static string ResolveBodyFontSizePt(string? interiorStyle, string? textSize) =>
        InteriorTypographyPresets.ResolveBodyFontSizePt(interiorStyle, textSize);

    /// <summary>Exact multiplier shared with formatter <c>--fmt-line-height</c> (1.4 / 1.6 / 1.8 / 2).</summary>
    public static string ResolveLineHeight(string? lineSpacing) =>
        InteriorLayoutTokens.ResolveLineHeightExact(lineSpacing);

    public static string PdfBodyTemplateClass(string? interiorStyle) =>
        NormalizeInteriorStyle(interiorStyle) switch
        {
            "Modern" => "tpl-modern",
            "Contemporary" => "tpl-contemporary",
            "Minimalist" => "tpl-minimalist",
            "Clean" => "tpl-clean",
            "Classic" => "tpl-classic",
            "FineBook" => "tpl-fine-book",
            "Traditional" => "tpl-traditional",
            "POD" => "tpl-pod",
            "ElegantTrade" => "tpl-elegant-trade",
            "ElegantTradePOD" => "tpl-elegant-trade-pod",
            _ => "tpl-novel"
        };

    public static string BuildEpubStylesheet(BookPdfExportOptions opt)
    {
        var interior = NormalizeInteriorStyle(opt.InteriorStyle);
        var fontSize = ResolveBodyFontSizePx(interior, opt.TextSize);
        var lineHeight = ResolveLineHeight(opt.LineSpacing);
        var pageBg = opt.ResolvePageBackgroundColor();
        var theme = ResolveTheme(interior) with { PageBackground = pageBg };
        var accent = NormalizeAccentHex(opt.PreviewAccent);
        var accentCss = string.IsNullOrEmpty(accent)
            ? ""
            : string.Concat("a { color: ", accent, "; } h1, h2, h3 { border-color: ", accent, "; } ");

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
            accentCss,
            "img { max-width: 100%; height: auto; page-break-inside: avoid; break-inside: avoid; } ",
            "[style] { -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            "blockquote { ", theme.BlockquoteRules, " } ",
            tplCss);
    }

    /// <summary>Theme color/font tokens for AI Writer preview (#bookResult) — pairs with <see cref="InteriorLayoutTokens.BuildFormatterSyncCss"/>.</summary>
    public static string BuildAiWriterThemeBridgeCss(BookPdfExportOptions opt)
    {
        var interior = NormalizeInteriorStyle(opt.InteriorStyle);
        var pageBg = opt.ResolvePageBackgroundColor();
        var theme = ResolveTheme(interior) with { PageBackground = pageBg };
        var accent = NormalizeAccentHex(opt.PreviewAccent);
        var accentCss = string.IsNullOrEmpty(accent) ? "" : string.Concat("--fmt-accent: ", accent, "; ");

        return string.Concat(
            ":root, #bookResult { ",
            "--page-bg: ", pageBg, "; ",
            "--body-color: ", theme.BodyColor, "; ",
            "--heading-color: ", theme.HeadingColor, "; ",
            "--body-font: ", theme.BodyFont, "; ",
            "--heading-font: ", theme.HeadingFont, "; ",
            "--ilt-page-bg: ", pageBg, "; ",
            "--export-page-bg: ", pageBg, "; ",
            accentCss,
            "} ",
            "#bookResult[data-interior-mode=\"web\"] #preview-content.chapter-preview-viewport { ",
            "background: var(--page-bg) !important; color: var(--body-color) !important; ",
            "font-family: var(--body-font) !important; font-size: var(--ilt-body-px, inherit) !important; ",
            "line-height: var(--ilt-body-lh, inherit) !important; -webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            "#bookResult #preview-content .reader-page-body, ",
            "#bookResult #preview-content .reader-page-body p, ",
            "#bookResult #preview-content .reader-page-body .manuscript-p { ",
            "color: var(--body-color) !important; font-family: var(--body-font) !important; ",
            "font-size: var(--ilt-body-pt, var(--ilt-body-px, inherit)) !important; ",
            "line-height: var(--ilt-body-lh, inherit) !important; text-align: var(--ilt-text-align, inherit) !important; ",
            "-webkit-print-color-adjust: exact; print-color-adjust: exact; } ",
            "#bookResult #preview-content .reader-page-title, ",
            "#bookResult #preview-content .manuscript-chapter-heading.reader-page-title { ",
            "font-family: var(--heading-font) !important; color: var(--heading-color) !important; } ",
            "#bookResult[data-interior-mode=\"web\"] .front-matter-page, ",
            "#bookResult[data-interior-mode=\"web\"] .writer-cover-page { ",
            "background: var(--page-bg) !important; -webkit-print-color-adjust: exact; print-color-adjust: exact; } ");
    }

    /// <summary>Default book-preview-sheet background per interior (matches Book Formatter).</summary>
    public static string ResolveDefaultPageBackground(string? interiorStyle) =>
        NormalizeInteriorStyle(interiorStyle) switch
        {
            "Classic" => "#ffffff",
            "FineBook" => "#fdf8f2",
            "ElegantTrade" => "#fdfbf7",
            "ElegantTradePOD" => "#fdfbf7",
            "Novel" => "#ffffff",
            "Traditional" => "#ffffff",
            "Modern" => "#ffffff",
            "Contemporary" => "#ffffff",
            "Clean" => "#ffffff",
            "Minimalist" => "#ffffff",
            "POD" => "#ffffff",
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
        var defaultAccent = interior switch
        {
            "ElegantTrade" or "ElegantTradePOD" or "FineBook" => "#C9A84C",
            "Contemporary" => "#3B82F6",
            _ => null
        };
        var accent = NormalizeAccentHex(opt.PreviewAccent) ?? defaultAccent;
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
            InteriorLayoutTokens.BuildTocCss(opt),
            ".front-matter-page { background: var(--page-bg); min-height: 100vh; } ",
            ".title-page { min-height: 100vh; background: var(--page-bg); padding: var(--ilt-title-page-pad-top) var(--ilt-pad-right) var(--ilt-pad-bottom) var(--ilt-pad-left); ",
            "display: flex; flex-direction: column; justify-content: center; text-align: center; page-break-after: always; } ",
            ".title-page h1 { max-width: var(--ilt-text-max); margin-inline: auto; } ",
            ".title-page .subtitle { font-size: 12pt; color: #64748b; margin-top: 4mm; } ",
            ".title-page-author { font-size: 13pt; margin: 16mm 0 3mm; letter-spacing: 0.06em; } ",
            ".title-page-genre { font-size: 9pt; color: #8a8175; text-transform: uppercase; letter-spacing: 0.22em; margin-top: 3mm; } ",
            ".reader-page-title { font-family: var(--heading-font); color: var(--heading-color); font-weight: 600; } ",
            ".book-pdf-body .export-meta { display: none !important; } ",
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
            InteriorLayoutTokens.BuildPdfInContentPageChromeCss(),
            ".book-pdf-body .title-page { padding-left: var(--ilt-pad-left); padding-right: var(--ilt-pad-right); } ");

        var tplCss = interior switch
        {
            "Modern" => ".book-pdf-body.tpl-modern .reader-page-title { font-weight: 800; border-left: 4px solid #6366f1; padding-left: 4mm; } ",
            "Classic" => ".book-pdf-body.tpl-classic .reader-page-title { font-variant: small-caps; font-style: italic; letter-spacing: 0.12em; border-bottom: 3px double #d6c4a8; } ",
            _ => ""
        };

        return baseCss + tplCss + InteriorLayoutTokens.BuildFrameAndSheetCss()
               + InteriorLayoutTokens.BuildPerInteriorCss(interior)
               + BuildFormatterInteriorCss() + BuildHeadingKeepWithNextCss()
               + BuildPdfInteriorParityCss(interior)
               + BuildFormatterChapterOpenerPdfCss(interior)
               + BuildRunningChromeCompensationCss(opt, interior);
    }

    /// <summary>Chapter opener CSS for PDF — mirrors Book Formatter <c>fmt-chapter-opener</c> per interior style.</summary>
    private static string BuildFormatterChapterOpenerPdfCss(string interior)
    {
        var wrap = InteriorPrintDocumentBuilder.PreviewInteriorWrapClass(interior);
        var gold = "#C9A84C";
        var blue = "#3B82F6";
        var sb = new System.Text.StringBuilder(8192);

        sb.Append(".book-pdf-body .reader-page-title:has(.fmt-chapter-opener) { border: none !important; padding-top: 0 !important; text-align: inherit; background: transparent; } ");
        sb.Append(".book-pdf-body .fmt-chapter-opener { display: block; text-align: center; margin: 0 0 0.35in; padding: 0; border: 0; line-height: 1.2; } ");
        sb.Append(".book-pdf-body .fmt-chapter-opener .fmt-ch-flourish { display: none; } ");
        sb.Append(".book-pdf-body .fmt-chapter-opener .fmt-ch-rule { display: none; } ");
        sb.Append(".book-pdf-body .fmt-chapter-opener .fmt-ch-eyebrow { display: block; font-size: 9pt; letter-spacing: 0.22em; text-transform: uppercase; opacity: 0.72; margin-bottom: 0.16in; font-weight: 600; } ");
        sb.Append(".book-pdf-body .fmt-chapter-opener .fmt-ch-title { display: block; font-family: var(--heading-font); font-weight: 600; font-size: 16pt; line-height: 1.15; margin: 0; } ");

        // Novel / Traditional family
        sb.Append(".book-pdf-body.interior-novel .fmt-chapter-opener .fmt-ch-eyebrow, .book-pdf-body.interior-traditional .fmt-chapter-opener .fmt-ch-eyebrow { font-variant: small-caps; color: #6b5742; } ");
        sb.Append(".book-pdf-body.interior-novel .fmt-chapter-opener .fmt-ch-title, .book-pdf-body.interior-traditional .fmt-chapter-opener .fmt-ch-title { font-variant: small-caps; letter-spacing: 0.04em; color: #2a2620; text-align: center; } ");

        // Classic / Fine Book
        sb.Append(".book-pdf-body.interior-classic .fmt-chapter-opener .fmt-ch-flourish, .book-pdf-body.interior-fine-book .fmt-chapter-opener .fmt-ch-flourish { display: block; font-size: 14pt; color: #8a6f3f; margin-bottom: 0.12in; } ");
        sb.Append(".book-pdf-body.interior-classic .fmt-chapter-opener .fmt-ch-eyebrow, .book-pdf-body.interior-fine-book .fmt-chapter-opener .fmt-ch-eyebrow { font-style: italic; text-transform: none; letter-spacing: 0.1em; color: #5f4a33; } ");
        sb.Append(".book-pdf-body.interior-classic .fmt-chapter-opener .fmt-ch-title, .book-pdf-body.interior-fine-book .fmt-chapter-opener .fmt-ch-title { font-style: italic; font-size: 18pt; color: #241c16; } ");

        // Minimalist / Clean / Modern
        sb.Append(".book-pdf-body.interior-minimalist .fmt-chapter-opener, .book-pdf-body.interior-clean .fmt-chapter-opener, .book-pdf-body.interior-modern .fmt-chapter-opener, .book-pdf-body.interior-contemporary .fmt-chapter-opener, .book-pdf-body.interior-pod .fmt-chapter-opener { text-align: left; } ");
        sb.Append(".book-pdf-body.interior-minimalist .fmt-chapter-opener .fmt-ch-eyebrow { letter-spacing: 0.3em; opacity: 0.5; color: #71717a; } ");
        sb.Append(".book-pdf-body.interior-minimalist .fmt-chapter-opener .fmt-ch-title { font-weight: 800; letter-spacing: -0.01em; color: #1d1d1f; } ");
        sb.Append(".book-pdf-body.interior-modern .fmt-chapter-opener .fmt-ch-title { text-transform: uppercase; letter-spacing: 0.12em; font-weight: 800; color: #334155; } ");
        sb.Append(".book-pdf-body.interior-clean .fmt-chapter-opener .fmt-ch-title { font-weight: 600; color: #374151; } ");
        sb.Append(".book-pdf-body.interior-pod .fmt-chapter-opener .fmt-ch-rule { display: block; height: 1px; background: #ccc; width: 1.5in; max-width: 40%; margin: 0.12in 0 0; } ");

        // Elegant Trade + POD variant
        sb.Append(".book-pdf-body.interior-elegant-trade .fmt-chapter-opener .fmt-ch-eyebrow, .book-pdf-body.interior-elegant-trade-pod .fmt-chapter-opener .fmt-ch-eyebrow { color: ").Append(gold).Append("; letter-spacing: 0.2em; } ");
        sb.Append(".book-pdf-body.interior-elegant-trade .fmt-chapter-opener .fmt-ch-title, .book-pdf-body.interior-elegant-trade-pod .fmt-chapter-opener .fmt-ch-title { font-variant: small-caps; letter-spacing: 0.06em; color: #2c241a; } ");
        sb.Append(".book-pdf-body.interior-elegant-trade .fmt-chapter-opener .fmt-ch-rule, .book-pdf-body.interior-elegant-trade-pod .fmt-chapter-opener .fmt-ch-rule, .book-pdf-body.interior-fine-book .fmt-chapter-opener .fmt-ch-rule { display: block; height: 2px; background: ").Append(gold).Append("; width: 1.6in; max-width: 45%; margin: 0.14in auto 0; } ");
        sb.Append(".book-pdf-body.interior-elegant-trade .reader-page-body > p:first-of-type::first-letter, .book-pdf-body.interior-elegant-trade-pod .reader-page-body > p:first-of-type::first-letter, .book-pdf-body.interior-fine-book .reader-page-body > p:first-of-type::first-letter { color: ").Append(gold).Append(" !important; } ");

        // Contemporary (blue accents)
        sb.Append(".book-pdf-body.interior-contemporary .fmt-chapter-opener .fmt-ch-eyebrow { color: ").Append(blue).Append("; letter-spacing: 0.25em; text-transform: uppercase; } ");
        sb.Append(".book-pdf-body.interior-contemporary .fmt-chapter-opener .fmt-ch-title { text-transform: uppercase; letter-spacing: 0.08em; color: #1f2937; } ");
        sb.Append(".book-pdf-body.interior-contemporary .fmt-chapter-opener .fmt-ch-rule { display: block; height: 2px; background: ").Append(blue).Append("; width: 2in; max-width: 55%; margin: 0.14in 0 0; } ");

        // Chapter sink — opener starts partway down the page (matches formatter dropPct).
        var sink = interior switch
        {
            "ElegantTrade" or "FineBook" or "ElegantTradePOD" => "2.4rem",
            "Traditional" or "Classic" => "2rem",
            "Minimalist" => "3.2rem",
            "Contemporary" => "1.8rem",
            "Modern" => "1.5rem",
            "Clean" => "1.2rem",
            _ => "1.7rem"
        };
        sb.Append(".book-pdf-body.").Append(wrap).Append(" .reader-chapter-block[data-chapter-start=\"1\"] .fmt-chapter-opener { padding-top: ").Append(sink).Append("; } ");

        return sb.ToString();
    }

    /// <summary>
    /// Reconciles the printed-sheet padding with the per-page Chromium chrome:
    /// (1) for the interior export (no cover page) Chromium draws a running head + folio on EVERY page
    /// inside reserved top/bottom page margins (18mm/16mm, see <see cref="BookPreviewPrintHtmlBuilder"/>
    /// <c>@page</c> + <c>ChromiumPdfExporter</c>). We therefore hide the duplicate in-content running
    /// head and trim the sheet's top/bottom padding by the reserved amount so the text block keeps its
    /// intended trim-relative position;
    /// (2) POD styles add a 0.125in bleed per side, so the sheet padding gains 0.125in to keep text
    /// inside the trim safe zone.
    /// </summary>
    private static string BuildRunningChromeCompensationCss(BookPdfExportOptions opt, string interior)
    {
        var isPod = interior is "POD" or "ElegantTradePOD";
        var bleedIn = EBookDashboard.Configuration.KdpSpecsAccessor.Current.BleedIn
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        var bleed = isPod ? bleedIn + "in" : "0px";

        if (!opt.IncludeCoverPage)
        {
            // Running header/folio active: hide the duplicate in-content head and reserve top/bottom
            // margin (plus the POD bleed where applicable). Horizontal bleed padding is added for POD.
            var sides = isPod
                ? $"padding-left: calc(var(--ilt-pad-left) + {bleed}); padding-right: calc(var(--ilt-pad-right) + {bleed}); "
                : string.Empty;
            return string.Concat(
                ".book-pdf-body .book-preview-sheet > .page-header { display: none !important; } ",
                ".book-pdf-body .book-preview-sheet { ",
                // Reserve matches the @page header(18mm)/footer(16mm) margins, minus ~2mm so the text
                // block keeps its intended trim-relative position.
                "padding-top: max(0px, calc(var(--ilt-pad-top) + ", bleed, " - 16mm)); ",
                "padding-bottom: max(0px, calc(var(--ilt-pad-bottom) + ", bleed, " - 14mm)); ",
                sides, "} ");
        }

        // Cover export (no running chrome). Only POD needs all-sides bleed padding.
        return isPod
            ? $".book-pdf-body .book-preview-sheet {{ padding: calc(var(--ilt-pad-top) + {bleed}) calc(var(--ilt-pad-right) + {bleed}) calc(var(--ilt-pad-bottom) + {bleed}) calc(var(--ilt-pad-left) + {bleed}); }} "
            : string.Empty;
    }

    /// <summary>
    /// PDF-only parity rules so the exported page matches the per-style preview:
    /// chapter "sink" (heading starts partway down the first page), a guaranteed gap so body
    /// text never touches the heading, and drop caps for the elegant/traditional family.
    /// </summary>
    private static string BuildPdfInteriorParityCss(string interior)
    {
        var wrap = InteriorPrintDocumentBuilder.PreviewInteriorWrapClass(interior); // interior-*
        var sink = interior switch
        {
            "ElegantTrade" or "FineBook" or "ElegantTradePOD" => 30,
            "Traditional" or "Classic" => 25,
            "Minimalist" => 24,
            "Contemporary" => 22,
            "Novel" or "POD" => 20,
            "Modern" => 18,
            "Clean" => 15,
            _ => 20
        };

        var css = string.Concat(
            // When structured opener is present, sink is on .fmt-chapter-opener (see BuildFormatterChapterOpenerPdfCss).
            ".book-pdf-body.", wrap, " .reader-page-title:not(:has(.fmt-chapter-opener)) { padding-top: ", sink.ToString(System.Globalization.CultureInfo.InvariantCulture), "vh; } ",
            // Body text must never touch the chapter heading.
            ".book-pdf-body .reader-page-title + .reader-page-body { margin-top: 12mm; } ");

        if (interior is "ElegantTrade" or "ElegantTradePOD" or "Traditional" or "FineBook")
        {
            css = string.Concat(css,
                ".book-pdf-body.", wrap, " .reader-page-body > p:first-of-type::first-letter { ",
                "float: left; font-family: var(--heading-font); font-weight: 700; font-size: 4.2em; ",
                "line-height: 0.72; padding-right: 6px; margin-top: 4px; color: var(--heading-color); } ");
        }

        // Scene-break ornament — replace the plain rule with a centered glyph per style
        // (gold ❧/✦ for Fine Book + Elegant; ⁂ for Classic/Traditional; ◆ elsewhere).
        var (glyph, ornColor) = interior switch
        {
            "FineBook" => ("\\2767", "#C9A84C"),                       // ❧
            "ElegantTrade" or "ElegantTradePOD" => ("\\2726", "#C9A84C"), // ✦
            "Classic" or "Traditional" => ("\\2042", "var(--heading-color)"), // ⁂
            "Modern" or "Contemporary" or "Clean" or "Minimalist" => ("\\25C6", "var(--fmt-accent, var(--heading-color))"), // ◆
            _ => ("\\2726", "var(--heading-color)")                    // ✦
        };
        css = string.Concat(css,
            ".book-pdf-body.", wrap, " .manuscript-hr { border: 0 !important; height: auto; text-align: center; margin: 7mm 0; line-height: 1; } ",
            ".book-pdf-body.", wrap, " .manuscript-hr::after { content: \"", glyph, " \"; color: ", ornColor, "; font-size: 13pt; letter-spacing: 0.35em; } ");

        return css;
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
            scope, ".interior-novel .reader-page-title { font-family: var(--heading-font, 'Playfair Display', Georgia, serif); color: var(--heading-color, #6f2f10); font-weight: 700; text-align: center; border-bottom: 1px solid rgba(111, 47, 16, 0.18); } ",
            scope, ".interior-novel .reader-page-body { font-family: var(--body-font, 'Merriweather', Georgia, serif); color: var(--body-color, #2c2118); } ",
            scope, ".interior-novel .reader-page-body p { text-indent: var(--ilt-text-indent); margin-bottom: var(--ilt-para-space); } ",
            scope, ".interior-modern .reader-page-title { font-family: var(--heading-font, 'Inter', system-ui, sans-serif); text-transform: uppercase; letter-spacing: 0.18em; font-weight: 800; color: var(--heading-color, #334155); text-align: left; border-bottom: none; } ",
            scope, ".interior-modern .reader-page-body { font-family: var(--body-font, 'Inter', system-ui, sans-serif); color: var(--body-color, #334155); } ",
            scope, ".interior-modern .reader-page-body p { text-indent: 0; border-left: 3px solid #6366f1; padding-left: 0.9em; margin-bottom: 0.85em; } ",
            scope, ".interior-classic .reader-page-title { font-family: var(--heading-font, 'Cormorant Garamond', 'Times New Roman', Times, serif); font-style: italic; font-variant: small-caps; letter-spacing: 0.12em; text-align: center; border-bottom: 3px double #d6c4a8; color: var(--heading-color, #000000); } ",
            scope, ".interior-classic .reader-page-body { font-family: var(--body-font, 'Cormorant Garamond', 'Times New Roman', Times, serif); text-align: justify; color: var(--body-color, #231f1a); } ",
            scope, ".interior-classic .reader-page-body.classic-body .manuscript-p:first-of-type::first-letter, ",
            scope, ".interior-classic .reader-page-body p.classic-first-para::first-letter { float: left; font-size: 3.4em; line-height: 0.8; padding-right: 0.1em; font-weight: 600; color: var(--heading-color, #111); font-family: var(--heading-font, 'Cormorant Garamond', 'Times New Roman', Times, serif); } ",
            scope, ".interior-minimalist .reader-page-title { font-family: var(--heading-font, 'Inter', system-ui, sans-serif); font-weight: 600; color: var(--heading-color, #111827); text-align: left; border-bottom: 1px solid #ececec; } ",
            scope, ".interior-minimalist .reader-page-body { font-family: var(--body-font, 'Inter', system-ui, sans-serif); color: var(--body-color, #3f3f46); } ",
            scope, ".interior-minimalist .reader-page-body p { text-indent: 0; margin-bottom: 1.1em; } ",
            scope, ".interior-elegant-trade .reader-page-title { font-family: var(--heading-font, 'Lora', 'Times New Roman', serif); font-weight: 600; letter-spacing: 0.06em; color: var(--heading-color, #3d2914); text-align: center; border-bottom: 1px solid #c8b08e; } ",
            scope, ".interior-elegant-trade .reader-page-body { font-family: var(--body-font, 'EB Garamond', Baskerville, 'Palatino Linotype', Palatino, Georgia, serif); text-align: justify; color: var(--body-color, #29211b); } ",
            scope, ".interior-elegant-trade .reader-page-body p { text-indent: var(--ilt-text-indent); margin-bottom: var(--ilt-para-space); } ",
            scope, ".interior-elegant-trade .reader-page-body .manuscript-heading { font-family: var(--heading-font, 'Lora', serif); color: var(--heading-color, #4a3728); text-indent: 0; } ",
            // In-body sub-headings must never inherit justified body text (justify stretches short
            // heading lines into ugly word gaps, e.g. "The   Bullet   That   Couldn't"). Force a
            // natural left edge for all in-chapter headings across every interior style.
            scope, " .reader-page-body h1, ", scope, " .reader-page-body h2, ", scope, " .reader-page-body h3, ",
            scope, " .reader-page-body h4, ", scope, " .reader-page-body h5, ", scope, " .reader-page-body h6, ",
            scope, " .reader-page-body .manuscript-heading, ", scope, " .reader-page-body .manuscript-chapter-heading, ",
            scope, " .reader-page-body .manuscript-h1, ", scope, " .reader-page-body .manuscript-h2, ",
            scope, " .reader-page-body .manuscript-h3, ", scope, " .reader-page-body .manuscript-h4 ",
            "{ text-align: left; text-align-last: left; text-indent: 0; } ");

    private static string? NormalizeAccentHex(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        return System.Text.RegularExpressions.Regex.IsMatch(s, @"^#[0-9A-Fa-f]{6}$") ? s : null;
    }

    /// <summary>Resolved typography/color tokens for a normalized interior style (preview + PDF + EPUB).</summary>
    public static ExportThemeTokens ResolveExportThemeTokens(string? interiorStyle)
    {
        var interior = NormalizeInteriorStyle(interiorStyle);
        var theme = ResolveTheme(interior);
        return new ExportThemeTokens(
            theme.BodyFont,
            theme.HeadingFont,
            theme.HeadingColor,
            theme.BodyColor,
            ResolveDefaultPageBackground(interior));
    }

    private static ThemeSpec ResolveTheme(string interior) => interior switch
    {
        // STYLE 5 — Modern: DM Sans body / Outfit headings, clean grid, blue accent.
        "Modern" => new ThemeSpec(
            "'DM Sans', system-ui, 'Helvetica Neue', Arial, sans-serif",
            "'Outfit', system-ui, 'Helvetica Neue', Arial, sans-serif",
            "#111827", "#111827", "#ffffff",
            "font-weight: 700; margin: 1.2em 0 0.8em;",
            "text-indent: 0; margin-bottom: 12px;",
            "border-left: 3px solid #6366f1; margin: 3mm 0 3mm 4mm; padding-left: 4mm; color: #334155;",
            "border-bottom: none;",
            ""),
        // STYLE 4 — Contemporary: Lora body / Raleway uppercase headings, blue underline accent.
        "Contemporary" => new ThemeSpec(
            "'Lora', Georgia, 'Times New Roman', Times, serif",
            "'Raleway', system-ui, 'Helvetica Neue', Arial, sans-serif",
            "#1f2937", "#1f2937", "#ffffff",
            "font-weight: 700; text-transform: uppercase; letter-spacing: 3px; margin: 1.2em 0 0.6em;",
            "text-indent: 0; margin-bottom: 10px;",
            "border-left: 4px solid #3b82f6; margin: 3mm 0; padding-left: 4mm; font-style: italic; color: #1f2937;",
            "border-bottom: 2px solid #3b82f6;",
            ""),
        // STYLE 9 — Minimalist: Libre Baskerville body / Jost ultra-light uppercase headings.
        "Minimalist" => new ThemeSpec(
            "'Libre Baskerville', Georgia, serif",
            "'Jost', system-ui, 'Helvetica Neue', Arial, sans-serif",
            "#4b5563", "#4b5563", "#ffffff",
            "font-weight: 300; font-size: 1em; text-transform: uppercase; letter-spacing: 6px; margin: 80px 0 40px;",
            "text-indent: 0; margin-bottom: 16px;",
            "border-left: 1px solid #e5e5e5; margin: 4mm 0; padding-left: 4mm; color: #4b5563;",
            "border-bottom: none;",
            ""),
        // STYLE 8 — Clean: Source Serif body / Inter headings, minimal lines.
        "Clean" => new ThemeSpec(
            "'Source Serif 4', Georgia, 'Times New Roman', Times, serif",
            "'Inter', system-ui, 'Helvetica Neue', Arial, sans-serif",
            "#111827", "#374151", "#ffffff",
            "font-weight: 600; font-size: 1.1em; margin: 1.2em 0 0.7em;",
            "text-indent: 0; margin-bottom: 8px;",
            "border-left: 2px solid #e5e7eb; margin: 3mm 0; padding-left: 4mm; color: #374151;",
            "border-bottom: none;",
            ""),
        // STYLE 7 — Classic: Times New Roman, centered bold underlined headings, drop cap.
        "Classic" => new ThemeSpec(
            "'Times New Roman', Times, Georgia, serif",
            "'Times New Roman', Times, Georgia, serif",
            "#000000", "#000000", "#ffffff",
            "font-weight: 700; font-size: 1.25em; text-decoration: underline; margin: 1.4em 0 1em;",
            "text-indent: 1.25em; margin-bottom: 0;",
            "border-left: 3px solid #999999; margin: 3mm 0 3mm 6mm; padding-left: 4mm; color: #333333;",
            "border-bottom: none;",
            ".book-pdf-body.tpl-classic .manuscript-p:first-of-type::first-letter { float: left; font-size: 3.2em; line-height: 0.85; padding-right: 0.08em; font-weight: 700; color: #000000; } "),
        // STYLE 6 — Fine Book: Spectral body / Playfair Display italic headings, gold ornaments.
        "FineBook" => new ThemeSpec(
            "'Spectral', Georgia, 'Times New Roman', Times, serif",
            "'Playfair Display', Georgia, 'Times New Roman', Times, serif",
            "#1a120b", "#1a120b", "#fdf8f2",
            "font-weight: 400; font-size: 1.3em; font-style: italic; margin: 1.5em 0 1em;",
            "text-indent: 1.5em; margin-bottom: 0;",
            "border-left: 3px solid #c9a84c; margin: 3mm 0 3mm 6mm; padding-left: 4mm; font-style: italic; color: #4a3a1a;",
            "border-bottom: none;",
            ""),
        // STYLE 2 — Traditional: EB Garamond, centered bold headings, drop cap.
        "Traditional" => new ThemeSpec(
            "'EB Garamond', 'Palatino Linotype', Palatino, Georgia, serif",
            "'EB Garamond', 'Palatino Linotype', Palatino, Georgia, serif",
            "#222222", "#222222", "#ffffff",
            "font-weight: 700; font-size: 1.25em; margin: 1.4em 0 0.9em;",
            "text-indent: 1.25em; margin-bottom: 0;",
            "border-left: 3px solid #cccccc; margin: 3mm 0 3mm 6mm; padding-left: 4mm; color: #333333;",
            "border-bottom: none;",
            ".book-pdf-body.tpl-traditional .manuscript-p:first-of-type::first-letter { float: left; font-size: 3em; line-height: 0.85; padding-right: 0.08em; font-weight: 700; color: #222222; } "),
        // STYLE 10 — POD: Crimson Pro body / Nunito Sans headings, rule below heading.
        "POD" => new ThemeSpec(
            "'Crimson Pro', Georgia, 'Times New Roman', Times, serif",
            "'Nunito Sans', system-ui, 'Helvetica Neue', Arial, sans-serif",
            "#111111", "#111111", "#ffffff",
            "font-weight: 700; font-size: 1.1em; margin: 1.2em 0 0.7em; border-bottom: 1px solid #dddddd; padding-bottom: 0.3em;",
            "text-indent: 1em; margin-bottom: 0;",
            "border-left: 3px solid #cccccc; margin: 3mm 0 3mm 6mm; padding-left: 4mm; color: #333333;",
            "border-bottom: 1px solid #dddddd;",
            ""),
        // STYLE 11 — Elegant Trade (POD): same typography as Elegant Trade, POD-tuned.
        "ElegantTradePOD" => new ThemeSpec(
            "'Cormorant Garamond', 'EB Garamond', 'Palatino Linotype', Palatino, Georgia, serif",
            "'Cormorant SC', 'Cormorant Garamond', Georgia, serif",
            "#1c1c1c", "#1c1c1c", "#fdfbf7",
            "font-weight: 600; font-size: 1.35em; font-variant: small-caps; letter-spacing: 0.04em; text-align: center; margin: 1.3em 0 0.9em;",
            "text-indent: 1.5em; margin-bottom: 0;",
            "border-left: 3px solid #c9b8a0; margin: 3mm 0 3mm 6mm; padding-left: 4mm; font-style: italic; color: #3d2914;",
            "border-bottom: none;",
            ".book-pdf-body.tpl-elegant-trade-pod .manuscript-p:first-of-type::first-letter { float: left; font-size: 3.2em; line-height: 0.82; padding-right: 0.08em; font-weight: 600; color: #1c1c1c; } "),
        // STYLE 1 — Elegant Trade: Cormorant Garamond body / Cormorant SC small-caps headings, drop cap.
        "ElegantTrade" => new ThemeSpec(
            "'Cormorant Garamond', 'EB Garamond', 'Palatino Linotype', Palatino, Georgia, serif",
            "'Cormorant SC', 'Cormorant Garamond', Georgia, serif",
            "#1c1c1c", "#1c1c1c", "#fdfbf7",
            "font-weight: 600; font-size: 1.35em; font-variant: small-caps; letter-spacing: 0.04em; text-align: center; margin: 1.3em 0 0.9em;",
            "text-indent: 1.5em; margin-bottom: 0;",
            "border-left: 3px solid #c9b8a0; margin: 3mm 0 3mm 6mm; padding-left: 4mm; font-style: italic; color: #3d2914;",
            "border-bottom: none;",
            ".book-pdf-body.tpl-elegant-trade .manuscript-p:first-of-type::first-letter { float: left; font-size: 3.2em; line-height: 0.82; padding-right: 0.08em; font-weight: 600; color: #1c1c1c; } "),
        // STYLE 3 — Novel: Palatino / Book Antiqua, plain bold left headings, mass-market feel.
        _ => new ThemeSpec(
            "'Palatino Linotype', Palatino, 'Book Antiqua', Georgia, 'Times New Roman', Times, serif",
            "'Palatino Linotype', Palatino, 'Book Antiqua', Georgia, serif",
            "#1a1a1a", "#1a1a1a", "#ffffff",
            "font-weight: 700; font-size: 1.35em; margin: 1.2em 0 0.7em;",
            "text-indent: 1em; margin-bottom: 0;",
            "border-left: 3px solid #cccccc; margin: 3mm 0 3mm 6mm; padding-left: 4mm; color: #333333;",
            "border-bottom: none;",
            "")
    };

    /// <summary>Public theme token snapshot for <see cref="Models.DTO.BookTheme"/> and API responses.</summary>
    public sealed record ExportThemeTokens(
        string BodyFont,
        string HeadingFont,
        string HeadingColor,
        string BodyColor,
        string DefaultPageBackground);

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
