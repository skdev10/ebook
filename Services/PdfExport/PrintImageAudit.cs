using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// Compares the images in the print HTML with the images embedded in the PDF.
/// A count mismatch fails the export and names the plate that did not survive.
/// </summary>
public static class PrintImageAudit
{
    private static readonly Regex ImgTag = new("<img\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Attr = new("\\b(alt|data-px|data-source-page)\\s*=\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Names of HTML images that are not embedded in <paramref name="pdf"/>. Empty when every image is present.</summary>
    public static IReadOnlyList<string> MissingFromPdf(string? html, byte[] pdf)
    {
        var expected = ExpectedImages(html);
        if (expected.Count == 0)
            return Array.Empty<string>();

        var actual = EmbeddedSizes(pdf);
        var used = new bool[actual.Count];
        var missing = new List<string>();
        var sizedMatched = 0;
        foreach (var image in expected)
        {
            if (image.W <= 0 || image.H <= 0)
                continue;
            var found = -1;
            for (var i = 0; i < actual.Count; i++)
            {
                if (used[i] || actual[i].W != image.W || actual[i].H != image.H)
                    continue;
                found = i;
                break;
            }

            if (found < 0)
                missing.Add(image.Name);
            else
            {
                used[found] = true;
                sizedMatched++;
            }
        }

        var unsized = expected.Count(image => image.W <= 0 || image.H <= 0);
        var leftover = actual.Count - sizedMatched;
        if (leftover < unsized)
        {
            foreach (var image in expected.Where(image => image.W <= 0 || image.H <= 0).Take(unsized - Math.Max(0, leftover)))
                missing.Add(image.Name);
        }

        if (missing.Count == 0 && expected.Count != actual.Count)
            missing.Add($"image count {expected.Count} in the import, {actual.Count} in the PDF");
        return missing;
    }

    private static List<Expected> ExpectedImages(string? html)
    {
        var list = new List<Expected>();
        if (string.IsNullOrEmpty(html))
            return list;
        var n = 0;
        foreach (Match tag in ImgTag.Matches(html))
        {
            n++;
            string? alt = null;
            string? px = null;
            string? page = null;
            foreach (Match attr in Attr.Matches(tag.Value))
            {
                var key = attr.Groups[1].Value;
                var value = attr.Groups[2].Value;
                if (key.Equals("alt", StringComparison.OrdinalIgnoreCase)) alt = value;
                else if (key.Equals("data-px", StringComparison.OrdinalIgnoreCase)) px = value;
                else if (key.Equals("data-source-page", StringComparison.OrdinalIgnoreCase)) page = value;
            }

            if (string.IsNullOrWhiteSpace(px) || !TrySize(px, out var w, out var h))
            {
                list.Add(new Expected(0, 0, Name(alt, page, n, px)));
                continue;
            }

            list.Add(new Expected(w, h, Name(alt, page, n, px)));
        }

        return list;
    }

    private static string Name(string? alt, string? page, int index, string? px)
    {
        if (!string.IsNullOrWhiteSpace(alt))
            return alt.Trim();
        if (!string.IsNullOrWhiteSpace(page))
            return "illustration page " + page.Trim();
        return string.IsNullOrWhiteSpace(px) ? "image " + index : "image " + px;
    }

    private static bool TrySize(string px, out int w, out int h)
    {
        w = h = 0;
        var parts = px.Split('x', 'X');
        return parts.Length == 2
               && int.TryParse(parts[0], out w)
               && int.TryParse(parts[1], out h)
               && w > 0
               && h > 0;
    }

    private static List<Size> EmbeddedSizes(byte[] pdf)
    {
        var list = new List<Size>();
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
        {
            try
            {
                foreach (var image in page.GetImages())
                    list.Add(new Size(image.WidthInSamples, image.HeightInSamples));
            }
            catch
            {
                /* unreadable image dictionary */
            }
        }

        return list;
    }

    private readonly record struct Expected(int W, int H, string Name);
    private readonly record struct Size(int W, int H);
}
