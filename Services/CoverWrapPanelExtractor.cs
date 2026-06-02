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

    /// <summary>Crops the front panel region from a loaded wrap image.</summary>
    public static Image<Rgba32> CropFrontPanel(Image<Rgba32> wrapImage, KdpPrintCoverCalculator.CoverLayoutSpec layout)
    {
        var layoutW = layout.WrapWidthInches;
        var layoutH = layout.WrapHeightInches;
        var frontX = layout.FrontPanelXInches;
        var y = layout.PanelTopYInches;
        var pw = layout.TrimWidthInches;
        var ph = layout.TrimHeightInches;

        var xPx = (int)Math.Round((frontX / layoutW) * wrapImage.Width);
        var yPx = (int)Math.Round((y / layoutH) * wrapImage.Height);
        var wPx = Math.Max(1, (int)Math.Round((pw / layoutW) * wrapImage.Width));
        var hPx = Math.Max(1, (int)Math.Round((ph / layoutH) * wrapImage.Height));

        xPx = Math.Clamp(xPx, 0, Math.Max(0, wrapImage.Width - 1));
        yPx = Math.Clamp(yPx, 0, Math.Max(0, wrapImage.Height - 1));
        wPx = Math.Min(wPx, wrapImage.Width - xPx);
        hPx = Math.Min(hPx, wrapImage.Height - yPx);

        return wrapImage.Clone(c => c.Crop(new Rectangle(xPx, yPx, wPx, hPx)));
    }

    /// <summary>Returns front-only bytes when the source is a wide wrap; otherwise returns the original bytes.</summary>
    public static byte[] EnsureFrontPanelBytes(byte[] sourceBytes, int pageCount, string? trimSize)
    {
        if (sourceBytes.Length == 0) return sourceBytes;
        try
        {
            using var probe = Image.Load<Rgba32>(sourceBytes);
            if (!IsLikelyWrapImage(probe.Width, probe.Height))
                return sourceBytes;

            var pages = Math.Clamp(pageCount > 0 ? pageCount : KdpPrintCoverCalculator.MinPages,
                KdpPrintCoverCalculator.MinPages,
                KdpPrintCoverCalculator.MaxPagesPaperback);
            var layout = KdpPrintCoverCalculator.Calculate(pages, trimSize);
            return ExtractFrontPanel(sourceBytes, layout);
        }
        catch
        {
            return sourceBytes;
        }
    }
}
