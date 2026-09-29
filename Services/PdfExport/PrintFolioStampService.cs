using System.Globalization;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// Stamps KDP-style folios after Chromium print: roman (or blank) on front matter, arabic from 1 on body.
/// Also writes PDF document Title/Author metadata (Chromium alone often leaves Title=about:blank).
/// </summary>
public static class PrintFolioStampService
{
    /// <param name="frontMatterPageCount">Physical pages before main body (title/copyright/toc/preface…). Body arabic starts after this.</param>
    /// <param name="suppressFrontPages">0-based physical page indexes with no folio (title, blanks).</param>
    public static byte[] Stamp(
        byte[] pdfBytes,
        int frontMatterPageCount,
        string? title,
        string? author,
        IReadOnlyCollection<int>? suppressFrontPages = null)
    {
        if (pdfBytes is not { Length: > 100 })
            return pdfBytes;

        var suppress = suppressFrontPages ?? new[] { 0 }; // title page = first physical page
        using var ms = new MemoryStream(pdfBytes);
        using var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);

        if (!string.IsNullOrWhiteSpace(title))
            doc.Info.Title = title.Trim();
        if (!string.IsNullOrWhiteSpace(author))
            doc.Info.Author = author.Trim();
        doc.Info.Creator = "EBookDashboard";

        XFont? font = null;
        try { font = new XFont("Georgia", 9); } catch { /* try next */ }
        try { font ??= new XFont("Times New Roman", 9); } catch { /* try next */ }
        try { font ??= new XFont("Arial", 9); } catch { /* give up */ }
        if (font == null)
            return Save(doc);

        var brush = XBrushes.Black;
        var frontCount = Math.Clamp(frontMatterPageCount, 0, doc.PageCount);
        var bodyOrdinal = 0;

        for (var i = 0; i < doc.PageCount; i++)
        {
            string? label = null;
            if (i < frontCount)
            {
                if (suppress.Contains(i))
                    label = null; // no number on title / blank
                else
                {
                    // Roman ordinal among numbered front pages only (skip suppressed).
                    var romanIndex = 0;
                    for (var j = 0; j <= i; j++)
                    {
                        if (!suppress.Contains(j))
                            romanIndex++;
                    }
                    label = ToRoman(romanIndex).ToLowerInvariant();
                }
            }
            else
            {
                bodyOrdinal++;
                label = bodyOrdinal.ToString(CultureInfo.InvariantCulture);
            }

            if (string.IsNullOrEmpty(label))
                continue;

            var page = doc.Pages[i];
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            var size = gfx.MeasureString(label, font);
            var x = (page.Width.Point - size.Width) / 2.0;
            var y = page.Height.Point - 28;
            gfx.DrawString(label, font, brush, x, y);
        }

        return Save(doc);
    }

    private static byte[] Save(PdfDocument doc)
    {
        using var outMs = new MemoryStream();
        doc.Save(outMs, false);
        return outMs.ToArray();
    }

    internal static string ToRoman(int number)
    {
        if (number <= 0) return number.ToString(CultureInfo.InvariantCulture);
        var map = new (int Value, string Numeral)[]
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
        };
        var sb = new StringBuilder();
        foreach (var (value, numeral) in map)
        {
            while (number >= value)
            {
                sb.Append(numeral);
                number -= value;
            }
        }
        return sb.ToString();
    }
}
