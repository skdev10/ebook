namespace EBookDashboard.Services;

/// <summary>
/// Amazon KDP–style interior margin presets for formatting preview (trim / gutter / bleed).
/// Values follow commonly published KDP guidance; Amazon’s template remains authoritative for print.
/// </summary>
public static class KdpInteriorMarginPresets
{
    /// <summary>Bleed beyond trim on each outer edge (inches).</summary>
    public const double BleedIn = 0.125;

    /// <summary>Minimum outside / top / bottom when not using generous text-block margins.</summary>
    public const double MinOuterNoBleedIn = 0.25;

    /// <summary>Comfortable no-bleed outer margins for a readable text block.</summary>
    public const double ComfortOuterNoBleedIn = 0.5;

    /// <summary>Safe inset from trim when bleed is enabled (content must stay inside).</summary>
    public const double SafeFromTrimWithBleedIn = 0.375;

    public sealed record TrimPreset(string Key, string Label, double WidthIn, double HeightIn);

    public static readonly IReadOnlyList<TrimPreset> TrimPresets =
    [
        new("6x9", "6 × 9 in (default)", 6.0, 9.0),
        new("5x8", "5 × 8 in", 5.0, 8.0),
        new("5.5x8.5", "5.5 × 8.5 in", 5.5, 8.5),
        new("8.5x11", "8.5 × 11 in", 8.5, 11.0),
        new("8x10", "8 × 10 in (children’s)", 8.0, 10.0),
        new("8.5x8.5", "8.5 × 8.5 in (children’s)", 8.5, 8.5)
    ];

    public static TrimPreset ResolveTrim(string? key)
    {
        var k = (key ?? "6x9").Trim().ToLowerInvariant();
        return TrimPresets.FirstOrDefault(t => t.Key.Equals(k, StringComparison.OrdinalIgnoreCase))
               ?? TrimPresets[0];
    }

    /// <summary>Inside (gutter) margin by estimated page count — KDP stepped guidance.</summary>
    public static double RecommendedInsideIn(int pageCount)
    {
        if (pageCount <= 150) return 0.375;
        if (pageCount <= 300) return 0.5;
        if (pageCount <= 500) return 0.625;
        if (pageCount <= 700) return 0.75;
        return 0.875;
    }

    /// <summary>
    /// Recommended margins when “Use Amazon KDP recommended margins &amp; gutter” is checked.
    /// Outside/top/bottom use comfortable text-block values; with bleed, safe zone ≥ 0.375″ from trim.
    /// </summary>
    public static (double Top, double Bottom, double Inside, double Outside) RecommendedMargins(
        int pageCount, bool bleed)
    {
        var inside = RecommendedInsideIn(Math.Max(1, pageCount));
        if (bleed)
        {
            var safe = Math.Max(SafeFromTrimWithBleedIn, ComfortOuterNoBleedIn);
            return (safe, safe, inside, safe);
        }

        var outer = Math.Max(MinOuterNoBleedIn, ComfortOuterNoBleedIn);
        // Prefer existing trade defaults when close (6×9 novel feel).
        var top = Math.Max(outer, InteriorSpacingTheme.MarginTopIn);
        var bottom = Math.Max(outer, InteriorSpacingTheme.MarginBottomIn);
        var outside = Math.Max(outer, InteriorSpacingTheme.MarginOutsideIn);
        return (top, bottom, Math.Max(inside, InteriorSpacingTheme.MarginInsideIn * 0.9), outside);
    }

    /// <summary>Client payload for Formatting preview JS.</summary>
    public static object ClientPayload(int estimatedPageCount = 200) => new
    {
        bleedIn = BleedIn,
        minOuterNoBleedIn = MinOuterNoBleedIn,
        comfortOuterNoBleedIn = ComfortOuterNoBleedIn,
        safeFromTrimWithBleedIn = SafeFromTrimWithBleedIn,
        trimPresets = TrimPresets.Select(t => new { key = t.Key, label = t.Label, w = t.WidthIn, h = t.HeightIn }),
        recommended = RecommendedMargins(estimatedPageCount, false),
        recommendedBleed = RecommendedMargins(estimatedPageCount, true),
        defaults = new
        {
            top = InteriorSpacingTheme.MarginTopIn,
            bottom = InteriorSpacingTheme.MarginBottomIn,
            inside = InteriorSpacingTheme.MarginInsideIn,
            outside = InteriorSpacingTheme.MarginOutsideIn
        }
    };
}
