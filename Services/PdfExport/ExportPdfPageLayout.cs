using System.Globalization;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services.PdfExport;

/// <summary>6×9 print page box and KDP-style margins (matches <see cref="BookPdfPlatformLayout"/>).</summary>
public sealed class ExportPdfPageLayout
{
    public const double InchesToPt = 72.0;
    public const double MmToPt = InchesToPt / 25.4;

    public double PageWidthPt { get; }
    public double PageHeightPt { get; }
    public double MarginLeftPt { get; }
    public double MarginRightPt { get; }
    public double MarginTopPt { get; }
    public double MarginBottomPt { get; }
    public double ContentWidthPt { get; }
    public double ContentHeightPt { get; }

    public static ExportPdfPageLayout ForOptions(BookPdfExportOptions opt)
    {
        var spec = BookPdfPlatformLayout.Resolve(opt);
        if (spec.UseBuiltInFormat)
            return A4Fallback();

        var w = 6.0 * InchesToPt;
        var h = 9.0 * InchesToPt;
        var top = ParseMarginMm(spec.MarginTop);
        var bottom = ParseMarginMm(spec.MarginBottom);
        var left = ParseMarginMm(spec.MarginLeft);
        var right = ParseMarginMm(spec.MarginRight);
        return new ExportPdfPageLayout(w, h, left, right, top, bottom);
    }

    public static ExportPdfPageLayout SixByNineKdp() =>
        ForOptions(new BookPdfExportOptions { Format = "Paperback", PublishingPlatform = "Amazon KDP" });

    private static ExportPdfPageLayout A4Fallback()
    {
        const double w = 595.28;
        const double h = 841.89;
        var m = 22.0 * MmToPt;
        return new ExportPdfPageLayout(w, h, m, m, m, m);
    }

    private ExportPdfPageLayout(double pageW, double pageH, double left, double right, double top, double bottom)
    {
        PageWidthPt = pageW;
        PageHeightPt = pageH;
        MarginLeftPt = left;
        MarginRightPt = right;
        MarginTopPt = top;
        MarginBottomPt = bottom;
        ContentWidthPt = pageW - left - right;
        ContentHeightPt = pageH - top - bottom;
    }

    private static double ParseMarginMm(string margin)
    {
        if (string.IsNullOrWhiteSpace(margin)) return 18 * MmToPt;
        var s = margin.Trim().ToLowerInvariant();
        if (s.EndsWith("mm", StringComparison.Ordinal))
        {
            if (double.TryParse(s[..^2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var mm))
                return mm * MmToPt;
        }
        if (s.EndsWith("in", StringComparison.Ordinal))
        {
            if (double.TryParse(s[..^2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var inches))
                return inches * InchesToPt;
        }
        return 18 * MmToPt;
    }
}
