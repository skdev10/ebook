using System.Globalization;

namespace EBookDashboard.Services;

/// <summary>
/// Authoritative spacing for 6×9 KDP trade paperback interiors (Reedsy / trade-novel conventions).
/// <see cref="InteriorLayoutTokens"/> and preview/PDF CSS derive every margin from these constants.
/// </summary>
public static class InteriorSpacingTheme
{
    /// <summary>Millimeters per inch (KDP / CSS conversion).</summary>
    public const double MmPerInch = 25.4;

    // ── Trim (6×9 trade paperback) ──────────────────────────────────────────────

    /// <summary>Page width — 6in trim.</summary>
    public const double TrimWidthMm = 152.4;

    /// <summary>Page height — 9in trim.</summary>
    public const double TrimHeightMm = 228.6;

    // ── Industry 6×9 page margins (inches — thebookdesigner / trade fiction) ───

    /// <summary>Inside (gutter) margin — 13/16″ (0.75–0.875″ range).</summary>
    public const double MarginInsideIn = 0.8125;

    /// <summary>Outside (fore-edge) margin — 5/8″ (0.5–0.75″ range).</summary>
    public const double MarginOutsideIn = 0.625;

    /// <summary>Top margin from trim to running-head band.</summary>
    public const double MarginTopIn = 0.625;

    /// <summary>Bottom margin — 7/8″ (0.75–1″ range).</summary>
    public const double MarginBottomIn = 0.875;

    /// <summary>Clean Minimalist — extra white space on all edges.</summary>
    public const double MinimalistMarginInsideIn = 0.875;

    public const double MinimalistMarginOutsideIn = 0.75;

    public const double MinimalistMarginTopIn = 0.75;

    public const double MinimalistMarginBottomIn = 1.0;

    // ── Derived page padding (mm) ───────────────────────────────────────────────

    /// <summary>Top padding — breathing room below trim before running head.</summary>
    public static double PageTopPaddingMm => InToMm(MarginTopIn);

    /// <summary>Bottom padding — space above folio / foot of page.</summary>
    public static double PageBottomPaddingMm => InToMm(MarginBottomIn);

    /// <summary>Inside (binding/gutter) padding.</summary>
    public static double PageInsideMarginMm => InToMm(MarginInsideIn);

    /// <summary>Outside (fore-edge) padding.</summary>
    public static double PageOutsideMarginMm => InToMm(MarginOutsideIn);

    /// <summary>Text uses full measure between gutters (not a narrow column).</summary>
    public static double TextColumnWidthMm =>
        TrimWidthMm - PageInsideMarginMm - PageOutsideMarginMm;

    /// <summary>
    /// Chapter title sink — margin below running head so title sits ~¼ page down (≈2.25″ from trim).
    /// </summary>
    public const double ChapterTitleSinkIn = 1.25;

    public static double ChapterDropMm => InToMm(ChapterTitleSinkIn);

    /// <summary>Alias — running head / folio reserved band.</summary>
    public static double HeaderFooterMarginMm => RunningHeadHeightMm;

    /// <summary>Space between justified paragraphs (trade fiction — indent only, no extra lead).</summary>
    public const double ParagraphSpacingMm = 0;

    /// <summary>Clean Minimalist — looser paragraph rhythm.</summary>
    public const double MinimalistParagraphSpacingMm = 4.5;

    /// <summary>First-line indent — ≈1.5 characters at 11pt.</summary>
    public const double FirstLineIndentMm = 3.5;

    // ── Chromium @page margin box (legacy reference — HTML padding is authoritative) ──

    public static double PrintMarginTopMm => PageTopPaddingMm;

    public static double PrintMarginBottomMm => PageBottomPaddingMm;

    public static double PrintMarginInsideMm => PageInsideMarginMm;

    public static double PrintMarginOutsideMm => PageOutsideMarginMm;

    // ── Running head / folio chrome ─────────────────────────────────────────────

    /// <summary>Reserved height for running head band (~0.35″).</summary>
    public const double RunningHeadHeightMm = 8.89;

    /// <summary>Air between running head baseline and chapter title / body.</summary>
    public const double RunningHeadGapBelowMm = 4.57;

    /// <summary>Gap between <c>.page-header</c> and <c>.page-body</c> in export HTML (~12px).</summary>
    public const string PageHeaderBodyGapCss = "0.75rem";

    /// <summary>Reserved height for folio (page number) band.</summary>
    public const double FolioHeightMm = 9.65;

    /// <summary>Air between last body line and folio.</summary>
    public const double FolioGapAboveMm = 4.06;

