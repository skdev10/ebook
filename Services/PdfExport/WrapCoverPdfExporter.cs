using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// Converts a full KDP wrap image (PNG/JPEG) into a single-page PDF at print DPI.
/// Page size = pixel dimensions ÷ DPI inches — matches the wrap canvas exactly.
/// </summary>
public static class WrapCoverPdfExporter
{
    /// <summary>
    /// Builds a one-page PDF from wrap image bytes. Uses <paramref name="dpi"/> (default 300).
    /// </summary>
    public static byte[] ToSinglePagePdf(byte[] imageBytes, int dpi = KdpConstants.Dpi)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
            throw new ArgumentException("Wrap image bytes are required.", nameof(imageBytes));
        if (dpi <= 0) dpi = KdpConstants.Dpi;

        using var imageStream = new MemoryStream(imageBytes, writable: false);
        using var image = XImage.FromStream(imageStream);

        var widthIn = image.PixelWidth / (double)dpi;
        var heightIn = image.PixelHeight / (double)dpi;

        using var doc = new PdfDocument();
        doc.Info.Title = "KDP Full Cover Wrap";
        var page = doc.AddPage();
        page.Width = XUnit.FromInch(widthIn);
        page.Height = XUnit.FromInch(heightIn);

        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }

        using var outMs = new MemoryStream();
        doc.Save(outMs, closeStream: false);
        return outMs.ToArray();
    }
}
