using EBookDashboard.Models;

namespace EBookDashboard.Services;

/// <summary>
/// KDP spine calculator matching Amazon's official cover templates.
/// Spine = pageCount × paperThickness (no extra allowance per KDP spec).
/// </summary>
public class SpineCalculatorService : ISpineCalculatorService
{
    private const double WhiteThickness = 0.002252;
    private const double CreamThickness = 0.0025;
    private const double PremiumColorThickness = 0.002347;
    private const double TrimWidth = 6.0;
    private const double TrimHeight = 9.0;
    private const double Bleed = 0.125;

    public CoverDimensionsResult Calculate(int pages, string paper)
    {
        pages = Math.Clamp(pages, 24, 828);

        double thickness = paper?.ToLowerInvariant() switch
        {
            "cream paper" or "cream" => CreamThickness,
            "color paper" or "premium color" or "standard color" => PremiumColorThickness,
            _ => WhiteThickness
        };

        double spine = pages * thickness;
        double totalWidth = TrimWidth * 2 + spine + Bleed * 2;
        double totalHeight = TrimHeight + Bleed * 2;

        return new CoverDimensionsResult
        {
            SpineInches = Math.Round(spine, 3),
            SpineMm = Math.Round(spine * 25.4, 2),
            TotalWidthInches = Math.Round(totalWidth, 3),
            TotalWidthMm = Math.Round(totalWidth * 25.4, 2),
            TotalHeightInches = Math.Round(totalHeight, 3),
            TotalHeightMm = Math.Round(totalHeight * 25.4, 2),
            BleedInches = Bleed,
            Pages = pages,
            PaperType = paper ?? "White paper",
            Warning = spine < 0.0625 ? "Spine too narrow for text — use image only" : null
        };
    }
}
