using System.Globalization;
using System.Security.Cryptography;
using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EBookDashboard.Services;

/// <summary>
/// Local KDP wrap compositor: [back description panel | spine | exact front cover image].
/// The front panel is always drawn from the saved front-cover bytes (cover-fill resize only).
/// </summary>
public sealed class PrintWrapCompositor : IPrintWrapCompositor
{
    /// <summary>Minimum spine width (inches) before spine title text is rendered.</summary>
    public const decimal MinSpineTextWidthInches = 0.25m;

    private readonly SpineRenderer _spineRenderer;

    public PrintWrapCompositor(SpineRenderer spineRenderer) => _spineRenderer = spineRenderer;

    /// <inheritdoc />
    public PrintWrapComposeResult Compose(PrintWrapComposeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.FrontCoverBytes.Length == 0)
            throw new InvalidOperationException("Front cover bytes are required for print wrap compositing.");
        if (string.IsNullOrWhiteSpace(request.FrontCoverAssetRef))
            throw new InvalidOperationException("FrontCoverAssetRef is required — wrap generation cannot proceed without the approved front cover asset.");

        var layout = request.Layout;
        var dpi = layout.Dpi > 0 ? layout.Dpi : KdpConstants.Dpi;

        var canvasW = layout.PixelWidth;
        var canvasH = layout.PixelHeight;
        if (canvasW <= 0 || canvasH <= 0)
            throw new InvalidOperationException("Invalid KDP layout pixel dimensions.");

        var backX = 0;
        var spineX = InchesToPixels(layout.SpineXInches, dpi);
        var frontX = InchesToPixels(layout.FrontPanelXInches, dpi);
        // Paint into full canvas height (top + bottom bleed). Using PanelTopY left a black strip
        // across the top bleed and cropped the front panel incorrectly.
        var panelY = 0;
        var backW = Math.Max(1, spineX - backX);
        var spineW = Math.Max(1, frontX - spineX);
        var frontW = Math.Max(1, canvasW - frontX);
        var panelH = Math.Max(1, canvasH);

        var frontHash = Convert.ToHexString(SHA256.HashData(request.FrontCoverBytes));

        using var frontImage = Image.Load<Rgba32>(request.FrontCoverBytes);
        using var canvas = new Image<Rgba32>(canvasW, canvasH);
        canvas.Mutate(ctx => ctx.BackgroundColor(Color.Black));

        var theme = request.Theme ?? BookTheme.FromExportOptions(new BookPdfExportOptions());
        var titleColor = Color.FromRgb(245, 240, 230);
        var bodyColor = Color.FromRgb(220, 215, 205);

        // Back panel — extend front-cover artwork (blurred/darkened) so wrap matches the front design.
        canvas.Mutate(ctx =>
        {
            DrawBackPanelArt(ctx, frontImage, new Rectangle(backX, panelY, backW, panelH));
            DrawBackCoverContent(ctx, layout, dpi, backX, panelY, backW, panelH,
                request.Title, request.Author, request.Description, bodyColor, titleColor);
        });

        // Spine — color strip from front cover's spine edge + optional vertical title.
        var spineTitle = layout.SpineWidth >= MinSpineTextWidthInches
            ? TruncateSpineTitle(request.Title)
            : null;
        using (var spineImg = RenderSpineFromFront(frontImage, spineW, panelH, spineTitle))
        {
            canvas.Mutate(ctx => ctx.DrawImage(spineImg, new Point(spineX, panelY), 1f));
        }

        // Front panel — EXACT saved front artwork (cover-fill into panel rect only).
        canvas.Mutate(ctx => DrawCoverFill(ctx, frontImage, new Rectangle(frontX, panelY, frontW, panelH)));

        canvas.Metadata.HorizontalResolution = dpi;
        canvas.Metadata.VerticalResolution = dpi;
        canvas.Metadata.ResolutionUnits = SixLabors.ImageSharp.Metadata.PixelResolutionUnit.PixelsPerInch;

        using var ms = new MemoryStream();
        canvas.SaveAsPng(ms);

