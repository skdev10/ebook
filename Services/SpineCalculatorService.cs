using EBookDashboard.Configuration;
using EBookDashboard.Models;

namespace EBookDashboard.Services;

/// <summary>
/// KDP spine calculator matching Amazon's official cover templates.
/// Values come from <see cref="KdpSpecsAccessor"/>.
/// </summary>
public class SpineCalculatorService : ISpineCalculatorService
{
    public CoverDimensionsResult Calculate(int pages, string paper)
    {
        var specs = KdpSpecsAccessor.Current;
        var calc = new KdpCalculationService(specs);
        var min = specs.Paperback.MinPages;
        var max = specs.Paperback.MaxPages;
        pages = Math.Clamp(pages, min, max);

        var paperType = paper?.ToLowerInvariant() switch
        {
            "cream paper" or "cream" => PaperType.Cream,
            "color paper" or "premium color" or "premiumcolor" => PaperType.PremiumColor,
            "standard color" or "standardcolor" => PaperType.StandardColor,
            _ => PaperType.White
        };

        var defaultTrim = specs.TrimPresets.FirstOrDefault(t => t.IsDefault)
                          ?? specs.TrimPresets.FirstOrDefault(t => t.Key == "6x9")
                          ?? new TrimSizeOption { WidthIn = 6, HeightIn = 9 };

        var spine = calc.CalculateSpineWidthIn(pages, paperType, ProjectType.Paperback);
        var (totalWidth, totalHeight) = calc.CalculateFullCoverSizeIn(
            defaultTrim.WidthIn, defaultTrim.HeightIn, spine, CoverType.PaperbackWrap);

        return new CoverDimensionsResult
        {
            SpineInches = Math.Round(spine, 3),
            SpineMm = Math.Round(spine * 25.4, 2),
            TotalWidthInches = Math.Round(totalWidth, 3),
            TotalWidthMm = Math.Round(totalWidth * 25.4, 2),
            TotalHeightInches = Math.Round(totalHeight, 3),
            TotalHeightMm = Math.Round(totalHeight * 25.4, 2),
            BleedInches = specs.BleedIn,
            Pages = pages,
            PaperType = paper ?? "White paper",
            Warning = calc.IsSpineTextAdvisable(spine)
                ? null
                : "Spine too narrow for text — use image only"
        };
    }
}
