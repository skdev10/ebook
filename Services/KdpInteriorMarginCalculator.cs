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
    /// Apply KDP readable margin defaults for print export (outside ≥0.5″, top/bottom ≥0.6″, gutter by page-count tier).
    /// </summary>
    public static void ApplyDefaults(BookPdfExportOptions opt, int estimatedPageCount)
    {
        if (opt == null) return;
        var (top, bottom, inside, outside) = KdpInteriorMarginPresets.RecommendedMargins(estimatedPageCount, opt.UseBleed);
        opt.MarginInsideIn = inside;
        opt.MarginOutsideIn = outside;
        opt.MarginTopIn = top;
        opt.MarginBottomIn = bottom;
    }
}
