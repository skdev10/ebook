using EBookDashboard.Models;

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

    private static readonly Dictionary<BindingType, BindingSpec> Specs = new()
    {
        [BindingType.Paperback] = new BindingSpec
        {
            ExtraWidth = KdpConstants.Bleed * 2,
            ExtraHeight = KdpConstants.Bleed * 2,
            MinPages = 24,
            MaxPages = 830,
        },
        [BindingType.Hardcover] = new BindingSpec
        {
            ExtraWidth = KdpConstants.HardcoverWrap * 2 + KdpConstants.HardcoverHinge,
            ExtraHeight = KdpConstants.HardcoverWrap * 2 + KdpConstants.HardcoverHeightExtra,
            ForcedThickness = KdpConstants.HardcoverThickness,
            UnsupportedPaper = new() { PaperType.Cream, PaperType.StandardColor },
            MinPages = 75,
            MaxPages = 550,
        },
    };

    public CoverResult Calculate(CoverRequest req)
    {
        var spec = Specs[req.Binding];
        var result = new CoverResult();

        double trimW = req.Units == Unit.Millimeters ? req.TrimWidth * MmToIn : req.TrimWidth;
        double trimH = req.Units == Unit.Millimeters ? req.TrimHeight * MmToIn : req.TrimHeight;

        double thickness = spec.ForcedThickness ?? KdpConstants.PaperThickness[req.Paper];
        double spine = req.PageCount * thickness + spec.SpineAllowance;
        result.SpineWidthInches = Math.Round(spine, 4);

        result.FullCoverWidthInches = Math.Round(trimW * 2 + spine + spec.ExtraWidth, 4);
        result.FullCoverHeightInches = Math.Round(trimH + spec.ExtraHeight, 4);

        result.FullCoverWidthPx = (int)Math.Round(result.FullCoverWidthInches * KdpConstants.Dpi);
        result.FullCoverHeightPx = (int)Math.Round(result.FullCoverHeightInches * KdpConstants.Dpi);

        result.SpineTextAllowed = req.PageCount >= KdpConstants.SpineTextMinPages;
        if (!result.SpineTextAllowed)
            result.Warnings.Add($"Spine text needs {KdpConstants.SpineTextMinPages}+ pages.");
        if (req.PageCount < spec.MinPages)
            result.Warnings.Add($"{req.Binding} minimum is {spec.MinPages} pages.");
        if (req.PageCount > spec.MaxPages)
            result.Warnings.Add($"{req.Binding} maximum is {spec.MaxPages} pages.");
        if (spec.UnsupportedPaper.Contains(req.Paper))
            result.Warnings.Add($"{req.Binding} does not support {req.Paper} paper.");

        return result;
    }
}
