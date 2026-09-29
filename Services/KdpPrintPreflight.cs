using System.Globalization;
using System.Text;
using EBookDashboard.Configuration;
using EBookDashboard.Models.DTO;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace EBookDashboard.Services;

/// <summary>
/// Preflight checks for Amazon KDP print PDF compliance (trim, margins, fonts, images, markers).
/// </summary>
public static class KdpPrintPreflight
{
    public sealed record Check(string Code, bool Pass, string Message, string Severity = "FAIL");

    public sealed record Report(IReadOnlyList<Check> Checks)
    {
        /// <summary>True when every hard-fail check passed (WARN severity does not block).</summary>
        public bool Passed => Checks.All(c => c.Pass || string.Equals(c.Severity, "WARN", StringComparison.OrdinalIgnoreCase));

        public string Format()
        {
            var hardFail = Checks.Any(c => !c.Pass && !string.Equals(c.Severity, "WARN", StringComparison.OrdinalIgnoreCase));
            var hasWarn = Checks.Any(c => !c.Pass && string.Equals(c.Severity, "WARN", StringComparison.OrdinalIgnoreCase));
            var sb = new StringBuilder();
            sb.AppendLine(!hardFail
                ? (hasWarn ? "KDP PREFLIGHT: PASS (with warnings)" : "KDP PREFLIGHT: PASS")
                : "KDP PREFLIGHT: FAIL");
            foreach (var c in Checks)
            {
                var tag = c.Pass ? "OK" : (string.Equals(c.Severity, "WARN", StringComparison.OrdinalIgnoreCase) ? "WARN" : "FAIL");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  [{tag}] {c.Code}: {c.Message}");
            }
            return sb.ToString();
        }
    }

