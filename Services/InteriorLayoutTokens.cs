using System.Globalization;

using System.Text.Json;

using EBookDashboard.Models.DTO;



namespace EBookDashboard.Services;



/// <summary>

/// Single source of truth for interior page layout numbers shared by Book Formatter preview and PDF export.

/// KDP 6×9 trim — margins, padding, frame, and text block use identical values in preview + Chromium PDF.

/// </summary>

public static class InteriorLayoutTokens

{

    /// <summary>KDP 6×9 Chromium margin box — running head / folio safe zone outside the text block.</summary>

    public sealed record PrintMarginSpec(string Top, string Bottom, string Inside, string Outside);



    /// <summary>Inner page padding — matches <c>.book-page-content-wrap</c> per interior.</summary>

    public sealed record ContentPaddingSpec(string Top, string Right, string Bottom, string Left);



    /// <summary>Body / paragraph typography defaults per interior (user line-spacing overrides via CSS var).</summary>

    public sealed record BodyTypographySpec(

        string DefaultBodyFontRem,

        string DefaultLineHeight,

        string TextIndent,

        string ParagraphSpacing,

        string TextAlign,

        string TitleMarginBottom,

        string TitlePaddingBottom);



    /// <summary>Studio mat + paper frame — preview card and PDF sheet chrome.</summary>

    public sealed record FrameSpec(

        string MatBackground,

        string MatBorder,

        string SheetBorder,

        string SheetShadow,

        string ContainerShadow,

        string PageBackground);



    /// <summary>Per-interior layout bundle used to emit identical preview + print CSS.</summary>

    public sealed record InteriorSpec(

        string Key,

        string SheetBackground,

        ContentPaddingSpec SheetPadding,

        BodyTypographySpec Body,

        FrameSpec Frame,

        string TitleFontSizePt,

        string TitleLetterSpacing,

        string TitleBorderBottom,

        bool TitleUppercase,

        bool TitleCentered);



    /// <summary>Full text measure between gutter and fore-edge.</summary>
    public static string TextBlockMaxWidth => "100%";



    /// <summary>KDP-style print margin box for 6×9 trim — inside is wider for the binding gutter.</summary>

    public static PrintMarginSpec KdpTrim6x9Default => InteriorSpacingTheme.KdpPrintMarginBox;

    /// <summary>PDF export margins — preview page inset on every printed page via Chromium.</summary>
    public static PrintMarginSpec PdfExportChromiumMargins => InteriorSpacingTheme.PdfExportChromiumMargins;

    public static string ChapterDrop => InteriorSpacingTheme.In(InteriorSpacingTheme.ChapterTitleSinkIn);

    public static string FrontMatterPadTop => InteriorSpacingTheme.MmToIn(InteriorSpacingTheme.FrontMatterPadTopMm);

    public static string TitlePagePadTop => InteriorSpacingTheme.MmToIn(InteriorSpacingTheme.TitlePagePadTopMm);

    public static string RunningHeadPadTop => InteriorSpacingTheme.MmToIn(InteriorSpacingTheme.RunningHeadHeightMm);

    public static string RunningHeadPadSides => InteriorSpacingTheme.MmToIn(InteriorSpacingTheme.HeaderFooterOutsideInsetMm);

    public static string RunningHeadPadInside => InteriorSpacingTheme.MmToIn(InteriorSpacingTheme.HeaderFooterInsideInsetMm);

    public static string FolioPadBottom => InteriorSpacingTheme.MmToIn(InteriorSpacingTheme.FolioHeightMm);

    /// <summary>Clear air between the running head and the first body line (preview + PDF).</summary>

    public static string RunningHeadGapBelow => InteriorSpacingTheme.MmToIn(InteriorSpacingTheme.RunningHeadGapBelowMm);

    /// <summary>Clear air between the last body line and the folio (page number).</summary>

    public static string FolioGapAbove => InteriorSpacingTheme.MmToIn(InteriorSpacingTheme.FolioGapAboveMm);



    private static readonly Dictionary<string, InteriorSpec> Specs = new(StringComparer.OrdinalIgnoreCase)

