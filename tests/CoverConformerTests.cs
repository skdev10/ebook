using EBookDashboard.Models;
using EBookDashboard.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace EBookDashboard.Tests;

public class CoverConformerTests
{
    [Fact]
    public void Conform_ResizesToKdpPixelDimensions()
    {
        var calc = new CoverCalculator();
        var conformer = new CoverConformer(calc);
        var req = new CoverRequest
        {
            Binding = BindingType.Paperback,
            Paper = PaperType.White,
            TrimWidth = 6,
            TrimHeight = 9,
            PageCount = 200,
            Units = Unit.Inches
        };
        var dims = calc.Calculate(req);

        using var source = new Image<Rgba32>(1536, 1024);
        using var ms = new MemoryStream();
        source.SaveAsPng(ms);
        ms.Position = 0;

        var (image, result) = conformer.Conform(req, ms);
        using (image)
        {
            Assert.Equal(dims.FullCoverWidthPx, image.Width);
            Assert.Equal(dims.FullCoverHeightPx, image.Height);
            Assert.Equal(300, (int)image.Metadata.HorizontalResolution);
        }

        Assert.Equal(0.4504, result.SpineWidthInches, 4);
    }
}
