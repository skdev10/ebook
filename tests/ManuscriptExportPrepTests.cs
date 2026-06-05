using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

public class ManuscriptExportPrepTests
{
    [Fact]
    public void GetPreviewStyleHeading_uses_display_title_without_chapter_prefix()
    {
        var heading = BookChapterExportHelper.GetPreviewStyleHeading(
            "Chapter 1: Shattered Timer over the Strait",
            storageChapterNumber: 1,
            narrativeOrdinal: 1);

        Assert.Equal("Shattered Timer over the Strait", heading);
    }

    [Fact]
    public void StripRedundantChapterOpenings_removes_ai_writer_banner_and_duplicate_title()
    {
        var html = """
            <h4 class="manuscript-chapter-heading">CHAPTER 1: THE CHOKEHOLD PARADIGM</h4>
            <hr class="manuscript-hr" />
            <h2 class="manuscript-h2">Shattered Timer over the Strait</h2>
            <p class="manuscript-p">Opening paragraph.</p>
            """;

        var cleaned = BookManuscriptHtmlFormatter.StripRedundantChapterOpenings(
            html,
            "Shattered Timer over the Strait");

        Assert.DoesNotContain("CHOKEHOLD", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manuscript-chapter-heading", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("<p", cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPdfThemeCss_classic_includes_tpl_classic_marker()
    {
        var css = InteriorExportTheme.BuildPdfThemeCss(new Models.DTO.BookPdfExportOptions
        {
            InteriorStyle = "Classic",
            TextSize = "Medium",
            LineSpacing = "1.6"
        });

        Assert.Contains("tpl-classic", css, StringComparison.Ordinal);
        Assert.Contains("small-caps", css, StringComparison.Ordinal);
    }
}
