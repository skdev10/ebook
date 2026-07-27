using EBookDashboard.Configuration;
using EBookDashboard.Models;

namespace EBookDashboard.Services;

/// <summary>
/// Convenience accessors over <see cref="KdpSpecsAccessor.Current"/>.
/// Numeric values live only in the KdpSpecs configuration section.
/// </summary>
public static class KdpConstants
{
    private static KdpSpecs S => KdpSpecsAccessor.Current;

    public static int Dpi => S.Dpi;
    public static double Bleed => S.BleedIn;
    public static double SafeFromTrim => S.SafeFromTrimIn;
    public static double SpineSafe => S.SpineTextMinWidthIn;

    /// <summary>Legacy name — prefer spine-width check via <see cref="KdpCalculationService.IsSpineTextAdvisable"/>.</summary>
    public static int SpineTextMinPages => 0;

    public static double HardcoverWrap => S.Hardcover.WrapAllowancePerEdgeIn;
    public static double HardcoverHinge => 0;
    public static double HardcoverHeightExtra => 0;
    public static double HardcoverThickness => S.PaperThicknessInPerPage.White;

    public static IReadOnlyDictionary<PaperType, double> PaperThickness =>
        new Dictionary<PaperType, double>
        {
            { PaperType.White, S.PaperThicknessInPerPage.White },
            { PaperType.Cream, S.PaperThicknessInPerPage.Cream },
            { PaperType.PremiumColor, S.PaperThicknessInPerPage.PremiumColor },
            { PaperType.StandardColor, S.PaperThicknessInPerPage.StandardColor },
        };
}