    {

        ["Novel"] = new(

            Key: "novel",

            SheetBackground: "#faf2e2",

            SheetPadding: InteriorSpacingTheme.TradePaperbackPagePadding,

            Body: new("0.97rem", "1.6", InteriorSpacingTheme.Mm(InteriorSpacingTheme.FirstLineIndentMm), "0", "justify", "1.1rem", "0.7rem"),

            Frame: new(

                "linear-gradient(180deg, #f7eee2 0%, #f3e4d3 100%)",

                "1px solid #d7bf9f",

                "1px solid #d4b08a",

                "0 2px 0 rgba(255, 255, 255, 0.85) inset, 0 14px 40px -12px rgba(91, 33, 182, 0.22)",

                "0 18px 44px -24px rgba(120, 72, 32, 0.36), 0 2px 0 rgba(255, 255, 255, 0.85) inset, 0 0 0 1px rgba(212, 176, 138, 0.45)",

                "linear-gradient(180deg, #fdf6e9 0%, #f7eddb 100%)"),

            TitleFontSizePt: "18",

            TitleLetterSpacing: "0.015em",

            TitleBorderBottom: "1px solid rgba(111, 47, 16, 0.18)",

            TitleUppercase: false,

            TitleCentered: true),

        ["Modern"] = new(

            Key: "modern",

            SheetBackground: "#ffffff",

            SheetPadding: new("0.72in", "0.68in", "0.66in", "0.8in"),

            Body: new("0.97rem", "1.74", "0", "0.9rem", "left", "1rem", "0"),

            Frame: new(

                "linear-gradient(180deg, #eceff5 0%, #e2e8f0 100%)",

                "1px solid #c6d2e2",

                "1px solid #9fb1c9",

                "0 12px 40px -12px rgba(51, 65, 85, 0.28)",

                "0 20px 50px -30px rgba(51, 65, 85, 0.42), 0 2px 0 rgba(255, 255, 255, 0.9) inset, 0 0 0 1px rgba(159, 177, 201, 0.55)",

                "linear-gradient(180deg, #ffffff 0%, #f8fafc 100%)"),

            TitleFontSizePt: "11.5",

            TitleLetterSpacing: "0.18em",

            TitleBorderBottom: "none",

            TitleUppercase: true,

            TitleCentered: false),

        ["Classic"] = new(

            Key: "classic",

            SheetBackground: "#f4ecd8",

            SheetPadding: new("0.8in", "0.72in", "0.7in", "0.84in"),

            Body: new("1.125rem", "1.95", "1.25rem", "0.28em", "justify", "1.1rem", "0.55rem"),

            Frame: new(

                "linear-gradient(180deg, #e8e2d8 0%, #d8d1c6 100%)",

                "1px solid #bcae99",

                "1px solid #ddd2c4",

                "0 12px 36px -14px rgba(0, 0, 0, 0.18)",

                "0 8px 30px rgba(0, 0, 0, 0.11), 0 0 0 1px rgba(255, 255, 255, 0.9) inset, 0 0 0 1px rgba(214, 196, 168, 0.5)",

                "#f3ead2 url(\"data:image/svg+xml,%3Csvg width='40' height='40' xmlns='http://www.w3.org/2000/svg'%3E%3Cpath d='M0 40L40 0' stroke='%23e3d9c2' stroke-width='0.5' fill='none'/%3E%3C/svg%3E\")"),

            TitleFontSizePt: "18",

            TitleLetterSpacing: "0.03em",

            TitleBorderBottom: "1px solid #d4cfc4",

            TitleUppercase: false,

            TitleCentered: true),

        ["Minimalist"] = new(

            Key: "minimalist",

            SheetBackground: "#ffffff",

            SheetPadding: InteriorSpacingTheme.MinimalistPagePadding,

            Body: new("0.97rem", "1.55", "0", InteriorSpacingTheme.Mm(InteriorSpacingTheme.MinimalistParagraphSpacingMm), "left", "1.15rem", "0.6rem"),

            Frame: new(

                "linear-gradient(180deg, #fafafa 0%, #f4f4f5 100%)",

                "1px solid #e4e4e7",

                "1px solid #e4e4e7",

                "0 8px 28px -16px rgba(15, 23, 42, 0.12)",

                "0 0 0 1px #e4e4e7",

                "#ffffff"),

            TitleFontSizePt: "12",

            TitleLetterSpacing: "-0.01em",

            TitleBorderBottom: "1px solid #ececec",

            TitleUppercase: false,

            TitleCentered: false),

        ["ElegantTrade"] = new(

            Key: "elegant-trade",

            SheetBackground: "#f7efdd",

            SheetPadding: InteriorSpacingTheme.TradePaperbackPagePadding,

            Body: new("0.97rem", "1.6", InteriorSpacingTheme.Mm(InteriorSpacingTheme.FirstLineIndentMm), "0", "justify", "0.5rem", "1rem"),

            Frame: new(

                "linear-gradient(180deg, #ece4d8 0%, #e0d4c4 100%)",

                "1px solid #c9b79f",

                "1px solid #cfbea5",

                "0 12px 40px -14px rgba(62, 47, 32, 0.22)",

                "0 14px 42px -20px rgba(62, 47, 32, 0.34), 0 0 0 1px rgba(201, 184, 160, 0.35)",

                "linear-gradient(165deg, #fbf5e6 0%, #f4ebd6 55%, #fbf5e6 100%)"),

            TitleFontSizePt: "17",

            TitleLetterSpacing: "0.06em",

            TitleBorderBottom: "1px solid #c8b08e",

            TitleUppercase: false,

            TitleCentered: true)

    };



    /// <summary>Returns the layout spec for a normalized interior style key.</summary>

    public static InteriorSpec Get(string? interiorStyle)

    {

        var key = InteriorExportTheme.NormalizeInteriorStyle(interiorStyle);

        return Specs.TryGetValue(key, out var spec) ? spec : Specs["Novel"];

    }



    /// <summary>Exact line-height multiplier — same numeric string preview passes to <c>--fmt-line-height</c>.</summary>

    public static string ResolveLineHeightExact(string? lineSpacing)

