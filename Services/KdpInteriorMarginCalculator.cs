using EBookDashboard.Configuration;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>
/// KDP interior gutter/outside margin helpers — page-count tiers from <see cref="KdpSpecs"/>.
/// </summary>
public static class KdpInteriorMarginCalculator
{
    /// <summary>Inside (gutter) margin in inches for the given page count.</summary>
    public static double InsideMarginIn(int pageCount, KdpSpecs? specs = null)
    {
        var margins = (specs ?? KdpSpecsAccessor.Current).InteriorMargins;
        var pages = Math.Max(1, pageCount);
        foreach (var tier in margins.GutterTiers.OrderBy(t => t.MaxPageCount))
        {
            if (pages <= tier.MaxPageCount)
                return tier.InsideIn;
        }

        return margins.FallbackInsideIn;
    }

    /// <summary>Outside/top/bottom minimum in inches (bleed-aware).</summary>
    public static double OutsideMarginIn(bool useBleed, KdpSpecs? specs = null)
    {
        var margins = (specs ?? KdpSpecsAccessor.Current).InteriorMargins;
        return useBleed ? margins.RecommendedOuterWithBleedIn : margins.RecommendedOuterNoBleedIn;
    }

    /// <summary>
    /// Apply KDP margin defaults onto export options when the user has not set explicit margins.
    /// </summary>
    public static void ApplyDefaults(BookPdfExportOptions opt, int estimatedPageCount)
    {
        if (opt == null) return;
        var (top, bottom, inside, outside) = KdpInteriorMarginPresets.RecommendedMargins(estimatedPageCount, opt.UseBleed);
        if (opt.MarginInsideIn is null or <= 0)
            opt.MarginInsideIn = inside;
        if (opt.MarginOutsideIn is null or <= 0)
            opt.MarginOutsideIn = outside;
        if (opt.MarginTopIn is null or <= 0)
            opt.MarginTopIn = top;
        if (opt.MarginBottomIn is null or <= 0)
            opt.MarginBottomIn = bottom;
    }
}
