using EBookDashboard.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EBookDashboard.Services;

public class ExportResult
{
    public CoverResult Dimensions { get; set; } = default!;
    public CoverLayout Layout { get; set; } = default!;
    public Image<Rgba32> Cover { get; set; } = default!;
    public List<string> Warnings { get; } = new();
}

public class CoverComposer
{
    private readonly CoverCalculator _calc;
    private readonly LayoutBuilder _layout;
    private readonly SpineRenderer _spine;

    public CoverComposer(CoverCalculator calc, LayoutBuilder layout, SpineRenderer spine)
    {
        _calc = calc;
        _layout = layout;
        _spine = spine;
    }

    public ExportResult ComposeForExport(CoverRequest req, Stream backPng, Stream frontPng,
        Color spineColor, string? spineTitle = null, Color? spineTextColor = null)
    {
        var dims = _calc.Calculate(req);
        double trimWIn = req.Units == Unit.Millimeters ? req.TrimWidth / 25.4 : req.TrimWidth;
        double outerMargin = req.Binding == BindingType.Hardcover ? KdpConstants.HardcoverWrap : KdpConstants.Bleed;
        var layout = _layout.Build(dims, trimWIn, outerMargin, req.Direction);

        var canvas = NewCanvas(layout);
        var result = new ExportResult { Dimensions = dims, Layout = layout, Cover = canvas };
        result.Warnings.AddRange(dims.Warnings);

        PlaceInto(canvas, backPng, layout.Back, "back", result.Warnings);
        PlaceInto(canvas, frontPng, layout.Front, "front", result.Warnings);

        using var spineImg = _spine.Render(layout.Spine.Width, layout.Spine.Height, spineColor,
            dims.SpineTextAllowed ? spineTitle : null, spineTextColor);
        canvas.Mutate(c => c.DrawImage(spineImg, new Point(layout.Spine.X, layout.Spine.Y), 1f));

        return result;
    }

    private static Image<Rgba32> NewCanvas(CoverLayout layout)
    {
        var canvas = new Image<Rgba32>(layout.CanvasWidthPx, layout.CanvasHeightPx);
        canvas.Metadata.HorizontalResolution = KdpConstants.Dpi;
        canvas.Metadata.VerticalResolution = KdpConstants.Dpi;
        canvas.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
        return canvas;
    }

    private static void PlaceInto(Image<Rgba32> canvas, Stream png, Rect region, string name, List<string> warnings)
    {
        using var img = Image.Load<Rgba32>(png);

        double imgAspect = (double)img.Width / img.Height;
        double regAspect = (double)region.Width / region.Height;
        if (Math.Abs(imgAspect - regAspect) / regAspect > 0.04)
            warnings.Add($"'{name}' aspect differs from its slot; edges will be cropped — check no text is cut.");
        if (img.Width < region.Width || img.Height < region.Height)
            warnings.Add($"'{name}' is lower resolution than its slot ({region.Width}x{region.Height}); it will be upscaled.");

        img.Mutate(c => c.Resize(new ResizeOptions
        {
            Size = new Size(region.Width, region.Height),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }));
        canvas.Mutate(c => c.DrawImage(img, new Point(region.X, region.Y), 1f));
    }
}
