// IMPORTANT: Re-verify every numeric value in this file (and the matching appsettings.json
// "KdpSpecs" section) against the current Amazon KDP Cover Calculator and print guidelines
// before shipping. Amazon changes these values periodically.

namespace EBookDashboard.Configuration;

/// <summary>
/// Strongly-typed Amazon KDP specification constants bound from the "KdpSpecs" configuration section.
/// All binding-specific numbers are editable via appsettings without a rebuild.
/// </summary>
public sealed class KdpSpecs
{
    public const string SectionName = "KdpSpecs";

    /// <summary>Print resolution used for pixel conversions (px = inches × Dpi).</summary>
    public int Dpi { get; set; } = 300;

    /// <summary>Bleed extension on each applicable edge (inches). Default 0.125.</summary>
    public double BleedIn { get; set; } = 0.125;

    /// <summary>Keep text / important content this far inside the trim line (inches).</summary>
    public double SafeFromTrimIn { get; set; } = 0.25;

    /// <summary>Spine text only advisable when spine width is at least this many inches.</summary>
    public double SpineTextMinWidthIn { get; set; } = 0.0625;

    /// <summary>Barcode reserve width on the back cover (inches).</summary>
    public double BarcodeWidthIn { get; set; } = 2.0;

    /// <summary>Barcode reserve height on the back cover (inches).</summary>
    public double BarcodeHeightIn { get; set; } = 1.2;

    /// <summary>Minimum clearance of the barcode zone from trim edges (inches).</summary>
    public double BarcodeMarginFromTrimIn { get; set; } = 0.25;

    /// <summary>How many manuscript versions to keep per project (oldest pruned).</summary>
    public int ManuscriptVersionRetainCount { get; set; } = 3;

    /// <summary>Max upload size in megabytes.</summary>
    public int MaxUploadSizeMb { get; set; } = 50;

    /// <summary>When false, TXT/RTF are rejected at upload.</summary>
    public bool AllowLegacyTextFormats { get; set; } = false;

    public PaperThicknessOptions PaperThicknessInPerPage { get; set; } = new();

    public InteriorMarginOptions InteriorMargins { get; set; } = new();

    public BindingPageLimitOptions Ebook { get; set; } = new()
    {
        MinPages = 0,
        MaxPages = int.MaxValue
    };

    public PaperbackOptions Paperback { get; set; } = new();

    public HardcoverOptions Hardcover { get; set; } = new();

    public ImageDpiThresholdOptions ImageDpi { get; set; } = new();

    public EbookCoverOptions EbookCover { get; set; } = new();

    /// <summary>
    /// Alternate wrap diagram used by the legacy print-wrap compositor
    /// (back + hinge + spine + hinge + front). Prefer Section 2 hardcover wrap when unsure.
    /// </summary>
    public AlternateCaseLayoutOptions AlternateCaseLayout { get; set; } = new();

    public List<TrimSizeOption> TrimPresets { get; set; } = CreateDefaultTrimPresets();

    /// <summary>Resolves paper thickness (inches/page). Hardcover spine uses paper thickness too (see calculator).</summary>
    public double ResolveThicknessInPerPage(Models.PaperType paper)
    {
        return paper switch
        {
            Models.PaperType.Cream => PaperThicknessInPerPage.Cream,
            Models.PaperType.PremiumColor => PaperThicknessInPerPage.PremiumColor,
            Models.PaperType.StandardColor => PaperThicknessInPerPage.StandardColor,
            _ => PaperThicknessInPerPage.White
        };
    }

    private static List<TrimSizeOption> CreateDefaultTrimPresets() =>
    [
        new() { Key = "5x8", Label = "5 × 8 in", WidthIn = 5.0, HeightIn = 8.0, Group = "Standard" },
        new() { Key = "5.06x7.81", Label = "5.06 × 7.81 in", WidthIn = 5.06, HeightIn = 7.81, Group = "Standard" },
        new() { Key = "5.25x8", Label = "5.25 × 8 in", WidthIn = 5.25, HeightIn = 8.0, Group = "Standard" },
        new() { Key = "5.5x8.5", Label = "5.5 × 8.5 in", WidthIn = 5.5, HeightIn = 8.5, Group = "Standard" },
        new() { Key = "6x9", Label = "6 × 9 in (default)", WidthIn = 6.0, HeightIn = 9.0, Group = "Standard", IsDefault = true },
        new() { Key = "6.14x9.21", Label = "6.14 × 9.21 in", WidthIn = 6.14, HeightIn = 9.21, Group = "Standard" },
        new() { Key = "6.69x9.61", Label = "6.69 × 9.61 in", WidthIn = 6.69, HeightIn = 9.61, Group = "Standard" },
        new() { Key = "7x10", Label = "7 × 10 in", WidthIn = 7.0, HeightIn = 10.0, Group = "Standard" },
        new() { Key = "7.44x9.69", Label = "7.44 × 9.69 in", WidthIn = 7.44, HeightIn = 9.69, Group = "Standard" },
        new() { Key = "7.5x9.25", Label = "7.5 × 9.25 in", WidthIn = 7.5, HeightIn = 9.25, Group = "Childrens" },
        new() { Key = "8x10", Label = "8 × 10 in", WidthIn = 8.0, HeightIn = 10.0, Group = "Childrens" },
        new() { Key = "8.25x6", Label = "8.25 × 6 in", WidthIn = 8.25, HeightIn = 6.0, Group = "Standard" },
        new() { Key = "8.25x8.25", Label = "8.25 × 8.25 in", WidthIn = 8.25, HeightIn = 8.25, Group = "Childrens" },
        new() { Key = "8.5x8.5", Label = "8.5 × 8.5 in", WidthIn = 8.5, HeightIn = 8.5, Group = "Childrens" },
        new() { Key = "8.5x11", Label = "8.5 × 11 in", WidthIn = 8.5, HeightIn = 11.0, Group = "Standard" },
        new() { Key = "8.27x11.69", Label = "8.27 × 11.69 in (A4)", WidthIn = 8.27, HeightIn = 11.69, Group = "Standard" },
        // Extensibility proof: one extra trim added via config only (see appsettings).
        new() { Key = "5.83x8.27", Label = "5.83 × 8.27 in (A5)", WidthIn = 5.83, HeightIn = 8.27, Group = "Standard" }
    ];
}

