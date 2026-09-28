using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

public class PdfImportTextNormalizerTests
{
    [Theory]
    [InlineData("S TARTUP THINKING", "STARTUP THINKING")]
    [InlineData("E VERY MOMENT IN BUSINESS", "EVERY MOMENT IN BUSINESS")]
    [InlineData("A S MATURE INDUSTRIES", "AS MATURE INDUSTRIES")]
    [InlineData("billion- dollar", "billion-dollar")]
    [InlineData("YOU ' VE", "YOU’VE")]
    [InlineData("CROSSING .", "CROSSING.")]
    [InlineData("P RINCE", "PRINCE")]
    public void Normalize_repairs_common_pdf_artifacts(string input, string expected)
    {
        Assert.Equal(expected, PdfImportTextNormalizer.Normalize(input));
    }

    [Fact]
    public void AppendLineToParagraph_reflows_hard_line_breaks()
    {
        var para = new System.Text.StringBuilder();
        PdfImportTextNormalizer.AppendLineToParagraph(para, "The cost of");
        PdfImportTextNormalizer.AppendLineToParagraph(para, "the product was high.");
        Assert.Equal("The cost of the product was high.", para.ToString());
    }

    [Fact]
    public void AppendLineToParagraph_dehyphenates_soft_wraps_but_keeps_compounds()
    {
        var soft = new System.Text.StringBuilder("exam-");
        PdfImportTextNormalizer.AppendLineToParagraph(soft, "ple");
        Assert.Equal("example", soft.ToString());

        var hard = new System.Text.StringBuilder("Jay-");
        PdfImportTextNormalizer.AppendLineToParagraph(hard, "Z");
        Assert.Contains("Jay", hard.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildChapterSectionsHtml_has_no_visible_tocmeasure_text()
    {
        var html = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(
            [new Models.DTO.ChapterDto { ChapterNumber = 1, Title = "Opening", Content = "<p>Hi</p>" }],
            BookManuscriptHtmlFormatter.CreateBaseContext("Book", null, null, null, "Author"),
            new Models.DTO.BookPdfExportOptions());
        Assert.Contains("toc-measure-marker", html, StringComparison.Ordinal);
        Assert.DoesNotContain("TOCMEASURE_", html, StringComparison.Ordinal);
    }
}
