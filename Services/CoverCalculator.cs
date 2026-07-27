using EBookDashboard.Configuration;
using EBookDashboard.Models;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services;

public record BindingSpec
{
    public double SpineAllowance { get; init; }
    public double ExtraWidth { get; init; }
    public double ExtraHeight { get; init; }
    public double? ForcedThickness { get; init; }
    public HashSet<PaperType> UnsupportedPaper { get; init; } = new();
    public int MinPages { get; init; }
    public int MaxPages { get; init; }
}

public class CoverCalculator
{
    private const double MmToIn = 1.0 / 25.4;
    private readonly KdpSpecs _specs;
    private readonly KdpCalculationService _calc;

    public CoverCalculator()
        : this(KdpSpecsAccessor.Current)
    {
    }

    public CoverCalculator(IOptions<KdpSpecs> options)
        : this(options.Value)
    {
    }

    public CoverCalculator(KdpSpecs specs)
    {
        _specs = specs ?? throw new ArgumentNullException(nameof(specs));
        _calc = new KdpCalculationService(_specs);
    }

    public CoverResult Calculate(CoverRequest req)
    {
        var result = new CoverResult();

        double trimW = req.Units == Unit.Millimeters ? req.TrimWidth * MmToIn : req.TrimWidth;
        double trimH = req.Units == Unit.Millimeters ? req.TrimHeight * MmToIn : req.TrimHeight;

        var projectType = req.Binding == BindingType.Hardcover ? ProjectType.Hardcover : ProjectType.Paperback;
        result.SpineWidthInches = _calc.CalculateSpineWidthIn(req.PageCount, req.Paper, projectType);

        var coverType = req.Binding == BindingType.Hardcover ? CoverType.HardcoverWrap : CoverType.PaperbackWrap;
        var (fullW, fullH) = _calc.CalculateFullCoverSizeIn(trimW, trimH, result.SpineWidthInches, coverType);
        result.FullCoverWidthInches = fullW;
        result.FullCoverHeightInches = fullH;

        result.FullCoverWidthPx = (int)Math.Round(result.FullCoverWidthInches * _specs.Dpi);
        result.FullCoverHeightPx = (int)Math.Round(result.FullCoverHeightInches * _specs.Dpi);

        result.SpineTextAllowed = _calc.IsSpineTextAdvisable(result.SpineWidthInches);
        if (!result.SpineTextAllowed)
            result.Warnings.Add($"Spine text needs spine width ≥ {_specs.SpineTextMinWidthIn} in.");

        var pageWarning = _calc.ValidatePageCount(req.PageCount, projectType, req.Paper);
        if (pageWarning != null)
            result.Warnings.Add(pageWarning);

        return result;
    }
}
