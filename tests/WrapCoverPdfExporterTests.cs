using EBookDashboard.Services;
using EBookDashboard.Services.PdfExport;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace EBookDashboard.Tests;

public class WrapCoverPdfExporterTests
{
    [Fact]
    public void ToSinglePagePdf_creates_valid_pdf_header()
    {
        using var img = new Image<Rgba32>(600, 900);
        img.Mutate(c => c.BackgroundColor(Color.Orange));
        using var pngMs = new MemoryStream();
        img.SaveAsPng(pngMs);
        var png = pngMs.ToArray();

        var pdf = WrapCoverPdfExporter.ToSinglePagePdf(png, dpi: 300);

        Assert.True(pdf.Length > 128);
        Assert.Equal((byte)'%', pdf[0]);
        Assert.Equal((byte)'P', pdf[1]);
        Assert.Equal((byte)'D', pdf[2]);
        Assert.Equal((byte)'F', pdf[3]);
    }
}
