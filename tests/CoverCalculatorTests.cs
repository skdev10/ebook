using EBookDashboard.Configuration;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

public class CoverCalculatorTests
{
    private readonly CoverCalculator _calculator = new();

    [Fact]
    public void Paperback_6x9_200pp_White_ReturnsExpectedDimensions()
    {
        var result = _calculator.Calculate(new CoverRequest
        {
            Binding = BindingType.Paperback,
            Paper = PaperType.White,
            TrimWidth = 6,
            TrimHeight = 9,
            PageCount = 200,
            Units = Unit.Inches
        });

        Assert.Equal(0.4504, result.SpineWidthInches, 4);
        Assert.Equal(12.7004, result.FullCoverWidthInches, 4);
        Assert.Equal(9.25, result.FullCoverHeightInches, 4);
    }

    [Fact]
    public void CreamPaper_WiderSpineThanWhite_SamePageCount()
    {
        var white = _calculator.Calculate(new CoverRequest
        {
            Paper = PaperType.White,
            PageCount = 200,
            TrimWidth = 6,
            TrimHeight = 9
        });

        var cream = _calculator.Calculate(new CoverRequest
        {
            Paper = PaperType.Cream,
            PageCount = 200,
            TrimWidth = 6,
            TrimHeight = 9
        });

        Assert.True(cream.SpineWidthInches > white.SpineWidthInches);
    }

    [Fact]
    public void Hardcover_CaseSpineFormula_AndLargerWrapThanPaperback()
    {
        KdpSpecsAccessor.Current = new Configuration.KdpSpecs();
        var paperback = _calculator.Calculate(new CoverRequest
        {
            Binding = BindingType.Paperback,
            Paper = PaperType.White,
            TrimWidth = 6,
            TrimHeight = 9,
            PageCount = 200
        });

        var hardcover = _calculator.Calculate(new CoverRequest
        {
            Binding = BindingType.Hardcover,
            Paper = PaperType.White,
            TrimWidth = 6,
            TrimHeight = 9,
            PageCount = 200
        });

        // Hardcover forces premium-color thickness (0.002347"/page); spine = pages × thickness.
        var expectedSpine = Math.Round(200 * 0.002347, 4);
        Assert.Equal(expectedSpine, hardcover.SpineWidthInches, 4);
        Assert.True(hardcover.FullCoverWidthInches > paperback.FullCoverWidthInches);
        Assert.True(hardcover.FullCoverHeightInches > paperback.FullCoverHeightInches);
    }

    [Fact]
    public void ThinSpine_SpineTextNotAdvisable_WithWarning()
    {
        KdpSpecsAccessor.Current = new Configuration.KdpSpecs();
        var result = _calculator.Calculate(new CoverRequest
        {
            PageCount = 20,
            TrimWidth = 6,
            TrimHeight = 9,
            Paper = PaperType.White
        });

        Assert.False(result.SpineTextAllowed);
        Assert.Contains(result.Warnings, w => w.Contains("Spine text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Hardcover_AcceptsCreamPaper_NoUnsupportedWarning()
    {
        KdpSpecsAccessor.Current = new Configuration.KdpSpecs();
        var result = _calculator.Calculate(new CoverRequest
        {
            Binding = BindingType.Hardcover,
            Paper = PaperType.Cream,
            PageCount = 200,
            TrimWidth = 6,
            TrimHeight = 9
        });

        Assert.DoesNotContain(result.Warnings, w => w.Contains("does not support", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MillimeterInput_EqualsInchResult()
    {
        var inches = _calculator.Calculate(new CoverRequest
        {
            TrimWidth = 6,
            TrimHeight = 9,
            PageCount = 200,
            Units = Unit.Inches
        });

        var mm = _calculator.Calculate(new CoverRequest
        {
            TrimWidth = 152.4,
            TrimHeight = 228.6,
            PageCount = 200,
            Units = Unit.Millimeters
        });

        Assert.Equal(inches.SpineWidthInches, mm.SpineWidthInches, 4);
        Assert.Equal(inches.FullCoverWidthInches, mm.FullCoverWidthInches, 4);
        Assert.Equal(inches.FullCoverHeightInches, mm.FullCoverHeightInches, 4);
    }

    [Theory]
    [InlineData(6, 9, 200, BindingType.Paperback, PaperType.White)]
    [InlineData(6, 9, 200, BindingType.Hardcover, PaperType.White)]
    public void PixelDimensions_EqualRoundInchesTimes300(
        double trimW, double trimH, int pages, BindingType binding, PaperType paper)
    {
        var result = _calculator.Calculate(new CoverRequest
        {
            Binding = binding,
            Paper = paper,
            TrimWidth = trimW,
            TrimHeight = trimH,
            PageCount = pages
        });

        Assert.Equal((int)Math.Round(result.FullCoverWidthInches * 300), result.FullCoverWidthPx);
        Assert.Equal((int)Math.Round(result.FullCoverHeightInches * 300), result.FullCoverHeightPx);
    }

    [Fact]
    public void LayoutBuilder_PanelWidthsSumToCanvasWidth()
    {
        var calc = new CoverCalculator();
        var layoutBuilder = new LayoutBuilder();
        var dims = calc.Calculate(new CoverRequest
        {
            Binding = BindingType.Paperback,
            TrimWidth = 6,
            TrimHeight = 9,
            PageCount = 200
        });

        var layout = layoutBuilder.Build(dims, 6, KdpConstants.Bleed);

        Assert.Equal(dims.FullCoverWidthPx, layout.Back.Width + layout.Spine.Width + layout.Front.Width);
        Assert.True(layout.BackSafe.X >= layout.Back.X);
        Assert.True(layout.BackSafe.X + layout.BackSafe.Width <= layout.Back.X + layout.Back.Width);
    }
}
