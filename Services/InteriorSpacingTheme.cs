using System.Globalization;

namespace EBookDashboard.Services;

/// <summary>
/// Authoritative millimeter spacing for 6×9 KDP trade paperback interiors.
/// <see cref="InteriorLayoutTokens"/> and both preview/PDF CSS builders derive every numeric
/// margin, padding, and rhythm value from these constants — never duplicate numbers elsewhere.
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

    // ── Page content padding (Novel / Traditional default) ──────────────────────
    // Reference: Amazon trade paperback — top 0.85", bottom 0.8", inside 0.75", outside 0.55".

    /// <summary>Top padding — breathing room below running head.</summary>
    public const double PageTopPaddingMm = 21.59;

    /// <summary>Bottom padding — space above folio.</summary>
    public const double PageBottomPaddingMm = 20.32;

    /// <summary>Inside (binding/gutter) margin — wider for comfortable thumb rest.</summary>
    public const double PageInsideMarginMm = 19.05;

    /// <summary>Outside (fore-edge) margin.</summary>
    public const double PageOutsideMarginMm = 13.97;

    /// <summary>Centered text column — ≈65 characters at 11–12pt (60–75 CPL target).</summary>
    public const double TextColumnWidthMm = 106.68;

    /// <summary>Vertical drop before chapter title on new chapter pages (test 24 / 26 / 28).</summary>
    public const double ChapterDropMm = 26.0;

    /// <summary>Alias — running head / folio reserved band (see <see cref="RunningHeadHeightMm"/>).</summary>
    public static double HeaderFooterMarginMm => RunningHeadHeightMm;

    /// <summary>Space between paragraphs (justified + indented prose).</summary>
    public const double ParagraphSpacingMm = 1.9;

    /// <summary>First-line indent for traditional novel interiors.</summary>
    public const double FirstLineIndentMm = 6.35;

    // ── Chromium @page margin box (outside the padded text block) ───────────────

    public const double PrintMarginTopMm = 15.75;
    public const double PrintMarginBottomMm = 14.73;
    public const double PrintMarginInsideMm = 10.67;
    public const double PrintMarginOutsideMm = 8.13;

    // ── Running head / folio chrome ─────────────────────────────────────────────

    /// <summary>Reserved height for running head band.</summary>
    public const double RunningHeadHeightMm = 10.16;

    /// <summary>Air between running head baseline and first body line.</summary>
    public const double RunningHeadGapBelowMm = 4.57;

    /// <summary>Reserved height for folio (page number) band.</summary>
    public const double FolioHeightMm = 9.65;

    /// <summary>Air between last body line and folio.</summary>
    public const double FolioGapAboveMm = 4.06;

    /// <summary>Horizontal inset for running head on outside edge.</summary>
    public const double HeaderFooterOutsideInsetMm = 14.73;

    /// <summary>Horizontal inset for running head on inside (gutter) edge.</summary>
    public const double HeaderFooterInsideInsetMm = 13.21;

    // ── Front matter ────────────────────────────────────────────────────────────

    public const double FrontMatterPadTopMm = 29.21;
    public const double TitlePagePadTopMm = 82.55;

    // ── Line-height multipliers (formatter UI → exact CSS number) ───────────────

    /// <summary>Tight — dense reference or back matter.</summary>
    public const double LineHeightTight = 1.4;

    /// <summary>Normal — standard trade paperback body.</summary>
    public const double LineHeightNormal = 1.6;

    /// <summary>Medium — alias of Normal in formatter UI.</summary>
    public const double LineHeightMedium = 1.6;

    /// <summary>Relaxed — default elegant spacing (Medium + 1.8 in formatter).</summary>
    public const double LineHeightRelaxed = 1.8;

    /// <summary>Loose — poetry, large type, or accessibility.</summary>
    public const double LineHeightLoose = 2.0;

    /// <summary>Formats a millimeter value as a CSS length (e.g. <c>26mm</c>).</summary>
    public static string Mm(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture) + "mm";

    /// <summary>Converts millimeters to inches for CSS (4 dp).</summary>
    public static string MmToIn(double mm) =>
        (mm / MmPerInch).ToString("0.####", CultureInfo.InvariantCulture) + "in";

    /// <summary>Trade paperback page padding — top, outside, bottom, inside.</summary>
    public static InteriorLayoutTokens.ContentPaddingSpec TradePaperbackPagePadding =>
        new(
            MmToIn(PageTopPaddingMm),
            MmToIn(PageOutsideMarginMm),
            MmToIn(PageBottomPaddingMm),
            MmToIn(PageInsideMarginMm));

    /// <summary>KDP 6×9 Chromium margin box.</summary>
    public static InteriorLayoutTokens.PrintMarginSpec KdpPrintMarginBox =>
        new(
            MmToIn(PrintMarginTopMm),
            MmToIn(PrintMarginBottomMm),
            MmToIn(PrintMarginInsideMm),
            MmToIn(PrintMarginOutsideMm));

    /// <summary>Shared spacing block for client-side token sync (formatter JS).</summary>
    public static object ClientSpacingPayload() => new
    {
        chapterDrop = Mm(ChapterDropMm),
        textMax = MmToIn(TextColumnWidthMm),
        paraSpace = Mm(ParagraphSpacingMm),
        textIndent = Mm(FirstLineIndentMm),
        runningHeadH = MmToIn(RunningHeadHeightMm),
        runningHeadGapBelow = MmToIn(RunningHeadGapBelowMm),
        folioH = MmToIn(FolioHeightMm),
        folioGapAbove = MmToIn(FolioGapAboveMm),
        frontPadTop = MmToIn(FrontMatterPadTopMm),
        titlePagePadTop = MmToIn(TitlePagePadTopMm),
        lineHeightTight = LineHeightTight.ToString("0.#", CultureInfo.InvariantCulture),
        lineHeightNormal = LineHeightNormal.ToString("0.#", CultureInfo.InvariantCulture),
        lineHeightRelaxed = LineHeightRelaxed.ToString("0.#", CultureInfo.InvariantCulture),
        lineHeightLoose = LineHeightLoose.ToString("0.#", CultureInfo.InvariantCulture)
    };
}
