using EBookDashboard.Models;

namespace EBookDashboard.Services;

public static class KdpConstants
{
    public const int Dpi = 300;
    public const double Bleed = 0.125;
    public const int SpineTextMinPages = 79;

    public const double HardcoverWrap = 0.591;
    public const double HardcoverHinge = 0.394;
    public const double HardcoverHeightExtra = 0.236;
    public const double HardcoverThickness = 0.002347;

    public const double SafeFromTrim = 0.25;
    public const double SpineSafe = 0.0625;

    public static readonly IReadOnlyDictionary<PaperType, double> PaperThickness =
        new Dictionary<PaperType, double>
        {
            { PaperType.White, 0.002252 },
            { PaperType.Cream, 0.0025 },
            { PaperType.PremiumColor, 0.002347 },
            { PaperType.StandardColor, 0.002252 },
        };
}
