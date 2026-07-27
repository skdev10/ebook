using EBookDashboard.Configuration;

namespace EBookDashboard.Services;

/// <summary>
/// Amazon KDP-style interior margin presets for formatting preview.
/// Values come from <see cref="KdpSpecs"/>.
/// </summary>
public static class KdpInteriorMarginPresets
{
    private static KdpSpecs S => KdpSpecsAccessor.Current;

    public static double BleedIn => S.BleedIn;
    public static double MinOuterNoBleedIn => S.InteriorMargins.RecommendedOuterNoBleedIn;
    public static double ComfortOuterNoBleedIn => S.InteriorMargins.RecommendedOuterNoBleedIn;
    public static double SafeFromTrimWithBleedIn => S.InteriorMargins.RecommendedOuterWithBleedIn;

    public sealed record TrimPreset(string Key, string Label, double WidthIn, double HeightIn, string Group = "Standard");

    public static IReadOnlyList<TrimPreset> TrimPresets =>
        S.TrimPresets.Select(t => new TrimPreset(t.Key, t.Label, t.WidthIn, t.HeightIn, t.Group)).ToList();

    public static TrimPreset ResolveTrim(string? key)
    {
        var presets = TrimPresets;
        var k = (key ?? "6x9").Trim().ToLowerInvariant();
        return presets.FirstOrDefault(t => t.Key.Equals(k, StringComparison.OrdinalIgnoreCase))
               ?? presets.FirstOrDefault(t => t.Key == "6x9")
               ?? presets[0];
    }

    public static double RecommendedInsideIn(int pageCount) =>
        new KdpCalculationService(S).GetRecommendedMargins(Math.Max(1, pageCount), hasBleed: false).InsideIn;

    public static (double Top, double Bottom, double Inside, double Outside) RecommendedMargins(
        int pageCount, bool bleed)
    {
        var m = new KdpCalculationService(S).GetRecommendedMargins(pageCount, bleed);
        return (m.TopIn, m.BottomIn, m.InsideIn, m.OutsideIn);
    }

    public static object ClientPayload(int estimatedPageCount = 200)
    {
        var calc = new KdpCalculationService(S);
        var recommended = calc.GetRecommendedMargins(estimatedPageCount, false);
        var recommendedBleed = calc.GetRecommendedMargins(estimatedPageCount, true);
        return new
        {
            bleedIn = BleedIn,
            minOuterNoBleedIn = MinOuterNoBleedIn,
            comfortOuterNoBleedIn = ComfortOuterNoBleedIn,
            safeFromTrimWithBleedIn = SafeFromTrimWithBleedIn,
            trimPresets = TrimPresets.Select(t => new { key = t.Key, label = t.Label, w = t.WidthIn, h = t.HeightIn, group = t.Group }),
            recommended = (recommended.TopIn, recommended.BottomIn, recommended.InsideIn, recommended.OutsideIn),
            recommendedBleed = (recommendedBleed.TopIn, recommendedBleed.BottomIn, recommendedBleed.InsideIn, recommendedBleed.OutsideIn),
            defaults = new
            {
                top = InteriorSpacingTheme.MarginTopIn,
                bottom = InteriorSpacingTheme.MarginBottomIn,
                inside = InteriorSpacingTheme.MarginInsideIn,
                outside = InteriorSpacingTheme.MarginOutsideIn
            }
        };
    }
}
