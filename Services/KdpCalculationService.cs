using EBookDashboard.Configuration;
using EBookDashboard.Models;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services;

/// <summary>Recommended interior margin set in inches (display: 4 dp).</summary>
public readonly record struct MarginSet(
    double TopIn,
    double BottomIn,
    double OutsideIn,
    double InsideIn);

public enum DpiSeverity
{
    Ok = 0,
    Warning = 1,
    Poor = 2
}

public readonly record struct ImageDpiCheckResult(double EffectiveDpi, DpiSeverity Severity);

/// <summary>
/// Pure, testable KDP calculation helpers. All numeric inputs come from <see cref="KdpSpecs"/>.
/// </summary>
public sealed class KdpCalculationService
{
    private readonly KdpSpecs _specs;

    public KdpCalculationService(IOptions<KdpSpecs> options)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)))
    {
    }

    public KdpCalculationService(KdpSpecs specs)
    {
        _specs = specs ?? throw new ArgumentNullException(nameof(specs));
    }

    /// <summary>
    /// Recommended margins. Outside/top/bottom use Section 2 minima (0.25 no bleed / 0.375 with bleed).
    /// Inside (gutter) follows configured page-count tiers.
    /// </summary>
    public MarginSet GetRecommendedMargins(int pageCount, bool hasBleed)
    {
        var pages = Math.Max(1, pageCount);
        var inside = ResolveInsideMarginIn(pages);
        var outer = hasBleed
            ? _specs.InteriorMargins.RecommendedOuterWithBleedIn
            : _specs.InteriorMargins.RecommendedOuterNoBleedIn;
        return RoundMargins(outer, outer, outer, inside);
    }

    /// <summary>Active gutter band label for UI, e.g. "151–300 pages".</summary>
    public string GetActiveGutterBandLabel(int pageCount)
    {
        var pages = Math.Max(1, pageCount);
        var tiers = _specs.InteriorMargins.GutterTiers.OrderBy(t => t.MaxPageCount).ToList();
        var prev = 1;
        foreach (var tier in tiers)
        {
            if (pages <= tier.MaxPageCount)
                return $"{prev}–{tier.MaxPageCount} pages";
            prev = tier.MaxPageCount + 1;
        }
        return $"{prev}+ pages";
    }

    public (int Min, int Max) GetPageCountLimits(ProjectType type, PaperType paper)
    {
        return type switch
        {
            ProjectType.Ebook => (_specs.Ebook.MinPages, _specs.Ebook.MaxPages),
            ProjectType.Hardcover => (_specs.Hardcover.MinPages, _specs.Hardcover.MaxPages),
            ProjectType.Paperback => ResolvePaperbackLimits(paper),
            _ => ResolvePaperbackLimits(paper)
        };
    }

    /// <summary>Warning-only page count message for non-native speakers. Never blocks export.</summary>
    public string? ValidatePageCount(int pageCount, ProjectType type, PaperType paper)
    {
        if (type == ProjectType.Ebook)
            return null;

        var (min, max) = GetPageCountLimits(type, paper);
        var kind = type == ProjectType.Hardcover ? "hardcover" : "paperback";

        if (pageCount < min)
        {
            return $"Your {kind} has fewer than the recommended minimum pages for Amazon KDP " +
                   $"({min} pages). You can still export the PDF, but KDP may not accept this configuration.";
        }

        if (pageCount > max)
        {
            return $"Your {kind} has more than the recommended maximum pages for Amazon KDP " +
                   $"({max} pages). You can still export the PDF, but KDP may not accept this configuration.";
        }

        return null;
    }

    /// <summary>
    /// Paperback: pageCount × thickness.
    /// Hardcover: (pageCount / 2) × thickness + SpineCaseExtraIn.
    /// </summary>
    public double CalculateSpineWidthIn(int pageCount, PaperType paper, ProjectType type)
    {
        var pages = Math.Max(0, pageCount);
        var thickness = _specs.ResolveThicknessInPerPage(paper);

        if (type == ProjectType.Hardcover)
        {
            var spine = (pages / 2.0) * thickness + _specs.Hardcover.SpineCaseExtraIn;
            return Math.Round(spine, 4);
        }

        return Math.Round(pages * thickness, 4);
    }

    /// <summary>
    /// Full cover size. Paperback: 2×trim + spine + 2×bleed on width, trim + 2×bleed on height.
    /// Hardcover: same trim panels + spine + 2× WrapAllowancePerEdgeIn on each dimension.
    /// </summary>
    public (double WidthIn, double HeightIn) CalculateFullCoverSizeIn(
        double trimW,
        double trimH,
        double spineW,
        CoverType coverType)
    {
        var bleedTotal = 2 * _specs.BleedIn;

        return coverType switch
        {
            CoverType.EbookFront => (
                Math.Round(trimW, 4),
                Math.Round(trimH, 4)),

            CoverType.PaperbackWrap => (
                Math.Round(2 * trimW + spineW + bleedTotal, 4),
                Math.Round(trimH + bleedTotal, 4)),

            CoverType.HardcoverWrap => (
                Math.Round(2 * trimW + spineW + 2 * _specs.Hardcover.WrapAllowancePerEdgeIn, 4),
                Math.Round(trimH + 2 * _specs.Hardcover.WrapAllowancePerEdgeIn, 4)),

            _ => (
                Math.Round(2 * trimW + spineW + bleedTotal, 4),
                Math.Round(trimH + bleedTotal, 4))
        };
    }

    /// <summary>
    /// With bleed: width grows by BleedIn on the OUTSIDE edge only (never gutter);
    /// height grows by 2 × BleedIn (top + bottom).
    /// </summary>
    public (double WidthIn, double HeightIn) GetDocumentSizeWithBleedIn(
        double trimW,
        double trimH,
        BleedMode bleedMode)
    {
        if (bleedMode == BleedMode.None)
            return (Math.Round(trimW, 4), Math.Round(trimH, 4));

        var b = _specs.BleedIn;
        return (Math.Round(trimW + b, 4), Math.Round(trimH + 2 * b, 4));
    }

    /// <summary>True when spine is wide enough for spine text (width &gt;= SpineTextMinWidthIn).</summary>
    public bool IsSpineTextAdvisable(double spineWidthIn) =>
        spineWidthIn >= _specs.SpineTextMinWidthIn;

    public ImageDpiCheckResult CheckImageDpi(
        int pixelWidth,
        int pixelHeight,
        double placedWidthIn,
        double placedHeightIn)
    {
        if (placedWidthIn <= 0 || placedHeightIn <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
            return new ImageDpiCheckResult(0, DpiSeverity.Poor);

        var dpiX = pixelWidth / placedWidthIn;
        var dpiY = pixelHeight / placedHeightIn;
        var effective = Math.Min(dpiX, dpiY);
        var rounded = Math.Round(effective, 2);

        var severity = rounded >= _specs.ImageDpi.OkMinDpi
            ? DpiSeverity.Ok
            : rounded >= _specs.ImageDpi.WarningMinDpi
                ? DpiSeverity.Warning
                : DpiSeverity.Poor;

        return new ImageDpiCheckResult(rounded, severity);
    }

    private double ResolveInsideMarginIn(int pageCount)
    {
        foreach (var tier in _specs.InteriorMargins.GutterTiers.OrderBy(t => t.MaxPageCount))
        {
            if (pageCount <= tier.MaxPageCount)
                return tier.InsideIn;
        }

        return _specs.InteriorMargins.FallbackInsideIn;
    }

    private (int Min, int Max) ResolvePaperbackLimits(PaperType paper)
    {
        var pb = _specs.Paperback;
        if (pb.MaxPagesByPaper.TryGetValue(paper.ToString(), out var max))
            return (pb.MinPages, max);
        return (pb.MinPages, pb.MaxPages);
    }

    private static MarginSet RoundMargins(double top, double bottom, double outside, double inside) =>
        new(Math.Round(top, 4), Math.Round(bottom, 4), Math.Round(outside, 4), Math.Round(inside, 4));
}
