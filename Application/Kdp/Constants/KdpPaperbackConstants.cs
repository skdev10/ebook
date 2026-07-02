namespace EBookDashboard.Application.Kdp.Constants;

/// <summary>
/// Amazon KDP paperback cover constants (inches unless noted).
/// Defaults match KDP Cover Calculator: Paperback, Standard Color, White Paper, RTL, bleed on.
/// </summary>
public static class KdpPaperbackConstants
{
    public const string BindingTypePaperback = "Paperback";
    public const string InteriorTypeStandardColor = "Standard Color";
    public const string PaperTypeWhite = "White Paper";
    public const string ReadingDirectionRtl = "Right To Left";
    public const string MeasurementUnitInches = "Inches";

    /// <summary>Minimum page count for KDP paperback (24 pages).</summary>
    public const int MinPageCount = 24;

    /// <summary>Maximum page count for KDP paperback white paper.</summary>
    public const int MaxPageCount = 828;

    public const int DefaultDpi = 150;
    public const decimal DefaultTrimWidthInches = 6m;
    public const decimal DefaultTrimHeightInches = 9m;

    /// <summary>Bleed extension beyond trim on each edge when bleed is enabled.</summary>
    public const decimal BleedInches = 0.125m;

    /// <summary>Standard Color + White Paper spine factor (inches per page).</summary>
    public const decimal StandardColorWhitePaperSpinePerPage = 0.002252m;

    /// <summary>Black &amp; white + White Paper spine factor.</summary>
    public const decimal BlackWhiteWhitePaperSpinePerPage = 0.002252m;

    /// <summary>Cream paper spine factor.</summary>
    public const decimal CreamPaperSpinePerPage = 0.0025m;

    /// <summary>Premium / standard color alternate factor used by legacy KDP tables.</summary>
    public const decimal ColorSpinePerPage = 0.002347m;

    /// <summary>Safe area inset: trim width minus this value = safeAreaWidth.</summary>
    public const decimal SafeAreaWidthReduction = 0.125m;

    /// <summary>Safe area inset: trim height minus this value = safeAreaHeight.</summary>
    public const decimal SafeAreaHeightReduction = 0.25m;

    /// <summary>Minimum distance from spine fold for live text/graphics.</summary>
    public const decimal SpineMarginInches = 0.062m;

    /// <summary>Barcode clearance on back cover (from trim edge).</summary>
    public const decimal BarcodeMarginInches = 0.25m;

    /// <summary>KDP reserved barcode area width (inches) — must remain clear of text/art.</summary>
    public const decimal BarcodeZoneWidthInches = 2.0m;

    /// <summary>KDP reserved barcode area height (inches) — must remain clear of text/art.</summary>
    public const decimal BarcodeZoneHeightInches = 1.2m;

    /// <summary>Decimal places for inch dimensions in API responses (matches KDP calculator display).</summary>
    public const int InchDecimalPlaces = 3;
}
