using EBookDashboard.Configuration;

namespace EBookDashboard.Application.Kdp.Constants;

/// <summary>
/// Amazon KDP paperback cover accessors. Numeric values come only from
/// <see cref="KdpSpecsAccessor.Current"/> (appsettings "KdpSpecs").
/// </summary>
public static class KdpPaperbackConstants
{
    private static KdpSpecs S => KdpSpecsAccessor.Current;

    public const string BindingTypePaperback = "Paperback";
    public const string InteriorTypeStandardColor = "Standard Color";
    public const string PaperTypeWhite = "White Paper";
    public const string ReadingDirectionRtl = "Right To Left";
    public const string MeasurementUnitInches = "Inches";

    public static int MinPageCount => S.Paperback.MinPages;
    public static int MaxPageCount => S.Paperback.MaxPages;

    public static int DefaultDpi => S.Dpi;
    public static decimal DefaultTrimWidthInches =>
        (decimal)(S.TrimPresets.FirstOrDefault(t => t.IsDefault)?.WidthIn
                  ?? S.TrimPresets.FirstOrDefault(t => t.Key == "6x9")?.WidthIn
                  ?? 6.0);
    public static decimal DefaultTrimHeightInches =>
        (decimal)(S.TrimPresets.FirstOrDefault(t => t.IsDefault)?.HeightIn
                  ?? S.TrimPresets.FirstOrDefault(t => t.Key == "6x9")?.HeightIn
                  ?? 9.0);

    public static decimal BleedInches => (decimal)S.BleedIn;
    public static decimal StandardColorWhitePaperSpinePerPage => (decimal)S.PaperThicknessInPerPage.StandardColor;
    public static decimal BlackWhiteWhitePaperSpinePerPage => (decimal)S.PaperThicknessInPerPage.White;
    public static decimal CreamPaperSpinePerPage => (decimal)S.PaperThicknessInPerPage.Cream;
    public static decimal ColorSpinePerPage => (decimal)S.PaperThicknessInPerPage.PremiumColor;

    public static decimal SafeAreaWidthReduction => (decimal)S.BleedIn;
    public static decimal SafeAreaHeightReduction => (decimal)S.SafeFromTrimIn;
    public static decimal SpineMarginInches => (decimal)S.SpineTextMinWidthIn;
    public static decimal BarcodeMarginInches => (decimal)S.BarcodeMarginFromTrimIn;
    public static decimal BarcodeZoneWidthInches => (decimal)S.BarcodeWidthIn;
    public static decimal BarcodeZoneHeightInches => (decimal)S.BarcodeHeightIn;

    public const int InchDecimalPlaces = 3;
}
