using System.Globalization;
using System.Net;
using System.Text;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>
/// Builds print HTML using the same DOM structure as Book Formatter / AI Writer preview (WYSIWYG).
/// </summary>
public static class InteriorPrintDocumentBuilder
{
    /// <summary>Compose all narrative chapter sections for PDF/print.</summary>
    public static string BuildChapterSectionsHtml(
        IReadOnlyList<ChapterDto> chapters,
        BookManuscriptHtmlFormatter.PlaceholderContext phBase,
        BookPdfExportOptions opt)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);
        var bodyExtraClass = interior == "Classic" ? "classic-body" : "";
        var ordered = BookChapterExportHelper.OrderForExport(chapters);
        var sb = new StringBuilder();
        var narrativeOrdinal = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var ch = ordered[i];
            if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                narrativeOrdinal++;

            var displayNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 0 : narrativeOrdinal;
            var phNum = displayNum > 0 ? displayNum : 1;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            var displayHeading = BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum);
            var titleHtml = BookManuscriptHtmlFormatter.EscapeHtml(displayHeading);
            var bodyHtml = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(ch.Content, ph, displayHeading);
            var bodyClass = string.IsNullOrEmpty(bodyExtraClass) ? "reader-page-body" : "reader-page-body " + bodyExtraClass;
            var sectionId = i + 1;

            sb.Append(CultureInfo.InvariantCulture, $"""
                <section class="chapter" id="ch-{sectionId}">
                  <div class="reader-chapter-block" data-chapter-start="1">
                    <article class="reader-page-title">{titleHtml}</article>
                    <section class="reader-page-body {bodyClass}">{bodyHtml}</section>
                  </div>
                </section>
                """);
        }

        if (sb.Length == 0)
        {
            sb.Append("""
                <section class="chapter" id="ch-0">
                  <div class="reader-chapter-block" data-chapter-start="1">
                    <article class="reader-page-title">Chapter</article>
                    <section class="reader-page-body"><p class="manuscript-p">No chapters in this book yet.</p></section>
                  </div>
                </section>
                """);
        }

        return sb.ToString();
    }

    /// <summary>Maps interior style to formatter shell class on <c>body</c> (mirrors preview).</summary>
    public static string PreviewShellClass(string? interiorStyle)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(interiorStyle);
        return interior switch
        {
            "Classic" => "fmt-style-classic",
            "Modern" => "fmt-style-modern",
            "Minimalist" => "fmt-style-clean-minimalist",
            "ElegantTrade" => "fmt-style-elegant-trade",
            _ => "fmt-style-traditional"
        };
    }

    public static string PreviewInteriorWrapClass(string? interiorStyle)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(interiorStyle);
        return interior switch
        {
            "Classic" => "interior-classic",
            "Modern" => "interior-modern",
            "Minimalist" => "interior-minimalist",
            "ElegantTrade" => "interior-elegant-trade",
            _ => "interior-novel"
        };
    }

    public static string GoogleFontLinks() =>
        """
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
        <link href="https://fonts.googleapis.com/css2?family=Cormorant+Garamond:ital,wght@0,400;0,600;1,400&family=EB+Garamond:ital,wght@0,400;0,600;1,400&family=Inter:wght@400;600;700&family=Lato:wght@400;700&family=Merriweather:ital,wght@0,400;0,700;1,400&family=Playfair+Display:ital,wght@0,400;0,700;1,400&display=swap" rel="stylesheet" />
        """;
}
