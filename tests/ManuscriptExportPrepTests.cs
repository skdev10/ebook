using EBookDashboard.Models.DTO;
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
    public void BuildChapterSectionsHtml_uses_preview_dom_structure()
    {
        var html = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(
            [new ChapterDto { ChapterNumber = 1, Title = "Scene One", Content = "Hello world." }],
            BookManuscriptHtmlFormatter.CreateBaseContext("Book", null, null, null, "Author"),
            new BookPdfExportOptions { InteriorStyle = "Classic", TextSize = "Medium", LineSpacing = "1.6" });

        Assert.Contains("reader-page-title", html, StringComparison.Ordinal);
        Assert.Contains("reader-page-body", html, StringComparison.Ordinal);
        Assert.Contains("reader-chapter-block", html, StringComparison.Ordinal);
        Assert.DoesNotContain("chapter-heading", html, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeHtml_preserves_img_and_inline_styles()
    {
        var html = """<p style="color:#b91c1c;font-size:18px">Red text</p><img src="https://cdn.example.com/a.png" alt="fig" style="width:80%" />""";
        var clean = BookManuscriptHtmlFormatter.SanitizeHtml(html);
        Assert.Contains("color:#b91c1c", clean, StringComparison.Ordinal);
        Assert.Contains("<img", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cdn.example.com", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPdfThemeCss_includes_formatter_interior_shell_rules()
    {
        var css = InteriorExportTheme.BuildPdfThemeCss(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains("interior-novel", css, StringComparison.Ordinal);
        Assert.Contains("Merriweather", css, StringComparison.Ordinal);
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
