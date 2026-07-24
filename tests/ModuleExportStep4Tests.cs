using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using EBookDashboard.Services.PdfExport;
using Xunit;

namespace EBookDashboard.Tests;

public class ModuleExportStep4Tests
{
    [Fact]
    public void DocxExport_BuildDocx_returns_zip_signature()
    {
        var details = new BookDetailsResponseDto
        {
            Success = true,
            BookId = 42,
            BookTitle = "Draft Book",
            AuthorName = "Author",
            Chapters =
            [
                new ChapterDto { ChapterNumber = 1, Title = "One", Content = "<p>Hello draft.</p>" }
            ]
        };
        var svc = new DocxExportService();
        var bytes = svc.BuildDocx(details, "Draft Book", "Author");
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 64);
        // DOCX is a ZIP: PK
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);
    }

    [Fact]
    public void BookPdfPlatformLayout_respects_explicit_trim_and_bleed()
    {
        var opt = new BookPdfExportOptions
        {
            Format = "Paperback",
            PublishingPlatform = "Amazon KDP",
            TrimWidthIn = 5.0,
            TrimHeightIn = 8.0,
            UseBleed = true
        };
        var layout = BookPdfPlatformLayout.Resolve(opt);
        Assert.Equal("5.25in", layout.PdfWidth); // 5 + 0.25 bleed
        Assert.Equal("8.25in", layout.PdfHeight);
        Assert.False(layout.PreferCssPageSize);
    }

    [Fact]
    public void ExportPdfPageLayout_uses_explicit_trim_inches()
    {
        var opt = new BookPdfExportOptions
        {
            TrimWidthIn = 6.0,
            TrimHeightIn = 9.0,
            UseBleed = false,
            MarginTopIn = 0.5,
            MarginBottomIn = 0.5,
            MarginInsideIn = 0.625,
            MarginOutsideIn = 0.5
        };
        var layout = ExportPdfPageLayout.ForOptions(opt);
        Assert.Equal(6.0 * 72.0, layout.PageWidthPt, 1);
        Assert.Equal(9.0 * 72.0, layout.PageHeightPt, 1);
        Assert.Equal(0.5 * 72.0, layout.MarginTopPt, 1);
        Assert.Equal(0.625 * 72.0, layout.MarginLeftPt, 1);
    }

    [Fact]
    public void KdpInteriorMarginPresets_gutter_steps_with_page_count()
    {
        Assert.Equal(0.375, KdpInteriorMarginPresets.RecommendedInsideIn(100));
        Assert.Equal(0.5, KdpInteriorMarginPresets.RecommendedInsideIn(200));
        Assert.Equal(0.875, KdpInteriorMarginPresets.RecommendedInsideIn(800));
        Assert.Equal(0.125, KdpInteriorMarginPresets.BleedIn);
    }

    [Fact]
    public void WrapCoverPdfExporter_produces_pdf_header()
    {
        // Minimal 1x1 PNG
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var pdf = WrapCoverPdfExporter.ToSinglePagePdf(png, 300);
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 64);
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);
    }
}