    {

        if (double.TryParse((lineSpacing ?? "").Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var lh))

        {

            if (lh <= 1.45) return InteriorSpacingTheme.LineHeightTight.ToString("0.#", CultureInfo.InvariantCulture);

            if (lh <= 1.7) return InteriorSpacingTheme.LineHeightNormal.ToString("0.#", CultureInfo.InvariantCulture);

            if (lh <= 1.9) return InteriorSpacingTheme.LineHeightRelaxed.ToString("0.#", CultureInfo.InvariantCulture);

            return InteriorSpacingTheme.LineHeightLoose.ToString("0.#", CultureInfo.InvariantCulture);

        }



        return InteriorSpacingTheme.LineHeightNormal.ToString("0.#", CultureInfo.InvariantCulture);

    }



    /// <summary>Body font size for preview/PDF — px string matching <c>applyPreviewStyles</c>.</summary>

    public static string ResolveBodyFontSizePx(BookPdfExportOptions opt) =>

        InteriorExportTheme.ResolveBodyFontSizePx(opt.InteriorStyle, opt.TextSize);



    /// <summary>Body font size in pt for print (px × 0.75).</summary>

    public static string ResolveBodyFontSizePt(BookPdfExportOptions opt)

    {

        if (!double.TryParse(ResolveBodyFontSizePx(opt), NumberStyles.Integer, CultureInfo.InvariantCulture, out var px))

            px = 16;

        return (px * 0.75).ToString("0.##", CultureInfo.InvariantCulture);

    }



    /// <summary>CSS custom properties block — inject in formatter preview and PDF HTML.</summary>

    public static string BuildCssCustomProperties(BookPdfExportOptions opt)

    {

        var interior = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);

        var spec = Get(interior);

        var typography = InteriorTypographyPresets.Resolve(opt);
        var lh = typography.LineHeight;
        var bodyPx = typography.BodyFontSizePx;
        var bodyPt = typography.BodyFontSizePt;

        var pageBg = opt.ResolvePageBackgroundColor();

        var m = KdpTrim6x9Default;

        var f = spec.Frame;



