using EBookDashboard.Configuration;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

public class KdpCalculationServiceTests
{
    private readonly KdpCalculationService _sut;

    public KdpCalculationServiceTests()
    {
        var specs = new KdpSpecs();
        KdpSpecsAccessor.Current = specs;
        _sut = new KdpCalculationService(specs);
    }

    [Theory]
    [InlineData(24, 0.375)]
    [InlineData(150, 0.375)]
    [InlineData(151, 0.5)]
    [InlineData(300, 0.5)]
    [InlineData(301, 0.625)]
    [InlineData(500, 0.625)]
    [InlineData(501, 0.75)]
    [InlineData(700, 0.75)]
    [InlineData(701, 0.875)]
    [InlineData(828, 0.875)]
    public void GetRecommendedMargins_GutterTiers_MatchPageBoundaries(int pages, double expectedInside)
    {
        var m = _sut.GetRecommendedMargins(pages, hasBleed: false);
        Assert.Equal(expectedInside, m.InsideIn, 4);
        Assert.Equal(0.25, m.OutsideIn, 4);
        Assert.Equal(0.25, m.TopIn, 4);
        Assert.Equal(0.25, m.BottomIn, 4);
    }

    [Fact]
    public void GetRecommendedMargins_WithBleed_Uses0375Outer()
    {
        var m = _sut.GetRecommendedMargins(200, hasBleed: true);
        Assert.Equal(0.375, m.OutsideIn, 4);
        Assert.Equal(0.5, m.InsideIn, 4);
    }

    [Fact]
    public void GetPageCountLimits_Paperback_Is24To828()
    {
        var (min, max) = _sut.GetPageCountLimits(ProjectType.Paperback, PaperType.White);
        Assert.Equal(24, min);
        Assert.Equal(828, max);
    }

    [Fact]
    public void GetPageCountLimits_Hardcover_Is75To550()
    {
        var (min, max) = _sut.GetPageCountLimits(ProjectType.Hardcover, PaperType.White);
        Assert.Equal(75, min);
        Assert.Equal(550, max);
    }

    [Theory]
    [InlineData(23, ProjectType.Paperback, true)]
    [InlineData(24, ProjectType.Paperback, false)]
    [InlineData(828, ProjectType.Paperback, false)]
    [InlineData(829, ProjectType.Paperback, true)]
    [InlineData(74, ProjectType.Hardcover, true)]
    [InlineData(75, ProjectType.Hardcover, false)]
    [InlineData(550, ProjectType.Hardcover, false)]
    [InlineData(551, ProjectType.Hardcover, true)]
    public void ValidatePageCount_Boundaries(int pages, ProjectType type, bool expectWarning)
    {
        var warning = _sut.ValidatePageCount(pages, type, PaperType.White);
        if (expectWarning)
            Assert.NotNull(warning);
        else
            Assert.Null(warning);
    }

    [Fact]
    public void ValidatePageCount_Ebook_NeverWarns()
    {
        Assert.Null(_sut.ValidatePageCount(1, ProjectType.Ebook, PaperType.White));
        Assert.Null(_sut.ValidatePageCount(100_000, ProjectType.Ebook, PaperType.Cream));
    }

    [Theory]
    [InlineData(24, PaperType.White, ProjectType.Paperback, 0.0540)]
    [InlineData(150, PaperType.White, ProjectType.Paperback, 0.3378)]
    [InlineData(828, PaperType.White, ProjectType.Paperback, 1.8647)]
    [InlineData(200, PaperType.Cream, ProjectType.Paperback, 0.5000)]
    [InlineData(75, PaperType.White, ProjectType.Hardcover, 0.1444)] // (75/2)*0.002252+0.06 → MidpointRounding.ToEven
    [InlineData(550, PaperType.Cream, ProjectType.Hardcover, 0.7475)] // (550/2)*0.0025+0.06
    public void CalculateSpineWidthIn_MatchesSection2Formulas(
        int pages, PaperType paper, ProjectType type, double expected)
    {
        Assert.Equal(expected, _sut.CalculateSpineWidthIn(pages, paper, type), 4);
    }

    [Fact]
    public void CalculateFullCoverSizeIn_EbookFront_IsTrimOnly()
    {
        var (w, h) = _sut.CalculateFullCoverSizeIn(6, 9, 0.45, CoverType.EbookFront);
        Assert.Equal(6.0, w, 4);
        Assert.Equal(9.0, h, 4);
    }

    [Fact]
    public void CalculateFullCoverSizeIn_PaperbackWrap_Adds025()
    {
        var spine = _sut.CalculateSpineWidthIn(200, PaperType.White, ProjectType.Paperback);
        var (w, h) = _sut.CalculateFullCoverSizeIn(6, 9, spine, CoverType.PaperbackWrap);
        Assert.Equal(12.7004, w, 4);
        Assert.Equal(9.25, h, 4);
    }

    [Fact]
    public void CalculateFullCoverSizeIn_HardcoverWrap_Uses05PerEdge()
    {
        var spine = _sut.CalculateSpineWidthIn(200, PaperType.White, ProjectType.Hardcover);
        var (w, h) = _sut.CalculateFullCoverSizeIn(6, 9, spine, CoverType.HardcoverWrap);
        Assert.Equal(Math.Round(12 + spine + 1.0, 4), w, 4);
        Assert.Equal(10.0, h, 4);
    }

    [Fact]
    public void GetDocumentSizeWithBleedIn_OutsideEdgeOnlyOnWidth()
    {
        var (w, h) = _sut.GetDocumentSizeWithBleedIn(6, 9, BleedMode.AllSides);
        Assert.Equal(6.125, w, 4); // +0.125 outside only
        Assert.Equal(9.25, h, 4);  // +0.125 top + bottom
    }

    [Fact]
    public void GetDocumentSizeWithBleedIn_None_ReturnsTrim()
    {
        var (w, h) = _sut.GetDocumentSizeWithBleedIn(6, 9, BleedMode.None);
        Assert.Equal(6.0, w, 4);
        Assert.Equal(9.0, h, 4);
    }

    [Theory]
    [InlineData(1800, 2700, 6.0, 9.0, 300.0, DpiSeverity.Ok)]
    [InlineData(1500, 2250, 6.0, 9.0, 250.0, DpiSeverity.Warning)]
    [InlineData(900, 1350, 6.0, 9.0, 150.0, DpiSeverity.Poor)]
    public void CheckImageDpi_SeverityBands(
        int pxW, int pxH, double placeW, double placeH,
        double expectedDpi, DpiSeverity expectedSeverity)
    {
        var result = _sut.CheckImageDpi(pxW, pxH, placeW, placeH);
        Assert.Equal(expectedDpi, result.EffectiveDpi, 1);
        Assert.Equal(expectedSeverity, result.Severity);
    }

    [Fact]
    public void IsSpineTextAdvisable_UsesWidthThreshold()
    {
        Assert.False(_sut.IsSpineTextAdvisable(0.05));
        Assert.True(_sut.IsSpineTextAdvisable(0.0625));
    }
}