        return new PrintWrapComposeResult
        {
            PngBytes = ms.ToArray(),
            FrontCoverSha256 = frontHash,
            CanvasWidthPx = canvasW,
            CanvasHeightPx = canvasH,
            FrontPanelX = frontX,
            FrontPanelY = panelY,
            FrontPanelWidth = frontW,
            FrontPanelHeight = panelH,
            DescriptionMissing = string.IsNullOrWhiteSpace(request.Description)
        };
    }

    /// <summary>
    /// Back panel — heavily blurred, zoomed center-crop of the front art so the palette and mood
    /// carry over but NO front text remains legible (no mirror flip: flipped titles read backwards).
    /// </summary>
    private static void DrawBackPanelArt(IImageProcessingContext ctx, Image<Rgba32> front, Rectangle target)
    {
        if (target.Width <= 0 || target.Height <= 0) return;

        // Crop the middle band of the front (drop top 35% / bottom 15% where title/author text
        // usually sits), then zoom it to fill the back panel.
        var cropY = (int)(front.Height * 0.35);
        var cropH = Math.Max(1, (int)(front.Height * 0.50));
        var srcRect = new Rectangle(0, Math.Min(cropY, front.Height - 1), front.Width, Math.Min(cropH, front.Height - cropY));

        using var backArt = front.Clone(c => c.Crop(srcRect));
        // Blur radius scales with output size so text is destroyed at print resolution too.
        var blurSigma = Math.Max(16f, target.Width / 40f);
        backArt.Mutate(c =>
        {
            c.Resize(new ResizeOptions
            {
                Size = new Size(target.Width, target.Height),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
                Sampler = KnownResamplers.Lanczos3
            });
            c.GaussianBlur(blurSigma);
            c.Brightness(0.52f);
            c.Saturate(0.9f);
        });
        ctx.DrawImage(backArt, new Point(target.X, target.Y), 1f);

        // Dark overlay so the synopsis text stays readable on busy artwork.
        ctx.Fill(Color.FromRgba(0, 0, 0, 120), new RectangleF(target.X, target.Y, target.Width, target.Height));
    }

    /// <summary>
    /// Spine — solid strip from the front artwork's average color (edge strips are often
    /// near-black and made the spine disappear), with subtle shading and optional vertical title.
    /// </summary>
    private Image<Rgba32> RenderSpineFromFront(Image<Rgba32> front, int spineW, int panelH, string? spineTitle)
    {
        var avg = SampleAverageColor(front);
        var baseColor = Color.FromRgb(
            (byte)Math.Clamp((int)(avg.R * 0.82), 12, 255),
            (byte)Math.Clamp((int)(avg.G * 0.82), 12, 255),
            (byte)Math.Clamp((int)(avg.B * 0.82), 12, 255));

        var spine = new Image<Rgba32>(spineW, panelH);
        spine.Mutate(c =>
        {
            c.BackgroundColor(baseColor);
            // Soft edge shading so the spine reads as a rounded book edge, not a flat bar.
            var edge = Math.Max(1, spineW / 8);
            c.Fill(Color.FromRgba(0, 0, 0, 70), new RectangleF(0, 0, edge, panelH));
            c.Fill(Color.FromRgba(0, 0, 0, 70), new RectangleF(spineW - edge, 0, edge, panelH));
            c.Fill(Color.FromRgba(255, 255, 255, 18), new RectangleF(edge, 0, Math.Max(1, spineW - edge * 2), panelH));
        });

        if (!string.IsNullOrWhiteSpace(spineTitle) && spineW >= 12)
        {
            using var titled = _spineRenderer.Render(
                spineW, panelH, Color.Transparent, spineTitle, Color.FromRgb(245, 240, 230));
            spine.Mutate(c => c.DrawImage(titled, new Point(0, 0), 1f));
        }

        return spine;
    }

    private static Rgba32 SampleAverageColor(Image<Rgba32> source)
    {
        long r = 0, g = 0, b = 0;
        var count = 0;
        var stepX = Math.Max(1, source.Width / 48);
        var stepY = Math.Max(1, source.Height / 48);
        for (var y = 0; y < source.Height; y += stepY)
        {
            for (var x = 0; x < source.Width; x += stepX)
            {
                var px = source[x, y];
                r += px.R; g += px.G; b += px.B;
                count++;
            }
        }
        if (count == 0) return new Rgba32(60, 50, 45);
        return new Rgba32((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    /// <summary>Cover-fill draw used for both wrap front panel and verification helpers.</summary>
    public static void DrawCoverFill(IImageProcessingContext ctx, Image<Rgba32> source, Rectangle target)
    {
        if (target.Width <= 0 || target.Height <= 0) return;

        if (source.Width == target.Width && source.Height == target.Height)
        {
            ctx.DrawImage(source, new Point(target.X, target.Y), 1f);
            return;
        }

        // Pad (not center-crop) so title/author on the front are not silently clipped.
        // Remaining bleed strips use the front's average edge color.
        var pad = SampleAverageColor(source);
        using var resized = source.CloneAs<Rgba32>();
        resized.Mutate(c => c.Resize(new ResizeOptions
        {
            Size = new Size(target.Width, target.Height),
            Mode = ResizeMode.Pad,
            Position = AnchorPositionMode.Center,
            PadColor = Color.FromRgb(pad.R, pad.G, pad.B),
            Sampler = KnownResamplers.Lanczos3
        }));
        ctx.DrawImage(resized, new Point(target.X, target.Y), 1f);
    }

    /// <summary>Extracts the front panel from a composed wrap for pixel-equality verification.</summary>
    public static Image<Rgba32> ExtractFrontPanel(Image<Rgba32> wrap, PrintWrapComposeResult meta)
    {
        var clone = wrap.Clone(ctx => ctx.Crop(new Rectangle(
            meta.FrontPanelX, meta.FrontPanelY, meta.FrontPanelWidth, meta.FrontPanelHeight)));
        return clone;
    }

    /// <summary>Renders the front panel independently from source bytes — must match wrap front panel pixels.</summary>
    public static Image<Rgba32> RenderFrontPanelOnly(byte[] frontBytes, PrintWrapComposeResult meta, int dpi)
    {
        using var front = Image.Load<Rgba32>(frontBytes);
        var panel = new Image<Rgba32>(meta.FrontPanelWidth, meta.FrontPanelHeight);
        panel.Mutate(ctx => DrawCoverFill(ctx, front, new Rectangle(0, 0, meta.FrontPanelWidth, meta.FrontPanelHeight)));
        panel.Metadata.HorizontalResolution = dpi;
        panel.Metadata.VerticalResolution = dpi;
        return panel;
    }

    private static void DrawBackCoverContent(
        IImageProcessingContext ctx,
        KdpCalculateResponse layout,
        int dpi,
        int backX,
        int panelY,
        int backW,
        int panelH,
        string title,
        string? author,
        string? description,
        Color bodyColor,
        Color headingColor)
    {
        var bleedPx = InchesToPixels(layout.Bleed, dpi);
        var trimRight = backX + backW - bleedPx;
        var trimBottom = panelY + panelH - bleedPx;
        var trimLeft = backX + bleedPx;
        var trimTop = panelY + bleedPx;

        // KDP barcode reserve: 2" × 1.2", inset BarcodeMargin from bottom-right trim corner.
        var barcodeW = InchesToPixels(KdpPaperbackConstants.BarcodeZoneWidthInches, dpi);
        var barcodeH = InchesToPixels(KdpPaperbackConstants.BarcodeZoneHeightInches, dpi);
        var barcodeMarginPx = InchesToPixels(layout.BarcodeMargin, dpi);
        var barcodeX = trimRight - barcodeMarginPx - barcodeW;
        var barcodeY = trimBottom - barcodeMarginPx - barcodeH;
        ctx.Fill(Color.White, new RectangleF(barcodeX, barcodeY, barcodeW, barcodeH));

        if (string.IsNullOrWhiteSpace(description)) return;

        if (!SystemFonts.Families.Any()) return;

        var family = SystemFonts.Families.First();
        // Font sizes scale with panel width (print DPI aware) — previous clamps topped out at
        // 42px on a ~2700px-tall panel, which printed microscopically small.
        var titleFont = family.CreateFont(Math.Clamp(backW * 0.052f, 24f, 120f), FontStyle.Bold);
        var bodyFont = family.CreateFont(Math.Clamp(backW * 0.030f, 16f, 64f), FontStyle.Regular);
        var authorFont = family.CreateFont(Math.Clamp(backW * 0.026f, 14f, 56f), FontStyle.Italic);

        var textPad = InchesToPixels(0.35m, dpi);
        var maxTextWidth = trimRight - trimLeft - textPad * 2;
        var maxTextBottom = barcodeY - textPad;
        float y = trimTop + textPad * 1.5f;

        if (!string.IsNullOrWhiteSpace(title))
        {
            y = DrawWrappedTextCentered(ctx, titleFont, title.Trim(), headingColor, trimLeft + textPad, y, maxTextWidth);
            y += textPad * 0.4f;
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            y = DrawWrappedTextCentered(ctx, authorFont, "by " + author.Trim(), bodyColor, trimLeft + textPad, y, maxTextWidth);
            y += textPad * 0.3f;
        }

        // Thin divider under the heading block.
        var divW = maxTextWidth * 0.28f;
        ctx.Fill(Color.FromRgba(255, 255, 255, 90),
            new RectangleF(trimLeft + textPad + (maxTextWidth - divW) / 2f, y, divW, Math.Max(2, dpi / 100)));
        y += textPad * 0.8f;

        var desc = description.Trim();
        if (y < maxTextBottom)
            DrawWrappedText(ctx, bodyFont, desc, bodyColor, trimLeft + textPad, y, maxTextWidth, maxTextBottom - y);
    }

    private static float DrawWrappedTextCentered(
        IImageProcessingContext ctx,
        Font font,
        string text,
        Color color,
        float x,
        float y,
        float maxWidth)
    {
        var lineHeight = TextMeasurer.MeasureSize("Ag", new TextOptions(font)).Height * 1.25f;
        foreach (var line in WrapText(text, font, maxWidth))
        {
            var size = TextMeasurer.MeasureSize(line, new TextOptions(font));
            var lineX = x + Math.Max(0, (maxWidth - size.Width) / 2f);
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(lineX, y),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, line, color);
            y += lineHeight;
        }
        return y;
    }

    private static float DrawWrappedText(
        IImageProcessingContext ctx,
        Font font,
        string text,
        Color color,
        float x,
        float y,
        float maxWidth,
        float? maxHeight = null)
    {
        var lineHeight = TextMeasurer.MeasureSize("Ag", new TextOptions(font)).Height * 1.35f;
        var lines = WrapText(text, font, maxWidth);
        foreach (var line in lines)
        {
            if (maxHeight.HasValue && y > maxHeight.Value)
                break;
            var lineOpts = new RichTextOptions(font)
            {
                Origin = new PointF(x, y),
                WrappingLength = maxWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                LineSpacing = 1.25f
            };
            ctx.DrawText(lineOpts, line, color);
            y += lineHeight;
        }

        return y;
    }

    private static IEnumerable<string> WrapText(string text, Font font, float maxWidth)
    {
        foreach (var paragraph in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var line = "";
            foreach (var word in words)
            {
                var test = string.IsNullOrEmpty(line) ? word : line + " " + word;
                var size = TextMeasurer.MeasureSize(test, new TextOptions(font));
                if (size.Width > maxWidth && !string.IsNullOrEmpty(line))
                {
                    yield return line;
                    line = word;
                }
                else
                {
                    line = test;
                }
            }

            if (!string.IsNullOrEmpty(line))
                yield return line;
        }
    }

    private static Color SampleEdgeColor(Image<Rgba32> source, bool sampleLeftEdge)
    {
        var w = source.Width;
        var h = source.Height;
        if (w <= 0 || h <= 0) return Color.FromRgb(40, 40, 40);

        long r = 0, g = 0, b = 0;
        var count = 0;
        var xStart = sampleLeftEdge ? 0 : Math.Max(0, w - 4);
        var xEnd = sampleLeftEdge ? Math.Min(4, w) : w;
        for (var y = 0; y < h; y += Math.Max(1, h / 32))
        {
            for (var x = xStart; x < xEnd; x++)
            {
                var px = source[x, y];
                r += px.R;
                g += px.G;
                b += px.B;
                count++;
            }
        }

        if (count == 0) return Color.FromRgb(40, 40, 40);
        return Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    private static Color ParseHexColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        var s = hex.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        return Color.TryParseHex(s, out var c) ? c : fallback;
    }

    private static string TruncateSpineTitle(string title)
    {
        var t = title.Trim();
        return t.Length <= 48 ? t : t[..45] + "...";
    }

    private static int InchesToPixels(decimal inches, int dpi) =>
        (int)Math.Round((double)inches * dpi, MidpointRounding.AwayFromZero);
}