public sealed class PaperThicknessOptions
{
    public double White { get; set; } = 0.002252;
    public double Cream { get; set; } = 0.0025;
    public double PremiumColor { get; set; } = 0.002347;
    public double StandardColor { get; set; } = 0.002252;
}

public sealed class InteriorMarginOptions
{
    /// <summary>Recommended outside/top/bottom when bleed is off (inches).</summary>
    public double RecommendedOuterNoBleedIn { get; set; } = 0.25;

    /// <summary>Recommended outside/top/bottom when bleed is on (inches).</summary>
    public double RecommendedOuterWithBleedIn { get; set; } = 0.375;

    public List<GutterTierOption> GutterTiers { get; set; } =
    [
        new() { MaxPageCount = 150, InsideIn = 0.375 },
        new() { MaxPageCount = 300, InsideIn = 0.5 },
        new() { MaxPageCount = 500, InsideIn = 0.625 },
        new() { MaxPageCount = 700, InsideIn = 0.75 },
        new() { MaxPageCount = 828, InsideIn = 0.875 }
    ];

    public double FallbackInsideIn { get; set; } = 0.875;
}

public sealed class GutterTierOption
{
    public int MaxPageCount { get; set; }
    public double InsideIn { get; set; }
}

public class BindingPageLimitOptions
{
    public int MinPages { get; set; }
    public int MaxPages { get; set; }
}

public sealed class PaperbackOptions : BindingPageLimitOptions
{
    public PaperbackOptions()
    {
        MinPages = 24;
        MaxPages = 828;
    }

    public Dictionary<string, int> MaxPagesByPaper { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["White"] = 828,
        ["Cream"] = 828,
        ["StandardColor"] = 828,
        ["PremiumColor"] = 828
    };
}

public sealed class HardcoverOptions : BindingPageLimitOptions
{
    public HardcoverOptions()
    {
        MinPages = 75;
        MaxPages = 550;
    }

    /// <summary>Case-wrap constant added to hardcover spine: (pages/2)×thickness + this.</summary>
    public double SpineCaseExtraIn { get; set; } = 0.06;

    /// <summary>Wrap/hinge allowance per outer edge (inches). Default 0.5.</summary>
    public double WrapAllowancePerEdgeIn { get; set; } = 0.5;

    public List<string> SupportedPaper { get; set; } = ["White", "Cream", "StandardColor", "PremiumColor"];
}

public sealed class ImageDpiThresholdOptions
{
    public double OkMinDpi { get; set; } = 300;
    public double WarningMinDpi { get; set; } = 200;
}

public sealed class EbookCoverOptions
{
    public int RecommendedWidthPx { get; set; } = 1600;
    public int RecommendedHeightPx { get; set; } = 2560;
    public double AspectRatio => RecommendedHeightPx == 0 ? 1.6 : (double)RecommendedHeightPx / RecommendedWidthPx;
}

/// <summary>Legacy case-bound diagram margins (editable; defaults align with Section 2 wrap allowance).</summary>
public sealed class AlternateCaseLayoutOptions
{
    public double MinSpineInches { get; set; } = 0.055;
    public double WrapMarginInches { get; set; } = 0.5;
    public double HingeGapInches { get; set; } = 0.0;
}

public sealed class TrimSizeOption
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public double WidthIn { get; set; }
    public double HeightIn { get; set; }
    /// <summary>Standard | Childrens | Custom</summary>
    public string Group { get; set; } = "Standard";
    public bool IsDefault { get; set; }
}

public static class KdpSpecsAccessor
{
    private static KdpSpecs _current = new();

    public static KdpSpecs Current
    {
        get => _current;
        set => _current = value ?? throw new ArgumentNullException(nameof(value));
    }
}
