using System.Globalization;
using System.Text;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>Builds long-form sample chapters for layout verification (preview ≡ PDF).</summary>
public static class InteriorSampleDocumentBuilder
{
    /// <summary>Lorem paragraph used to fill trade-novel sample interiors.</summary>
    public const string SampleParagraph =
        "The evening settled over the harbor in layers of violet and rust, and every lantern along the quay seemed to breathe in the same slow rhythm. " +
        "She kept her thumb on the spine of the ledger, counting margins that were not ink but memory, and listened for the particular silence that meant a ship had turned without farewell. " +
        "When the bell finally rang, it was not alarm but invitation, and the city answered with footsteps on wet stone.";

    /// <summary>Build chapter HTML sections (~<paramref name="paragraphsPerChapter"/> paragraphs each).</summary>
    public static string BuildSampleChapterSections(
        int chapterCount,
        int paragraphsPerChapter,
        BookPdfExportOptions opt,
        string bookTitle = "Trade Novel Sample")
    {
        var ph = BookManuscriptHtmlFormatter.CreateBaseContext(bookTitle, null, null, "Fiction", "Sample Author");
        var chapters = new List<ChapterDto>();
        for (var c = 1; c <= chapterCount; c++)
        {
            var sb = new StringBuilder();
            for (var p = 0; p < paragraphsPerChapter; p++)
            {
                sb.Append("<p class=\"manuscript-p\">").Append(SampleParagraph).Append("</p>");
            }
            chapters.Add(new ChapterDto
            {
                ChapterNumber = c,
                Title = $"Chapter {c}",
                Content = sb.ToString()
            });
        }

        return InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(chapters, ph, opt);
    }

    /// <summary>
    /// Estimates body lines on a typical continuation page (no chapter sink / running head band).
    /// Target: ~30–35 lines for ElegantTrade Medium at 1.6 on 6×9.
    /// </summary>
    public static double EstimateBodyLinesPerContinuationPage(BookPdfExportOptions opt)
    {
        var typography = InteriorTypographyPresets.Resolve(opt);
        if (!double.TryParse(typography.BodyFontSizePt, NumberStyles.Any, CultureInfo.InvariantCulture, out var pt))
            pt = InteriorTypographyPresets.PtMedium;
        if (!double.TryParse(typography.LineHeight, NumberStyles.Any, CultureInfo.InvariantCulture, out var lh))
            lh = InteriorSpacingTheme.LineHeightNormal;

        var pageHeightIn = InteriorSpacingTheme.TrimHeightMm / InteriorSpacingTheme.MmPerInch;
        var usableIn = pageHeightIn - InteriorSpacingTheme.MarginTopIn - InteriorSpacingTheme.MarginBottomIn;
        var lineIn = (pt * lh) / 72.0;
        return usableIn / lineIn;
    }
}
