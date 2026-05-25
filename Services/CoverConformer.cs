using EBookDashboard.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EBookDashboard.Services;

public class CoverConformer
{
    private readonly CoverCalculator _calc;
    public CoverConformer(CoverCalculator calc) => _calc = calc;

    /// <summary>
    /// Fit a complete wraparound cover to KDP's exact full-cover canvas.
    /// Crop mode = fill the canvas without distorting the artwork, trimming
    /// only a thin sliver of the outer edge (which is bleed anyway) when the
    /// AI image's aspect ratio differs from KDP's. The spine stays centred.
    /// </summary>
    public (Image<Rgba32> image, CoverResult dims) Conform(CoverRequest req, Stream wraparoundPng)
    {
        var dims = _calc.Calculate(req);

        var image = Image.Load<Rgba32>(wraparoundPng);

        image.Mutate(c => c.Resize(new ResizeOptions
        {
            Size = new Size(dims.FullCoverWidthPx, dims.FullCoverHeightPx),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center,
            Sampler = KnownResamplers.Lanczos3
        }));

        image.Metadata.HorizontalResolution = KdpConstants.Dpi;
        image.Metadata.VerticalResolution = KdpConstants.Dpi;
        image.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;

        return (image, dims);
    }
}
