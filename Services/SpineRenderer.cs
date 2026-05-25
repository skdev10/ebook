using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EBookDashboard.Services;

public class SpineRenderer
{
    // Renders a spine image filled with a solid colour, plus optional vertical title.
    // If no system font is available, it safely falls back to a solid colour spine.
    public Image<Rgba32> Render(int widthPx, int heightPx, Color background,
        string? title = null, Color? textColor = null)
    {
        var spine = new Image<Rgba32>(widthPx, heightPx);
        spine.Mutate(c => c.BackgroundColor(background));

        if (!string.IsNullOrWhiteSpace(title) && SystemFonts.Families.Any())
        {
            try
            {
                using var strip = new Image<Rgba32>(heightPx, widthPx);
                strip.Mutate(c => c.BackgroundColor(Color.Transparent));

                var family = SystemFonts.Families.First();
                float fontSize = Math.Clamp(widthPx * 0.45f, 14f, 200f);
                var font = family.CreateFont(fontSize, FontStyle.Bold);
                var color = textColor ?? Color.White;

                var options = new RichTextOptions(font)
                {
                    Origin = new PointF(heightPx / 2f, widthPx / 2f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                strip.Mutate(c => c.DrawText(options, title!, color));
                strip.Mutate(c => c.Rotate(RotateMode.Rotate90));
                spine.Mutate(c => c.DrawImage(strip, new Point(0, 0), 1f));
            }
            catch
            {
                // Never break export over a font issue — leave the spine solid.
            }
        }

        return spine;
    }
}