        return string.Concat(

            ":root { ",

            "--ilt-page-bg: ", pageBg, "; ",

            "--ilt-body-px: ", bodyPx, "px; ",

            "--ilt-body-pt: ", bodyPt, "pt; ",

            "--ilt-body-lh: ", lh, "; ",

            "--ilt-sheet-bg: ", spec.SheetBackground, "; ",

            "--ilt-pad-top: ", spec.SheetPadding.Top, "; ",

            "--ilt-pad-right: ", spec.SheetPadding.Right, "; ",

            "--ilt-pad-bottom: ", spec.SheetPadding.Bottom, "; ",

            "--ilt-pad-left: ", spec.SheetPadding.Left, "; ",

            "--ilt-text-indent: ", typography.TextIndent, "; ",

            "--ilt-para-space: ", typography.ParagraphSpacing, "; ",

            "--ilt-text-align: ", typography.TextAlign, "; ",

            "--ilt-title-mb: ", spec.Body.TitleMarginBottom, "; ",

            "--ilt-title-pb: ", spec.Body.TitlePaddingBottom, "; ",

            "--ilt-text-max: ", TextBlockMaxWidth, "; ",

            "--ilt-chapter-drop: ", ChapterDrop, "; ",

            "--ilt-front-pad-top: ", FrontMatterPadTop, "; ",

            "--ilt-title-page-pad-top: ", TitlePagePadTop, "; ",

            "--ilt-margin-top: ", m.Top, "; ",

            "--ilt-margin-bottom: ", m.Bottom, "; ",

            "--ilt-margin-inside: ", m.Inside, "; ",

            "--ilt-margin-outside: ", m.Outside, "; ",

            "--ilt-mat-bg: ", f.MatBackground, "; ",

            "--ilt-mat-border: ", f.MatBorder, "; ",

            "--ilt-sheet-border: ", f.SheetBorder, "; ",

            "--ilt-sheet-shadow: ", f.SheetShadow, "; ",

            "--ilt-container-shadow: ", f.ContainerShadow, "; ",

            "--ilt-page-surface: ", f.PageBackground, "; ",

            "--ilt-running-head-h: ", RunningHeadPadTop, "; ",

            "--ilt-folio-h: ", FolioPadBottom, "; ",

            "--ilt-running-head-gap-below: ", RunningHeadGapBelow, "; ",

            "} ");

    }



    /// <summary>Shared reader layout rules — used by PDF export and formatter token sync.</summary>

    public static string BuildSharedReaderLayoutCss()

    {

        var sb = new System.Text.StringBuilder(4096);

        sb.Append(".book-preview-sheet { width:100%; box-sizing:border-box; background:var(--ilt-sheet-bg); ");

        sb.Append("padding:var(--ilt-pad-top) var(--ilt-pad-right) var(--ilt-pad-bottom) var(--ilt-pad-left); ");

        sb.Append("-webkit-print-color-adjust:exact; print-color-adjust:exact; border:var(--ilt-sheet-border, none); ");

        sb.Append("box-shadow:var(--ilt-sheet-shadow, none); } ");

        sb.Append(".book-page-content-wrap { box-sizing:border-box; background:var(--ilt-page-bg, var(--ilt-sheet-bg)); } ");

        sb.Append(".book-page-content { background:transparent; overflow:hidden; } ");

        sb.Append(".book-page-background { background:var(--ilt-page-surface, var(--ilt-page-bg)); ");

        sb.Append("-webkit-print-color-adjust:exact; print-color-adjust:exact; } ");

        sb.Append(".reader-page-body { max-width:var(--ilt-text-max); margin-inline:auto; width:100%; } ");

        sb.Append(".reader-page-body,.reader-page-body p,.reader-page-body .manuscript-p { ");

        sb.Append("font-size:var(--ilt-body-pt); line-height:var(--ilt-body-lh); text-align:var(--ilt-text-align); } ");

        sb.Append(".reader-page-body p,.reader-page-body .manuscript-p { ");

        sb.Append("text-indent:var(--ilt-text-indent); margin:0 0 var(--ilt-para-space); orphans:3; widows:3; ");

        sb.Append("hyphens:auto; -webkit-hyphens:auto; text-align:var(--ilt-text-align, justify); ");

        sb.Append("hyphenate-limit-chars:6 3 3; } ");

        sb.Append(".reader-page-body p:empty,.reader-page-body .manuscript-p:empty { display:none; margin:0; padding:0; height:0; } ");

        sb.Append(".reader-page-body p:first-of-type,.reader-page-title + .reader-page-body p:first-of-type { text-indent:0; } ");

        sb.Append(".reader-chapter-block { margin:0; padding:0; } ");

        sb.Append(".reader-page-title { margin:0 0 var(--ilt-title-mb); padding-bottom:var(--ilt-title-pb); max-width:var(--ilt-text-max); ");

        sb.Append("margin-inline:auto; width:100%; box-sizing:border-box; } ");

        sb.Append(".manuscript-root > section.chapter { padding-top:0; padding-bottom:0; min-height:auto; } ");

        sb.Append(".reader-chapter-block[data-chapter-start=\"1\"] .reader-page-title, ");

        sb.Append(".book-pdf-body section.chapter .reader-page-title { margin-top:var(--ilt-chapter-drop); } ");

        sb.Append(".front-matter-page { padding-top:var(--ilt-front-pad-top); } ");

        sb.Append("@media print { ");

        sb.Append(".book-pdf-body .reader-page-body:not(.page-header + .page-body .reader-page-body) { ");

        sb.Append("-webkit-box-decoration-break:clone; box-decoration-break:clone; ");

        sb.Append("padding-top:var(--ilt-running-head-gap-below, ").Append(RunningHeadGapBelow).Append("); } ");

        sb.Append(".book-pdf-body .page-header + .page-body .reader-page-body, ");

        sb.Append(".book-pdf-body .reader-page-title + .reader-page-body, ");

        sb.Append(".book-pdf-body .title-page, .book-pdf-body .copyright-page, .book-pdf-body .toc-page { padding-top:0; } ");

        sb.Append("} ");

        sb.Append("img { max-width:100%; height:auto; page-break-inside:avoid; break-inside:avoid; display:block; margin:0.75rem auto; } ");

        return sb.ToString();

    }



    /// <summary>
    /// PDF export page chrome — running head and body are in-page only (no Chromium header/footer).
    /// <c>.page-header</c> sits once at each chapter start; gap below uses <see cref="InteriorSpacingTheme.PageHeaderBodyGapCss"/>.
    /// </summary>
    public static string BuildPdfInContentPageChromeCss()
    {
        return string.Concat(
            ".book-pdf-body .book-preview-sheet > .page-header { ",
            "flex-shrink:0; text-align:center; font-family:Georgia,'Times New Roman',serif; ",
            "font-size:7.5pt; letter-spacing:0.22em; text-transform:uppercase; color:#7c7368; line-height:1.35; ",
            "min-height:var(--ilt-running-head-h, ", RunningHeadPadTop, "); padding:0.08in 0 0; margin:0 0 ",
            InteriorSpacingTheme.PageHeaderBodyGapCss, " 0; ",
            "overflow:hidden; text-overflow:ellipsis; white-space:nowrap; box-sizing:border-box; ",
            "break-after:avoid; page-break-after:avoid; } ",
            ".book-pdf-body .book-preview-sheet > .page-body { flex:1 1 auto; min-height:0; } ",
            ".book-pdf-body .page-header + .page-body .reader-page-title { margin-top:var(--ilt-chapter-drop); } ",
            ".book-pdf-body .page-header + .page-body .reader-page-body { padding-top:0; } ",
            ".book-pdf-body section.chapter { break-before:page; page-break-before:always; } ",
            ".book-pdf-body .manuscript-root > section.chapter:first-of-type { break-before:auto; page-break-before:auto; } ");
    }



    /// <summary>Running head + folio chrome — formatter paginated preview matches PDF margin box.</summary>

    public static string BuildPreviewPageChromeCss()

    {

        return string.Concat(

            "#book-formatter-root #paginatedReaderShell { display:flex; flex-direction:column; padding:0 !important; } ",

            "#book-formatter-root #bookStage { display:flex; flex-direction:column; width:100%; height:100%; min-height:0; flex:1 1 auto; } ",

            "#book-formatter-root #bookContainer { display:flex; flex-direction:column; width:100%; height:100%; min-height:0; flex:1 1 auto; } ",

            "#book-formatter-root .book-page-layer { display:flex; flex-direction:column; height:100%; min-height:0; } ",

            "#book-formatter-root .book-page-content-wrap { display:flex; flex-direction:column; height:100%; min-height:0; flex:1 1 auto; ",

            "padding:var(--ilt-pad-top) var(--ilt-pad-right) var(--ilt-pad-bottom) var(--ilt-pad-left); box-sizing:border-box; } ",

            "#book-formatter-root .book-page-running-head { flex-shrink:0; text-align:center; font-family:Georgia,'Times New Roman',serif; ",

            "font-size:7.5pt; letter-spacing:0.22em; text-transform:uppercase; color:#7c7368; line-height:1.35; ",

            "min-height:var(--ilt-running-head-h,0.4in); padding:0.08in 0 ", RunningHeadGapBelow, "; margin:0; overflow:hidden; ",

            "text-overflow:ellipsis; white-space:nowrap; box-sizing:border-box; } ",

            "#book-formatter-root .book-page-running-head.is-empty { min-height:0; padding:0; visibility:hidden; } ",

            "#book-formatter-root .book-page-content { flex:1 1 auto; min-height:0; overflow:hidden; display:flex; flex-direction:column; } ",

            "#book-formatter-root .book-page-footer { flex-shrink:0; margin-top:auto; padding:", FolioGapAbove, " 0 var(--ilt-folio-h,0.38in); ",

            "text-align:center; font-family:Georgia,'Times New Roman',serif; font-size:7.5pt; letter-spacing:0.12em; color:#7c7368; } ",

            "#book-formatter-root #paginatedReaderShell .reader-page-title, ",

            "#book-formatter-root #paginatedReaderShell .reader-page-body, ",

            "#book-formatter-root #paginatedReaderShell .reader-page-body p { ",

            "font-size:var(--fmt-font-size,var(--ilt-body-px)) !important; ",

            "line-height:var(--fmt-line-height,var(--ilt-body-lh)) !important; ",

            "text-align:var(--ilt-text-align,justify) !important; } ",

            "#book-formatter-root #paginatedReaderShell .reader-page-body p { ",

            "text-indent:var(--ilt-text-indent,0) !important; margin:0 0 var(--ilt-para-space,1em) !important; ",

            "border-left:none !important; padding-left:0 !important; } ",

            "#book-formatter-root #paginatedReaderShell.interior-modern .reader-page-body p { ",

            "text-indent:0 !important; border-left:3px solid var(--fmt-accent,#6366f1) !important; padding-left:0.9rem !important; } ",

            "#book-formatter-root #paginatedReaderShell.interior-minimalist .reader-page-body p { text-indent:0 !important; } ",

            "#book-formatter-root #fmt-preview-measure-host { padding:0 !important; font-size:inherit; line-height:inherit; } ");

    }



    /// <summary>Front matter + table of contents — identical in formatter preview and PDF export.</summary>

    public static string BuildTocCss()

    {

        return string.Concat(

            ".front-matter-page { box-sizing: border-box; page-break-after: always; break-after: page; } ",

            ".copyright-page, .toc-page { display: block; text-align: left; min-height: auto; justify-content: flex-start; } ",

            ".copyright-block, .toc-block { max-width: var(--ilt-text-max); margin-inline: auto; width: 100%; box-sizing: border-box; } ",

            ".copyright-page .cr-meta { font-size: 11pt; margin: 0 0 0.35in; line-height: 1.45; } ",

            ".copyright-page .cr-legal { font-size: 9.5pt; margin: 0.28in 0 0.18in; line-height: 1.55; color: #3f3a34; } ",

            ".copyright-page .cr-small { font-size: 8.5pt; color: #64748b; margin-top: 0.22in; } ",

            ".toc-title { font-family: var(--heading-font, Georgia, serif); font-size: 17pt; font-weight: 600; ",

            "letter-spacing: 0.06em; text-transform: uppercase; text-align: center; color: var(--heading-color, #1c1917); ",

            "margin: 0 0 0.55in; padding-bottom: 0.22in; border-bottom: 1px solid rgba(120, 96, 72, 0.28); } ",

            ".toc-nav { margin: 0; } ",

            ".toc-list { list-style: none; margin: 0; padding: 0; } ",

            ".toc-item { margin: 0 0 0.38in; font-size: 10.5pt; line-height: 1.4; } ",

            ".toc-item-empty { color: #64748b; font-style: italic; } ",

            ".toc-chapter-line { display: flex; align-items: baseline; gap: 0.12in; font-weight: 600; color: #1c1917; } ",

            ".toc-entry-text { flex: 0 1 auto; min-width: 0; } ",

            ".toc-leader { flex: 1 1 auto; min-width: 0.35in; border-bottom: 1px dotted rgba(100, 116, 139, 0.65); ",

            "transform: translateY(-0.14em); margin: 0 0.08in; } ",

            ".toc-page-ref { flex: 0 0 auto; min-width: 0.35in; text-align: right; font-weight: 600; font-variant-numeric: tabular-nums; color: #334155; } ",

            ".toc-subheadings { list-style: none; margin: 0.14in 0 0; padding: 0 0 0 0.28in; } ",

            ".toc-subheading-item { font-size: 9.5pt; font-weight: 400; color: #475569; margin: 0 0 0.12in; line-height: 1.38; } ",

            ".toc-sub-text { display: block; padding-left: 0.12in; border-left: 1px solid rgba(148, 163, 184, 0.45); } ",

            ".toc-link { color: inherit; text-decoration: none; } ",

            ".toc-link:hover { text-decoration: underline; } ",

            "[data-interior-mode=\"web\"] .toc-page { padding-top: var(--ilt-front-pad-top); min-height: auto; display: block; } ",

            "[data-interior-mode=\"web\"] .copyright-page { padding-top: var(--ilt-front-pad-top); } ",

            "[data-interior-mode=\"web\"] .title-page { padding: var(--ilt-title-page-pad-top) var(--ilt-pad-right) var(--ilt-pad-bottom) var(--ilt-pad-left); ",

            "min-height: var(--preview-page-min-h, 9in); display: flex; flex-direction: column; justify-content: center; text-align: center; } ",

            "[data-interior-mode=\"web\"] .title-page h1 { font-size: 1.65rem; letter-spacing: 0.03em; margin: 0 0 0.75rem; max-width: var(--ilt-text-max); margin-inline: auto; } ",

            "[data-interior-mode=\"web\"] .title-page-author { font-size: 0.95rem; letter-spacing: 0.06em; margin: 1rem 0 0.35rem; } ",

            "[data-interior-mode=\"web\"] .title-page-genre, [data-interior-mode=\"web\"] .title-page .subtitle { ",

            "text-transform: uppercase; letter-spacing: 0.18em; font-size: 0.72rem; color: #8a8175; margin-top: 0.35rem; } ");

    }



    private static string MatClassForKey(string key) => key switch
    {
        "elegant-trade" => "fmt-mat-elegant-trade",
        _ => "fmt-mat-" + key
    };

    /// <summary>Frame, mat, and container chrome — identical in formatter preview and PDF body.</summary>

    public static string BuildFrameAndSheetCss()

    {

        var sb = new System.Text.StringBuilder(8192);

        foreach (var spec in Specs.Values)

        {

            var k = spec.Key;

            var f = spec.Frame;

            var wrap = "interior-" + k;

            var mat = MatClassForKey(k);



            // Formatter preview: no mat chrome — the 6×9 page card floats clean on the canvas.
            // (Background/border intentionally removed across every interior variant.)
            sb.Append("#book-formatter-root #fmt-book-result.").Append(mat).Append(" { ");
            sb.Append("background:transparent; border:0; ");
            sb.Append("border-radius:0; padding:0; box-sizing:border-box; } ");

            sb.Append("#book-formatter-root #paginatedReaderShell.").Append(wrap).Append(", ");
            sb.Append("#book-formatter-root .paginated-reader-shell.").Append(wrap).Append(" { ");
            sb.Append("background:var(--ilt-page-bg, ").Append(spec.SheetBackground).Append("); ");
            sb.Append("border:").Append(f.SheetBorder).Append("; ");
            sb.Append("box-shadow:").Append(f.SheetShadow).Append("; } ");

            sb.Append("#book-formatter-root #paginatedReaderShell.").Append(wrap).Append(" .book-page-preview-content, ");
            sb.Append("#book-formatter-root .paginated-reader-shell.").Append(wrap).Append(" .book-page-preview-content { ");
            sb.Append("background:var(--ilt-page-bg, ").Append(spec.SheetBackground).Append("); ");
            sb.Append("padding:var(--ilt-pad-top) var(--ilt-pad-right) var(--ilt-pad-bottom) var(--ilt-pad-left); ");
            sb.Append("box-sizing:border-box; height:100%; } ");
            sb.Append("#book-formatter-root #bookContainer.").Append(wrap).Append(", ");

            sb.Append("#book-formatter-root #paginatedReaderShell.").Append(wrap).Append(" #bookContainer, ");

            sb.Append(".book-pdf-body.").Append(wrap).Append(" .book-preview-sheet { ");

            sb.Append("background:var(--ilt-page-bg, ").Append(spec.SheetBackground).Append("); ");

            sb.Append("border:").Append(f.SheetBorder).Append("; ");

            sb.Append("box-shadow:").Append(f.ContainerShadow).Append("; } ");



            sb.Append("#book-formatter-root .").Append(wrap).Append(" .book-page-background, ");

            sb.Append("#book-formatter-root .book-page-background.").Append(wrap).Append(" { ");

            sb.Append("background:").Append(f.PageBackground).Append("; } ");



            sb.Append(".book-pdf-body.").Append(wrap).Append(" { background:var(--page-bg); } ");

            sb.Append(".book-pdf-body.").Append(wrap).Append(" .book-preview-sheet { ");

            sb.Append("background:var(--ilt-page-bg, ").Append(spec.SheetBackground).Append("); ");

            sb.Append("border:").Append(f.SheetBorder).Append("; } ");

        }



        sb.Append("#book-formatter-root #bookContainer { border-radius:4px; overflow:hidden; width:100%; height:100%; ");

        sb.Append("background:var(--ilt-page-bg, var(--ilt-sheet-bg)); border:var(--ilt-sheet-border); ");

        sb.Append("box-shadow:var(--ilt-container-shadow); transition:background 0.4s ease, border-color 0.4s ease, box-shadow 0.4s ease; } ");

        sb.Append("#book-formatter-root .book-page-content-wrap { overflow:hidden; } ");



        return sb.ToString();

    }



    /// <summary>Per-interior title + body font overrides for print/preview parity.</summary>

    public static string BuildPerInteriorCss(string interior)

    {

        var spec = Get(interior);

        var align = spec.TitleCentered ? "center" : "left";

        var uppercase = spec.TitleUppercase ? "text-transform:uppercase; " : "";

        // Per-interior font + ink — emitted with the SAME high-specificity selector that already
        // drives padding, so the typeface/colour visibly change on every style switch (preview + PDF).
        var (bodyFont, titleFont, bodyColor, titleColor) = spec.Key switch
        {
            "novel" => ("'Merriweather', Georgia, serif", "'Playfair Display', Georgia, serif", "#2c2118", "#6f2f10"),
            "modern" => ("'Inter', system-ui, sans-serif", "'Inter', system-ui, sans-serif", "#334155", "#334155"),
            "classic" => ("'Cormorant Garamond', 'Times New Roman', Times, serif", "'Cormorant Garamond', 'Times New Roman', Times, serif", "#231f1a", "#141414"),
            "minimalist" => ("'Inter', system-ui, sans-serif", "'Inter', system-ui, sans-serif", "#3f3f46", "#111827"),
            "elegant-trade" => ("'EB Garamond', Baskerville, 'Palatino Linotype', Palatino, Georgia, serif", "'Lora', 'Times New Roman', serif", "#29211b", "#3d2914"),
            _ => ("Georgia, serif", "Georgia, serif", "#2c2118", "#1c1917")
        };

        var sb = new System.Text.StringBuilder(4096);



        sb.Append(".interior-").Append(spec.Key).Append(" .reader-page-title,");

        sb.Append(".book-pdf-body.interior-").Append(spec.Key).Append(" .reader-page-title,");

        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content .reader-page-title,");

        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content .reader-page-title { ");

        sb.Append("font-size:").Append(spec.TitleFontSizePt).Append("pt; ");

        sb.Append("letter-spacing:").Append(spec.TitleLetterSpacing).Append("; ");

        sb.Append("text-align:").Append(align).Append("; ");

        sb.Append(uppercase);

        if (!string.IsNullOrEmpty(spec.TitleBorderBottom))

            sb.Append("border-bottom:").Append(spec.TitleBorderBottom).Append("; ");

        sb.Append("margin-bottom:").Append(spec.Body.TitleMarginBottom).Append("; ");

        sb.Append("padding-bottom:").Append(spec.Body.TitlePaddingBottom).Append("; } ");



        var pad = spec.SheetPadding;

        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content-wrap,");

        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content-wrap { padding:");

        sb.Append(pad.Top).Append(' ').Append(pad.Right).Append(' ').Append(pad.Bottom).Append(' ').Append(pad.Left);

        sb.Append(" !important; box-sizing:border-box; } ");

        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content .reader-page-body,");

        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content .reader-page-body,");

        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content .reader-page-body p,");

        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content .reader-page-body p { ");

        sb.Append("font-size:var(--fmt-font-size, var(--ilt-body-px)) !important; ");

        sb.Append("line-height:var(--fmt-line-height, var(--ilt-body-lh)) !important; ");

        sb.Append("text-align:var(--ilt-text-align) !important; } ");

        sb.Append(".book-pdf-body.interior-").Append(spec.Key).Append(" .reader-page-body,");

        sb.Append(".book-pdf-body.interior-").Append(spec.Key).Append(" .reader-page-body p { ");

        sb.Append("font-size:var(--ilt-body-pt) !important; ");

        sb.Append("line-height:var(--ilt-body-lh) !important; ");

        sb.Append("text-align:var(--ilt-text-align) !important; } ");

        // Body typeface + ink (broad descendant selector covers BOTH render targets:
        // the paginated .book-page-content and the single-page #preview-content). !important
        // guarantees it wins wherever the (working) padding rule wins.
        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key).Append(" .reader-page-body,");
        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key).Append(" .reader-page-body p,");
        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key).Append(" .reader-page-body .manuscript-p,");
        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key).Append(" .reader-page-body,");
        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key).Append(" .reader-page-body p,");
        sb.Append(".book-pdf-body.interior-").Append(spec.Key).Append(" .reader-page-body,");
        sb.Append(".book-pdf-body.interior-").Append(spec.Key).Append(" .reader-page-body p { ");
        sb.Append("font-family:").Append(bodyFont).Append(" !important; color:").Append(bodyColor).Append(" !important; } ");

        // Chapter title typeface + ink.
        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key).Append(" .reader-page-title,");
        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key).Append(" .reader-page-title,");
        sb.Append(".book-pdf-body.interior-").Append(spec.Key).Append(" .reader-page-title { ");
        sb.Append("font-family:").Append(titleFont).Append(" !important; color:").Append(titleColor).Append(" !important; } ");

        // Page surface colour per interior (so the page tint changes too; user hex picker still overrides via --ilt-page-bg).
        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key).Append(" .book-page-content-wrap,");
        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key).Append(" .book-page-background,");
        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key).Append(" .book-page-content-wrap { ");
        sb.Append("background:var(--ilt-page-bg, ").Append(spec.SheetBackground).Append("); } ");



        sb.Append("#book-formatter-root #paginatedReaderShell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content .reader-page-body p,");

        sb.Append(".paginated-reader-shell.interior-").Append(spec.Key);

        sb.Append(" .book-page-content .reader-page-body p,");

        sb.Append(".book-pdf-body.interior-").Append(spec.Key).Append(" .reader-page-body p { ");

        sb.Append("text-indent:").Append(spec.Body.TextIndent).Append(" !important; ");

        sb.Append("margin-bottom:").Append(spec.Body.ParagraphSpacing).Append(" !important; } ");



        if (interior == "Modern")

        {

            sb.Append("#book-formatter-root #paginatedReaderShell.interior-modern .book-page-content .reader-page-body p,");

            sb.Append(".paginated-reader-shell.interior-modern .book-page-content .reader-page-body p { ");

            sb.Append("text-indent:0 !important; border-left:3px solid var(--fmt-accent, #6366f1); padding-left:0.9rem !important; } ");

            sb.Append(".book-pdf-body.interior-modern .reader-page-body p { ");

            sb.Append("text-indent:0; border-left:3px solid var(--fmt-accent, #6366f1); padding-left:0.9rem; margin-bottom:0.85rem; } ");

        }



        if (interior == "Minimalist")

        {

            sb.Append("#book-formatter-root #paginatedReaderShell.interior-minimalist .book-page-content .reader-page-body p,");

            sb.Append(".book-pdf-body.interior-minimalist .reader-page-body p { text-indent:0 !important; text-indent:0; } ");

        }



        if (interior == "Classic")

        {

            sb.Append(".book-pdf-body.tpl-classic .reader-page-body.classic-body .manuscript-p:first-of-type::first-letter, ");

            sb.Append("#book-formatter-root #paginatedReaderShell.interior-classic .reader-page-body.classic-body p.classic-first-para::first-letter { ");

            sb.Append("float:left; font-size:3.2em; line-height:0.85; padding-right:0.08em; font-weight:600; color:#78350f; } ");

        }



        return sb.ToString();

    }



    /// <summary>Full CSS block for formatter preview — include once in Book Formatting view.</summary>

    public static string BuildFormatterSyncCss(BookPdfExportOptions? opt = null)

    {

        opt ??= new BookPdfExportOptions();

        var sb = new System.Text.StringBuilder(16384);

        sb.Append(BuildCssCustomProperties(opt));

        sb.Append(BuildSharedReaderLayoutCss());

        sb.Append(BuildTocCss());

        sb.Append(BuildPreviewPageChromeCss());

        sb.Append(BuildFrameAndSheetCss());

        sb.Append(BuildWebPreviewModeCss());

        foreach (var key in Specs.Keys)

            sb.Append(BuildPerInteriorCss(key));

        sb.Append(InteriorExportTheme.BuildFormatterInteriorCss());

        return sb.ToString();

    }



    /// <summary>JSON specs for live token sync in formatter preview (matches C# values).</summary>

    public static string BuildClientSpecsJson()

    {

        var interiors = Specs.ToDictionary(

            kv => kv.Key,

            kv => new

            {

                sheetBg = kv.Value.SheetBackground,

                padTop = kv.Value.SheetPadding.Top,

                padRight = kv.Value.SheetPadding.Right,

                padBottom = kv.Value.SheetPadding.Bottom,

                padLeft = kv.Value.SheetPadding.Left,

                pageBg = kv.Value.Frame.PageBackground,

                matBg = kv.Value.Frame.MatBackground,

                matBorder = kv.Value.Frame.MatBorder,

                sheetBorder = kv.Value.Frame.SheetBorder,

                sheetShadow = kv.Value.Frame.SheetShadow,

                containerShadow = kv.Value.Frame.ContainerShadow

            });

        var payload = new { spacing = InteriorSpacingTheme.ClientSpacingPayload(), interiors };

        return JsonSerializer.Serialize(payload);

    }



    /// <summary>

    /// Web preview — 6×9 sheet; frame/mat from <see cref="BuildFrameAndSheetCss"/>.

    /// PDF export uses the same tokens via <see cref="InteriorExportTheme.BuildPdfThemeCss"/>.

    /// </summary>

    public static string BuildWebPreviewModeCss()

    {

        return string.Concat(

            "[data-interior-mode=\"web\"] { --preview-page-max: 6in; --preview-page-min-h: 9in; } ",

            "[data-interior-mode=\"web\"] .book-preview-sheet, ",

            "[data-interior-mode=\"web\"] .book-page-content-wrap { ",

            "background: var(--ilt-page-bg, var(--ilt-sheet-bg, #fffdf8)); ",

            "border: var(--ilt-sheet-border, none); ",

            "border-radius: 2px; ",

            "-webkit-print-color-adjust: exact; print-color-adjust: exact; } ",

            "[data-interior-mode=\"web\"] .paginated-reader-shell { padding: clamp(0.45rem, 1.2vw, 0.75rem); } ",

            "[data-interior-mode=\"web\"] section.chapter { position: relative; } ",

            "[data-interior-mode=\"web\"] section.chapter::before { ",

            "content: attr(data-running-head); display: block; text-align: center; ",

            "font-family: Georgia, 'Times New Roman', serif; font-size: 0.62rem; ",

            "letter-spacing: 0.22em; text-transform: uppercase; color: #7c7368; ",

            "margin: 0 0 1.1rem; padding-top: 0.15rem; overflow: hidden; ",

            "text-overflow: ellipsis; white-space: nowrap; } ",

            "[data-interior-mode=\"web\"] .toc-link { cursor: pointer; } ");

    }

}


