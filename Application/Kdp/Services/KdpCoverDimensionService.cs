using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Interfaces;

namespace EBookDashboard.Application.Kdp.Services;

/// <summary>
/// Amazon KDP paperback cover calculator — matches official Cover Calculator formulas.
/// All inch math uses <see cref="decimal"/> with <see cref="MidpointRounding.AwayFromZero"/>.
/// </summary>
public sealed class KdpCoverDimensionService : IKdpCoverDimensionService
{
    public KdpCalculateResponse Calculate(KdpCalculateRequest request)
    {
        var interiorType = NormalizeInteriorType(request.InteriorType);
        var paperType = NormalizePaperType(request.PaperType);
        var spinePerPage = ResolveSpineInchesPerPage(interiorType, paperType);

        // spineWidth = pageCount × spineInchesPerPage (dynamic for any page count)
        var rawSpine = request.PageCount * spinePerPage;
        var spineWidth = RoundInches(rawSpine);

        var frontCoverWidth = request.TrimWidth;
        var backCoverWidth = request.TrimWidth;

        // bleed = 0.125" per edge when enabled; 0 when disabled
        var bleed = request.Bleed ? KdpPaperbackConstants.BleedInches : 0m;

        // fullCoverWidth = backCoverWidth + frontCoverWidth + spineWidth + (bleed × 2)
        var fullCoverWidth = RoundInches(backCoverWidth + frontCoverWidth + spineWidth + (bleed * 2m));

        // fullCoverHeight = trimHeight + (bleed × 2)
        var fullCoverHeight = RoundInches(request.TrimHeight + (bleed * 2m), heightStyle: true);

        // safeAreaWidth = trimWidth − 0.125
        var safeAreaWidth = RoundInches(request.TrimWidth - KdpPaperbackConstants.SafeAreaWidthReduction);

        // safeAreaHeight = trimHeight − 0.25
        var safeAreaHeight = RoundInches(request.TrimHeight - KdpPaperbackConstants.SafeAreaHeightReduction, heightStyle: true);

        var marginWidth = KdpPaperbackConstants.SafeAreaWidthReduction;
        var marginHeight = KdpPaperbackConstants.SafeAreaHeightReduction;

        // Panel layout (RTL: back | spine | front, left to right on wrap)
        var backPanelX = bleed;
        var spineX = bleed + backCoverWidth;
        var frontPanelX = bleed + backCoverWidth + spineWidth;
        var panelTopY = bleed;

        // pixels = inches × DPI (rounded away from zero)
        var pixelWidth = InchesToPixels(fullCoverWidth, request.Dpi);
        var pixelHeight = InchesToPixels(fullCoverHeight, request.Dpi);
        var spinePixels = InchesToPixels(spineWidth, request.Dpi);

        return new KdpCalculateResponse
        {
            PageCount = request.PageCount,
            FullCoverWidth = fullCoverWidth,
            FullCoverHeight = fullCoverHeight,
            FrontCoverWidth = frontCoverWidth,
            BackCoverWidth = backCoverWidth,
            SpineWidth = spineWidth,
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            SpinePixels = spinePixels,
            SafeAreaWidth = safeAreaWidth,
            SafeAreaHeight = safeAreaHeight,
            Bleed = bleed,
            SpineMargin = KdpPaperbackConstants.SpineMarginInches,
            BarcodeMargin = KdpPaperbackConstants.BarcodeMarginInches,
            MarginWidth = marginWidth,
            MarginHeight = marginHeight,
            BackPanelXInches = backPanelX,
            SpineXInches = spineX,
            FrontPanelXInches = frontPanelX,
            PanelTopYInches = panelTopY,
            BindingType = KdpPaperbackConstants.BindingTypePaperback,
            InteriorType = interiorType,
            PaperType = paperType,
            ReadingDirection = KdpPaperbackConstants.ReadingDirectionRtl,
            MeasurementUnit = KdpPaperbackConstants.MeasurementUnitInches,
            Dpi = request.Dpi,
            BleedEnabled = request.Bleed,
            SpineInchesPerPage = spinePerPage
        };
    }

    /// <summary>
    /// Standard Color + White Paper (default print-ready): 0.002252 in/page per KDP spec.
    /// </summary>
    public static decimal ResolveSpineInchesPerPage(string interiorType, string paperType)
    {
        if (interiorType.Equals("Premium Color", StringComparison.OrdinalIgnoreCase))
            return KdpPaperbackConstants.ColorSpinePerPage;

        if (interiorType.Equals("Standard Color", StringComparison.OrdinalIgnoreCase)
            && paperType.Equals(KdpPaperbackConstants.PaperTypeWhite, StringComparison.OrdinalIgnoreCase))
            return KdpPaperbackConstants.StandardColorWhitePaperSpinePerPage;

        if (interiorType.Equals("Standard Color", StringComparison.OrdinalIgnoreCase))
            return KdpPaperbackConstants.ColorSpinePerPage;

        if (paperType.Contains("Cream", StringComparison.OrdinalIgnoreCase))
            return KdpPaperbackConstants.CreamPaperSpinePerPage;

        return KdpPaperbackConstants.BlackWhiteWhitePaperSpinePerPage;
    }

    internal static string NormalizeInteriorType(string? value)
    {
        var v = (value ?? KdpPaperbackConstants.InteriorTypeStandardColor).Trim();
        if (v.Contains("premium", StringComparison.OrdinalIgnoreCase) && v.Contains("color", StringComparison.OrdinalIgnoreCase))
            return "Premium Color";
        if (v.Contains("color", StringComparison.OrdinalIgnoreCase))
            return KdpPaperbackConstants.InteriorTypeStandardColor;
        if (v.Contains("black", StringComparison.OrdinalIgnoreCase) || v.Contains("white", StringComparison.OrdinalIgnoreCase) && v.Contains("&"))
            return "Black & White";
        return KdpPaperbackConstants.InteriorTypeStandardColor;
    }

    internal static string NormalizePaperType(string? value)
    {
        var v = (value ?? KdpPaperbackConstants.PaperTypeWhite).Trim();
        if (v.Contains("cream", StringComparison.OrdinalIgnoreCase))
            return "Cream Paper";
        return KdpPaperbackConstants.PaperTypeWhite;
    }

    /// <summary>Round inch values like KDP (3 dp; height may display 2 dp when exact).</summary>
    internal static decimal RoundInches(decimal value, bool heightStyle = false)
    {
        var rounded = Math.Round(value, KdpPaperbackConstants.InchDecimalPlaces, MidpointRounding.AwayFromZero);
        if (heightStyle)
            return Math.Round(rounded, 2, MidpointRounding.AwayFromZero);
        return rounded;
    }

    /// <summary>Convert inches to pixels: pixels = inches × DPI.</summary>
    internal static int InchesToPixels(decimal inches, int dpi)
        => (int)Math.Round(inches * dpi, 0, MidpointRounding.AwayFromZero);
}
