using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EBookDashboard.Services;

/// <summary>Preview-only watermark. Clean files are served only from entitlement-checked downloads.</summary>
public static class CoverPreviewWatermark
{
    public static byte[] Apply(byte[] imageBytes)
    {
        try
        {
            using var image = Image.Load<Rgba32>(imageBytes);
            Font font;
            try
            {
                font = SystemFonts.CreateFont("Arial", Math.Max(18, image.Width / 18f), FontStyle.Bold);
            }
            catch
            {
                font = SystemFonts.CreateFont("DejaVu Sans", Math.Max(18, image.Width / 18f), FontStyle.Bold);
            }
            var banner = Color.FromRgba(0, 0, 0, 90);
            var color = Color.FromRgba(255, 255, 255, 200);
            var y = image.Height * 0.40f;
            image.Mutate(ctx =>
            {
                ctx.Fill(banner, new RectangularPolygon(0, y, image.Width, Math.Max(48, image.Height * 0.18f)));
                ctx.DrawText("PREVIEW", font, color, new PointF(image.Width * 0.18f, y + 8));
            });
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
        }
        catch
        {
            return imageBytes;
        }
    }
}
