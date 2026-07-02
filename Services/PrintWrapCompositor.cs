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
        var panelY = InchesToPixels(layout.PanelTopYInches, dpi);
        var backW = Math.Max(1, spineX - backX);
        var spineW = Math.Max(1, frontX - spineX);
        var frontW = Math.Max(1, canvasW - frontX);
        var panelH = Math.Max(1, canvasH - panelY);

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

    /// <summary>Back panel — mirrored front art with soft blur and readable text overlay.</summary>
    private static void DrawBackPanelArt(IImageProcessingContext ctx, Image<Rgba32> front, Rectangle target)
    {
        if (target.Width <= 0 || target.Height <= 0) return;

        using var backArt = front.CloneAs<Rgba32>();
        backArt.Mutate(c =>
        {
            c.Flip(FlipMode.Horizontal);
            c.Resize(new ResizeOptions
            {
                Size = new Size(target.Width, target.Height),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
                Sampler = KnownResamplers.Lanczos3
            });
            c.GaussianBlur(5);
            c.Brightness(0.58f);
            c.Contrast(1.04f);
        });
        ctx.DrawImage(backArt, new Point(target.X, target.Y), 1f);

        // Dark vignette so synopsis text stays readable on busy covers.
        ctx.Fill(Color.FromRgba(0, 0, 0, 110), new RectangleF(target.X, target.Y, target.Width, target.Height));
    }

    /// <summary>Spine strip from front edge — seamless with front panel.</summary>
    private Image<Rgba32> RenderSpineFromFront(Image<Rgba32> front, int spineW, int panelH, string? spineTitle)
    {
        var stripW = Math.Clamp(Math.Max(6, front.Width / 5), 6, front.Width);
        using var edgeStrip = front.Clone(c => c.Crop(new Rectangle(0, 0, stripW, front.Height)));
        using var resized = edgeStrip.Clone(c => c.Resize(spineW, panelH));
        var spine = new Image<Rgba32>(spineW, panelH);
        spine.Mutate(c =>
        {
            c.DrawImage(resized, new Point(0, 0), 1f);
            c.Brightness(0.72f);
            c.Contrast(1.06f);
        });

        if (!string.IsNullOrWhiteSpace(spineTitle) && spineW >= 12)
        {
            using var titled = _spineRenderer.Render(
                spineW, panelH, Color.Transparent, spineTitle, Color.FromRgb(245, 240, 230));
            spine.Mutate(c => c.DrawImage(titled, new Point(0, 0), 1f));
        }

        return spine;
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

        using var resized = source.CloneAs<Rgba32>();
        resized.Mutate(c => c.Resize(new ResizeOptions
        {
            Size = new Size(target.Width, target.Height),
            // KDP front panel: center-crop to fill bleed+trim (matches print export).
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center,
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
        var titleFont = family.CreateFont(Math.Clamp(backW * 0.045f, 18f, 42f), FontStyle.Bold);
        var bodyFont = family.CreateFont(Math.Clamp(backW * 0.028f, 12f, 22f), FontStyle.Regular);
        var authorFont = family.CreateFont(Math.Clamp(backW * 0.024f, 11f, 18f), FontStyle.Italic);

        var textPad = InchesToPixels(0.2m, dpi);
        var maxTextWidth = trimRight - trimLeft - textPad * 2;
        var maxTextBottom = barcodeY - textPad;
        float y = trimTop + textPad;

        if (!string.IsNullOrWhiteSpace(title))
        {
            y = DrawWrappedText(ctx, titleFont, title.Trim(), headingColor, trimLeft + textPad, y, maxTextWidth);
            y += textPad / 2;
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            y = DrawWrappedText(ctx, authorFont, author.Trim(), bodyColor, trimLeft + textPad, y, maxTextWidth);
            y += textPad / 2;
        }

        var desc = description.Trim();
        if (y < maxTextBottom)
            DrawWrappedText(ctx, bodyFont, desc, bodyColor, trimLeft + textPad, y, maxTextWidth, maxTextBottom - y);
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
        var lineHeight = TextMeasurer.MeasureSize("Ag", new TextOptions(font)).Height;
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
