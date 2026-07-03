using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Interfaces;
using EBookDashboard.Application.Kdp.Services;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace EBookDashboard.Tests;

public class PrintWrapCompositorTests
{
    private static KdpCalculateResponse LayoutForPages(int pageCount) =>
        new KdpCoverDimensionService().Calculate(new KdpCalculateRequest
        {
            PageCount = pageCount,
            TrimWidth = 6m,
            TrimHeight = 9m,
            Dpi = KdpConstants.Dpi,
            Bleed = true,
            InteriorType = KdpPaperbackConstants.InteriorTypeStandardColor,
            PaperType = KdpPaperbackConstants.PaperTypeWhite
        });

    [Fact]
    public void Compose_front_panel_matches_independent_cover_fill_pixels()
    {
        using var front = new Image<Rgba32>(800, 1200);
        front.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.FromRgb(120, 60, 30)));

        using var ms = new MemoryStream();
        front.SaveAsPng(ms);
        var frontBytes = ms.ToArray();

        var layout = LayoutForPages(40);
        var compositor = new PrintWrapCompositor(new SpineRenderer());
        var result = compositor.Compose(new PrintWrapComposeRequest
        {
            FrontCoverBytes = frontBytes,
            FrontCoverAssetRef = "/uploads/test/front.png",
            Layout = layout,
            Title = "Sharukh Khan",
            Author = "saad",
            Description = "A biography exploring cinema, identity, and the arc of a global icon.",
            Theme = BookTheme.FromExportOptions(new BookPdfExportOptions { InteriorStyle = "Classic" })
        });

        using var wrap = Image.Load<Rgba32>(result.PngBytes);
        using var extracted = PrintWrapCompositor.ExtractFrontPanel(wrap, result);
        using var expected = PrintWrapCompositor.RenderFrontPanelOnly(frontBytes, result, layout.Dpi);

        Assert.Equal(expected.Width, extracted.Width);
        Assert.Equal(expected.Height, extracted.Height);

        var mismatches = 0;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var a = expected[x, y];
                var b = extracted[x, y];
                if (a.R != b.R || a.G != b.G || a.B != b.B || a.A != b.A)
                    mismatches++;
            }
        }

        Assert.Equal(0, mismatches);
    }

    [Fact]
    public void Compose_without_front_asset_ref_throws()
    {
        var compositor = new PrintWrapCompositor(new SpineRenderer());
        Assert.Throws<InvalidOperationException>(() => compositor.Compose(new PrintWrapComposeRequest
        {
            FrontCoverBytes = [1, 2, 3],
            FrontCoverAssetRef = "",
            Layout = LayoutForPages(40),
            Title = "T",
            Description = "Back blurb text."
        }));
    }

    [Fact]
    public void Barcode_zone_constants_match_kdp_spec()
    {
        Assert.Equal(2.0m, KdpPaperbackConstants.BarcodeZoneWidthInches);
        Assert.Equal(1.2m, KdpPaperbackConstants.BarcodeZoneHeightInches);
        Assert.Equal(0.25m, KdpPaperbackConstants.BarcodeMarginInches);
    }

    [Fact]
    public void ResolveBackCoverDescription_uses_saved_description_when_present()
    {
        var book = new Books { Title = "My Book", Description = "  Saved blurb.  " };
        var desc = PrintWrapGenerationService.ResolveBackCoverDescription(book, null);
        Assert.Equal("Saved blurb.", desc);
    }

    [Fact]
    public void ResolveBackCoverDescription_falls_back_without_blocking()
    {
        var book = new Books { Title = "Great Dictator", Genre = "Fantasy" };
        var desc = PrintWrapGenerationService.ResolveBackCoverDescription(book, null);
        Assert.Contains("Great Dictator", desc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fantasy", desc, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] PngBytes(Image<Rgba32> img)
    {
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public void FrontPanelMatchesSavedFront_accepts_reencoded_same_art()
    {
        // Gradient front so re-scaling produces small per-pixel drift (like upstream re-encoding).
        using var front = new Image<Rgba32>(400, 600);
        for (var y = 0; y < 600; y++)
            for (var x = 0; x < 400; x++)
                front[x, y] = new Rgba32((byte)(x * 255 / 400), (byte)(y * 255 / 600), 128);
        var frontBytes = PngBytes(front);

        var layout = LayoutForPages(40);
        var compositor = new PrintWrapCompositor(new SpineRenderer());
        var result = compositor.Compose(new PrintWrapComposeRequest
        {
            FrontCoverBytes = frontBytes,
            FrontCoverAssetRef = "/uploads/test/front.png",
            Layout = layout,
            Title = "T",
            Author = "A",
            Description = "Blurb."
        });

        Assert.True(CoverWrapPanelExtractor.FrontPanelMatchesSavedFront(
            result.PngBytes, frontBytes, 40, "6x9"));
    }

    [Fact]
    public void FrontPanelMatchesSavedFront_rejects_different_art()
    {
        using var front = new Image<Rgba32>(400, 600);
        front.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.FromRgb(200, 40, 40)));
        var frontBytes = PngBytes(front);

        using var otherFront = new Image<Rgba32>(400, 600);
        otherFront.Mutate(ctx => ctx.BackgroundColor(SixLabors.ImageSharp.Color.FromRgb(30, 90, 200)));
        var otherBytes = PngBytes(otherFront);

        var layout = LayoutForPages(40);
        var compositor = new PrintWrapCompositor(new SpineRenderer());
        var wrapFromOther = compositor.Compose(new PrintWrapComposeRequest
        {
            FrontCoverBytes = otherBytes,
            FrontCoverAssetRef = "/uploads/test/other.png",
            Layout = layout,
            Title = "T",
            Author = "A",
            Description = "Blurb."
        });

        Assert.False(CoverWrapPanelExtractor.FrontPanelMatchesSavedFront(
            wrapFromOther.PngBytes, frontBytes, 40, "6x9"));
    }
}