    /// <summary>Horizontal inset for running head on outside edge (matches fore-edge margin).</summary>
    public static double HeaderFooterOutsideInsetMm => PageOutsideMarginMm;

    /// <summary>Horizontal inset for running head on inside (gutter) edge.</summary>
    public static double HeaderFooterInsideInsetMm => PageInsideMarginMm;

    // ── Front matter ────────────────────────────────────────────────────────────

    public const double FrontMatterPadTopMm = 29.21;

    public const double TitlePagePadTopMm = 82.55;

    // ── Line-height multipliers (formatter UI → exact CSS number) ───────────────

    public const double LineHeightTight = 1.4;

    public const double LineHeightNormal = 1.6;

    public const double LineHeightMedium = 1.6;

    public const double LineHeightRelaxed = 1.8;

    public const double LineHeightLoose = 2.0;

    public static double InToMm(double inches) => inches * MmPerInch;

    /// <summary>Formats a millimeter value as a CSS length (e.g. <c>26mm</c>).</summary>
    public static string Mm(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture) + "mm";

    /// <summary>Converts millimeters to inches for CSS (4 dp).</summary>
    public static string MmToIn(double mm) =>
        (mm / MmPerInch).ToString("0.####", CultureInfo.InvariantCulture) + "in";

    /// <summary>Converts inches to CSS length.</summary>
    public static string In(double inches) =>
        inches.ToString("0.####", CultureInfo.InvariantCulture) + "in";

    /// <summary>Standard trade paperback page padding — top, outside, bottom, inside (gutter).</summary>
    public static InteriorLayoutTokens.ContentPaddingSpec TradePaperbackPagePadding =>
        new(
            In(MarginTopIn),
            In(MarginOutsideIn),
            In(MarginBottomIn),
            In(MarginInsideIn));

    /// <summary>Clean Minimalist — more white space on every edge.</summary>
    public static InteriorLayoutTokens.ContentPaddingSpec MinimalistPagePadding =>
        new(
            In(MinimalistMarginTopIn),
            In(MinimalistMarginOutsideIn),
            In(MinimalistMarginBottomIn),
            In(MinimalistMarginInsideIn));

    /// <summary>KDP 6×9 Chromium margin box (reference — export uses @page margin 0 + HTML padding).</summary>
    public static InteriorLayoutTokens.PrintMarginSpec KdpPrintMarginBox =>
        new(
            In(MarginTopIn),
            In(MarginBottomIn),
            In(MarginInsideIn),
            In(MarginOutsideIn));

    /// <summary>Chromium margin reference when header/footer lived outside HTML (superseded by in-page chrome).</summary>
    public static InteriorLayoutTokens.PrintMarginSpec PdfExportChromiumMargins =>
        new(
            In(MarginTopIn + (RunningHeadHeightMm + RunningHeadGapBelowMm) / MmPerInch),
            In(MarginBottomIn + (FolioHeightMm + FolioGapAboveMm) / MmPerInch),
            In(MarginInsideIn),
            In(MarginOutsideIn));

    /// <summary>Shared spacing block for client-side token sync (formatter JS).</summary>
    public static object ClientSpacingPayload() => new
    {
        chapterDrop = In(ChapterTitleSinkIn),
        textMax = "100%",
        paraSpace = Mm(ParagraphSpacingMm),
        textIndent = Mm(FirstLineIndentMm),
        runningHeadH = MmToIn(RunningHeadHeightMm),
        runningHeadGapBelow = MmToIn(RunningHeadGapBelowMm),
        folioH = MmToIn(FolioHeightMm),
        folioGapAbove = MmToIn(FolioGapAboveMm),
        frontPadTop = MmToIn(FrontMatterPadTopMm),
        titlePagePadTop = MmToIn(TitlePagePadTopMm),
        marginInside = In(MarginInsideIn),
        marginOutside = In(MarginOutsideIn),
        marginTop = In(MarginTopIn),
        marginBottom = In(MarginBottomIn),
        lineHeightTight = LineHeightTight.ToString("0.#", CultureInfo.InvariantCulture),
        lineHeightNormal = LineHeightNormal.ToString("0.#", CultureInfo.InvariantCulture),
        lineHeightRelaxed = LineHeightRelaxed.ToString("0.#", CultureInfo.InvariantCulture),
        lineHeightLoose = LineHeightLoose.ToString("0.#", CultureInfo.InvariantCulture),
        typography = InteriorTypographyPresets.ClientTypographyPayload()
    };
}
