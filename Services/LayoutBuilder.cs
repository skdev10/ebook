using EBookDashboard.Models;

namespace EBookDashboard.Services;

public record Rect(int X, int Y, int Width, int Height);

public class CoverLayout
{
    public int CanvasWidthPx { get; set; }
    public int CanvasHeightPx { get; set; }
    public Rect Back { get; set; } = default!;
    public Rect Spine { get; set; } = default!;
    public Rect Front { get; set; } = default!;
    public Rect BackSafe { get; set; } = default!;
    public Rect FrontSafe { get; set; } = default!;
    public Rect SpineSafe { get; set; } = default!;
}

public class LayoutBuilder
{
    private static int Px(double inches) => (int)Math.Round(inches * KdpConstants.Dpi);

    public CoverLayout Build(CoverResult dims, double trimWidthInches, double outerMargin,
        ReadingDirection direction = ReadingDirection.LeftToRight)
    {
        int canvasW = dims.FullCoverWidthPx;
        int canvasH = dims.FullCoverHeightPx;

        int outer = Px(outerMargin);
        int trimW = Px(trimWidthInches);
        int spineW = Px(dims.SpineWidthInches);
        int sideBlock = outer + trimW;

        var left = new Rect(0, 0, sideBlock, canvasH);
        var spine = new Rect(sideBlock, 0, spineW, canvasH);
        var right = new Rect(sideBlock + spineW, 0, canvasW - sideBlock - spineW, canvasH);

        bool ltr = direction == ReadingDirection.LeftToRight;
        var back = ltr ? left : right;
        var front = ltr ? right : left;

        int safeIn = Px(KdpConstants.SafeFromTrim);
        int safeSp = Px(KdpConstants.SpineSafe);
        int outerEdge = outer + safeIn;

        return new CoverLayout
        {
            CanvasWidthPx = canvasW,
            CanvasHeightPx = canvasH,
            Back = back,
            Spine = spine,
            Front = front,
            BackSafe = SafeBox(back, outerEdge, safeIn, spineOnRight: ltr, safeSp),
            FrontSafe = SafeBox(front, outerEdge, safeIn, spineOnRight: !ltr, safeSp),
            SpineSafe = new Rect(spine.X + safeSp, safeIn, spine.Width - 2 * safeSp, canvasH - 2 * safeIn),
        };
    }

    private static Rect SafeBox(Rect r, int outerEdge, int topBottom, bool spineOnRight, int spineGap)
    {
        int left = r.X + (spineOnRight ? outerEdge : spineGap);
        int right = r.X + r.Width - (spineOnRight ? spineGap : outerEdge);
        return new Rect(left, topBottom, right - left, r.Height - 2 * topBottom);
    }
}