    /// <summary>Validate export options + rendered PDF bytes before handing the file to the user.</summary>
    public static Report Validate(
        byte[] pdfBytes,
        BookPdfExportOptions opt,
        string? documentTitle,
        string? documentAuthor,
        int? expectedImageCount = null)
    {
        var checks = new List<Check>();
        var specs = KdpSpecsAccessor.Current;

        // Trim size
        var tw = opt.TrimWidthIn ?? 6.0;
        var th = opt.TrimHeightIn ?? 9.0;
        var trimOk = specs.TrimPresets.Any(p =>
            Math.Abs(p.WidthIn - tw) < 0.02 && Math.Abs(p.HeightIn - th) < 0.02);
        checks.Add(new Check(
            "TRIM",
            trimOk,
            trimOk
                ? FormattableString.Invariant($"Trim {tw:0.####}×{th:0.####} in is a known KDP size.")
                : FormattableString.Invariant($"Trim {tw:0.####}×{th:0.####} in is not in the KDP preset list.")));

        // Bleed page box
        if (opt.UseBleed)
        {
            var (bw, bh) = (tw + specs.BleedIn, th + 2 * specs.BleedIn);
            checks.Add(new Check(
                "BLEED",
                Math.Abs(bw - (tw + 0.125)) < 0.001 && Math.Abs(bh - (th + 0.25)) < 0.001,
                FormattableString.Invariant($"Bleed page box should be {bw:0.###}×{bh:0.###} in (outside + top/bottom).")));
        }

        // Margins
        var inside = opt.MarginInsideIn ?? KdpInteriorMarginCalculator.InsideMarginIn(200);
        var outside = opt.MarginOutsideIn ?? KdpInteriorMarginCalculator.OutsideMarginIn(opt.UseBleed);
        var minOutside = opt.UseBleed ? 0.375 : 0.25;
        checks.Add(new Check(
            "MARGIN_OUTSIDE",
            outside >= minOutside - 0.001,
            FormattableString.Invariant($"Outside margin {outside:0.###}in (min {minOutside:0.###}in).")));
        checks.Add(new Check(
            "MARGIN_INSIDE",
            inside >= 0.375 - 0.001,
            FormattableString.Invariant($"Inside/gutter margin {inside:0.###}in.")));

        // Metadata
        var titleOk = !string.IsNullOrWhiteSpace(documentTitle)
                      && !documentTitle.Equals("about:blank", StringComparison.OrdinalIgnoreCase)
                      && !documentTitle.Equals("Untitled", StringComparison.OrdinalIgnoreCase);
        checks.Add(new Check(
            "TITLE_META",
            titleOk,
            titleOk ? "Document title is set from user settings." : "Document title missing or still a placeholder."));

        var authorOk = !string.IsNullOrWhiteSpace(documentAuthor);
        checks.Add(new Check(
            "AUTHOR_META",
            authorOk,
            authorOk ? "Document author is set." : "Document author is empty — set it in book settings."));

        if (pdfBytes is not { Length: > 100 })
        {
            checks.Add(new Check("PDF_BYTES", false, "PDF is empty."));
            return new Report(checks);
        }

        try
        {
            using var doc = PdfDocument.Open(new MemoryStream(pdfBytes, writable: false));
            var pageCount = doc.NumberOfPages;
            checks.Add(new Check(
                "PAGE_COUNT",
                pageCount >= 24 && pageCount <= 828,
                pageCount > 828
                    ? FormattableString.Invariant(
                        $"Page count {pageCount} exceeds KDP paperback max 828. Choose a larger trim, smaller font (min 10.5pt), or tighter line spacing.")
                    : pageCount < 24
                        ? FormattableString.Invariant($"Page count {pageCount} is below KDP minimum 24.")
                        : FormattableString.Invariant($"Page count {pageCount} (KDP paperback range 24–828).")));

            // Internal markers must never appear in print text
            var markerHit = false;
            foreach (var page in doc.GetPages())
            {
                var t = page.Text ?? "";
                if (t.Contains("TOCMEASURE_", StringComparison.Ordinal))
                {
                    markerHit = true;
                    break;
                }
            }

            checks.Add(new Check(
                "NO_INTERNAL_MARKERS",
                !markerHit,
                markerHit ? "Internal TOCMEASURE markers found in PDF text." : "No internal TOC measure markers in PDF text."));

            // Font embedding — PdfPig exposes font names; Type3 often lack reliable embedding.
            var type3 = 0;
            var fonts = 0;
            foreach (var page in doc.GetPages())
            {
                foreach (var letter in page.Letters ?? Enumerable.Empty<Letter>())
                {
                    fonts++;
                    var name = letter.FontName ?? "";
                    if (name.Contains("Type3", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("Type 3", StringComparison.OrdinalIgnoreCase))
                        type3++;
                }
            }

            checks.Add(new Check(
                "FONT_EMBED",
                type3 == 0 || type3 < Math.Max(50, fonts / 5000),
                type3 == 0
                    ? "No Type 3 fonts detected in content stream."
                    : FormattableString.Invariant($"Type 3 glyphs detected ({type3}/{Math.Max(fonts, 1)}). Embed TrueType/OpenType subsets."),
                Severity: type3 > 0 && type3 < Math.Max(50, fonts / 5000) ? "WARN" : "FAIL"));

            // Images DPI (best-effort via placed size)
            var lowDpi = 0;
            var imgCount = 0;
            foreach (var page in doc.GetPages())
            {
                foreach (var img in page.GetImages())
                {
                    if (ChapterDocumentImportService.IsLikelyFullPageBackground(img, page.Width, page.Height))
                        continue;
                    imgCount++;
                    var wIn = Math.Abs(img.BoundingBox.Width) / 72.0;
                    var hIn = Math.Abs(img.BoundingBox.Height) / 72.0;
                    if (wIn <= 0.01 || hIn <= 0.01) continue;
                    var dpiX = img.WidthInSamples / wIn;
                    var dpiY = img.HeightInSamples / hIn;
                    if (Math.Min(dpiX, dpiY) < 300)
                        lowDpi++;
                }
            }

            checks.Add(new Check(
                "IMAGE_DPI",
                lowDpi == 0,
                lowDpi == 0
                    ? FormattableString.Invariant($"All {imgCount} content images meet ≥300 DPI (placed).")
                    : FormattableString.Invariant($"{lowDpi} image(s) below 300 DPI effective."),
                Severity: "WARN"));

            if (expectedImageCount is >= 0)
            {
                var match = Math.Abs(imgCount - expectedImageCount.Value) <= Math.Max(2, expectedImageCount.Value / 10);
                checks.Add(new Check(
                    "IMAGE_COUNT",
                    match,
                    FormattableString.Invariant($"Output images={imgCount}, expected≈{expectedImageCount.Value}.")));
            }
        }
        catch (Exception ex)
        {
            checks.Add(new Check("PDF_PARSE", false, "Could not parse export PDF: " + ex.Message));
        }

        return new Report(checks);
    }
}
