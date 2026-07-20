using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EBookDashboard.Services;

/// <summary>Crops individual panels from a KDP full-wrap cover image.</summary>
public static class CoverWrapPanelExtractor
{
    /// <summary>True when width is clearly wider than a single front panel (back + spine + front).</summary>
    public static bool IsLikelyWrapImage(int width, int height)
        => width > 0 && height > 0 && width > height * 1.02;

    /// <summary>Crops the front panel from wrap bytes using KDP layout inches.</summary>
    public static byte[] ExtractFrontPanel(byte[] wrapBytes, KdpPrintCoverCalculator.CoverLayoutSpec layout)
    {
        using var image = Image.Load<Rgba32>(wrapBytes);
        using var cropped = CropFrontPanel(image, layout);
        using var ms = new MemoryStream();
        cropped.Save(ms, PngFormat.Instance);
        return ms.ToArray();
    }

    /// <summary>
    /// Crops the bleed-inclusive front panel (right-hand panel through right edge, full height).
    /// Uses the same dimension service as <see cref="PrintWrapCompositor"/>.
    /// </summary>
    public static Image<Rgba32> CropFrontPanel(Image<Rgba32> wrapImage, KdpPrintCoverCalculator.CoverLayoutSpec layout)
    {
        var pages = Math.Clamp(
            layout.PageCount > 0 ? layout.PageCount : KdpPaperbackConstants.MinPageCount,
            KdpPaperbackConstants.MinPageCount,
            KdpPaperbackConstants.MaxPageCount);
        return CropFrontPanelBleedInclusive(
            wrapImage,
            pages,
            (decimal)layout.TrimWidthInches,
            (decimal)layout.TrimHeightInches);
    }

    /// <summary>Crops the front panel region using <see cref="KdpCoverDimensionService"/>.</summary>
    public static Image<Rgba32> CropFrontPanelBleedInclusive(
        Image<Rgba32> wrapImage,
        int pageCount,
        decimal trimWidth,
        decimal trimHeight)
    {
        var kdp = new KdpCoverDimensionService().Calculate(new KdpCalculateRequest
        {
            PageCount = pageCount,
            TrimWidth = trimWidth,
            TrimHeight = trimHeight,
            Dpi = KdpConstants.Dpi,
            Bleed = true,
            InteriorType = KdpPaperbackConstants.InteriorTypeStandardColor,
            PaperType = KdpPaperbackConstants.PaperTypeWhite
        });

        var layoutW = (double)kdp.FullCoverWidth;
        var layoutH = (double)kdp.FullCoverHeight;
        if (layoutW <= 0 || layoutH <= 0)
            throw new InvalidOperationException("Invalid KDP wrap layout for front-panel crop.");

        // Match compositor: front art from FrontPanelX → right edge, Y = 0 → full height.
        var xPx = (int)Math.Round(((double)kdp.FrontPanelXInches / layoutW) * wrapImage.Width);
        var yPx = 0;
        var wPx = Math.Max(1, wrapImage.Width - xPx);
        var hPx = wrapImage.Height;

        xPx = Math.Clamp(xPx, 0, Math.Max(0, wrapImage.Width - 1));
        wPx = Math.Min(wPx, wrapImage.Width - xPx);
        hPx = Math.Min(hPx, wrapImage.Height - yPx);

        return wrapImage.Clone(c => c.Crop(new Rectangle(xPx, yPx, wPx, hPx)));
    }

    /// <summary>
    /// Returns front-only bytes when the source is a full wrap; otherwise returns the original bytes.
    /// When <paramref name="assumeWrap"/> is true the source is known to be the full wrap
    /// (e.g. the front download fell back to the wrap), so the front panel is always cropped
    /// even if the aspect-ratio heuristic is borderline — this prevents the "front button returns
    /// the full cover" glitch when the generated wrap is not clearly landscape.
    /// </summary>
    public static byte[] EnsureFrontPanelBytes(byte[] sourceBytes, int pageCount, string? trimSize, bool assumeWrap = false)
    {
        if (sourceBytes.Length == 0) return sourceBytes;
        try
        {
            using var probe = Image.Load<Rgba32>(sourceBytes);
            if (!assumeWrap && !IsLikelyWrapImage(probe.Width, probe.Height))
                return sourceBytes;

            var pages = Math.Clamp(
                pageCount > 0 ? pageCount : KdpPaperbackConstants.MinPageCount,
                KdpPaperbackConstants.MinPageCount,
                KdpPaperbackConstants.MaxPageCount);
            var trim = ParseTrimInches(trimSize);
            using var cropped = CropFrontPanelBleedInclusive(probe, pages, trim.W, trim.H);
            using var ms = new MemoryStream();
            cropped.Save(ms, PngFormat.Instance);
            return ms.ToArray();
        }
        catch
        {
            return sourceBytes;
        }
    }

    /// <summary>
    /// Returns true when the front panel cropped from a wrap matches the saved front cover image.
    /// Comparison is tolerance-based on a downscaled copy: upstream APIs re-encode/rescale the
    /// same artwork (small per-pixel drift), which must PASS, while genuinely different art
    /// (different composition/colors) must FAIL. Exact pixel equality rejected every legitimate
    /// upstream wrap and forced the plain local fallback on production.
    /// </summary>
    public static bool FrontPanelMatchesSavedFront(byte[] wrapBytes, byte[] frontBytes, int pageCount, string? trimSize)
    {
        if (wrapBytes.Length == 0 || frontBytes.Length == 0) return false;
        try
        {
            var extracted = EnsureFrontPanelBytes(wrapBytes, pageCount, trimSize, assumeWrap: true);
            const int compareW = 96;
            const int compareH = 144;
            using var ext = Image.Load<Rgba32>(extracted);
            using var front = Image.Load<Rgba32>(frontBytes);
            using var a = ext.Clone(c => c.Resize(compareW, compareH, KnownResamplers.Lanczos3));
            using var b = front.Clone(c => c.Resize(compareW, compareH, KnownResamplers.Lanczos3));

            long totalDiff = 0;
            var bigDiffPixels = 0;
            for (var y = 0; y < compareH; y++)
            {
                for (var x = 0; x < compareW; x++)
                {
                    var pa = a[x, y];
                    var pb = b[x, y];
                    var d = Math.Abs(pa.R - pb.R) + Math.Abs(pa.G - pb.G) + Math.Abs(pa.B - pb.B);
                    totalDiff += d;
                    if (d > 96) bigDiffPixels++;
                }
            }

            var totalPixels = compareW * compareH;
            var meanDiff = (double)totalDiff / totalPixels;          // 0..765
            var bigDiffRatio = (double)bigDiffPixels / totalPixels;   // fraction of clearly-different pixels

            // Same art re-encoded: meanDiff ~ 2-20, bigDiffRatio ~ 0.
            // Different art: meanDiff typically 80+, bigDiffRatio 0.3+.
            return meanDiff <= 40 && bigDiffRatio <= 0.10;
        }
        catch
        {
            return false;
        }
    }

    private static (decimal W, decimal H) ParseTrimInches(string? trimSize)
    {
        var src = (trimSize ?? "").Trim().ToLowerInvariant().Replace(" ", "");
        if (src.Contains("5.5") && src.Contains("8.5")) return (5.5m, 8.5m);
        if (src.Contains("8.5") && src.Contains("11")) return (8.5m, 11m);
        return (6m, 9m);
    }
}
