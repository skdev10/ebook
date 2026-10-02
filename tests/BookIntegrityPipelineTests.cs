using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using EBookDashboard.Services.PdfExport;
using Xunit;

namespace EBookDashboard.Tests;

public class BookIntegrityPipelineTests
{
    private const string PowerLaw =
        "<p>most of the <em>companies they work with are by definition</em> average. " +
        "Most of the differences <em>that investors and entrepreneurs perceive</em> every day are between relative " +
        "<em>levels of success, not between exponential</em> dominance and failure. And since " +
        "<em>nobody wants to give up on an investment</em>, VCs usually spend even more " +
        "<em>time on the most problematic companies than</em> they do on the most obviously " +
        "<em>successful</em>.</p>";

    private const string Cosmology =
        "<p>the universe expanded by a factor of 10<sup>30</sup>—a million trillion trillion. As cosmogonic epochs came and went</p>";

    private const string Solar =
        "<p>cylindrical cells are only 1/π as efficient as flat ones—they simply don't receive as much direct sunlight</p>";

    private const string Copyright =
        "<p><span class=\"smallcaps\">CROWN</span> is a trademark and CROWN and the Rising Sun colophon are registered trademarks of Penguin Random House LLC.</p>";

    [Fact]
    public void Split_every_word_boundary_rejoins_to_the_original_text()
    {
        foreach (var html in new[] { PowerLaw, Cosmology, Solar, Copyright })
        {
            var original = HtmlParagraphSplitter.VisibleText(html);
            var words = HtmlParagraphSplitter.CountWords(html);
            Assert.True(words >= 2, original);
            for (var n = 1; n < words; n++)
            {
                var (left, right) = HtmlParagraphSplitter.SplitAtWord(html, n);
                var joined = HtmlParagraphSplitter.VisibleText(left) + HtmlParagraphSplitter.VisibleText(right);
                Assert.Equal(original, joined);
            }
        }
    }

    [Fact]
    public void Split_keeps_sup_and_em_on_the_half_that_owns_the_word()
    {
        var (left, right) = HtmlParagraphSplitter.SplitAtWord(Cosmology, 7);
        var both = left + right;
        Assert.Contains("<sup>", both, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30", both, StringComparison.Ordinal);
        Assert.Equal(HtmlParagraphSplitter.VisibleText(Cosmology), HtmlParagraphSplitter.VisibleText(left) + HtmlParagraphSplitter.VisibleText(right));
    }

    [Fact]
    public void Normalize_keeps_superscript_and_strips_calibre_junk()
    {
        var html = "<p class=\"calibre1\">factor of 10<sup>30</sup>—a million</p><p class=\"calibre_pb\">v3.1</p>";
        var clean = BookHtmlNormalizer.NormalizeFragment(html);
        Assert.Contains("<sup>30</sup>", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calibre", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v3.1", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeHtml_does_not_flatten_superscript()
    {
        var clean = BookManuscriptHtmlFormatter.SanitizeHtml(Cosmology);
        Assert.Contains("<sup>", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void Merge_heading_split_by_br_or_a_second_element()
    {
        var byBr = BookHtmlNormalizer.NormalizeFragment("<h1>If You Build It, Will They<br/>COME?</h1>");
        Assert.Contains("If You Build It, Will They COME?", BookIntegrityChecker.PlainFromHtml(byBr), StringComparison.Ordinal);
        Assert.DoesNotContain("<br", byBr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(byBr, "<h1", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);

        var byTwo = BookHtmlNormalizer.NormalizeFragment("<h1>If You Build It, Will They</h1><h2>COME?</h2><p>Body.</p>");
        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(byTwo, "<h", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
        Assert.Contains("COME?", BookIntegrityChecker.PlainFromHtml(byTwo), StringComparison.Ordinal);
        Assert.Contains("Body.", byTwo, StringComparison.Ordinal);
    }

    [Fact]
    public void Drop_cap_mark_keeps_short_word_and_the_space_after_it()
    {
        var html = BookHtmlNormalizer.MarkDropCapParagraph("<p>If even the most farsighted investors.</p>");
        Assert.Contains("drop-cap-start", html, StringComparison.Ordinal);
        Assert.Equal("If even the most farsighted investors.", BookIntegrityChecker.PlainFromHtml(html).Trim());

        var quoted = BookHtmlNormalizer.MarkDropCapParagraph("<p>“If even the most farsighted.</p>");
        Assert.Contains("“If even", BookIntegrityChecker.PlainFromHtml(quoted), StringComparison.Ordinal);
    }

    [Fact]
    public void Drop_cap_is_not_applied_to_index_or_credits()
    {
        Assert.False(BookHtmlNormalizer.AllowsDropCap("Index"));
        Assert.False(BookHtmlNormalizer.AllowsDropCap("Illustration Credits"));
        Assert.False(BookHtmlNormalizer.AllowsDropCap("Copyright"));
        Assert.False(BookHtmlNormalizer.AllowsDropCap("Acknowledgments"));
        Assert.True(BookHtmlNormalizer.AllowsDropCap("The Mechanics of Monopoly"));
        Assert.True(BookHtmlNormalizer.AllowsDropCap("Conclusion"));
        Assert.True(BookHtmlNormalizer.AllowsDropCap("Preface"));
    }

    [Fact]
    public void Back_matter_is_not_labeled_chapter_n()
    {
        foreach (var title in new[] { "Conclusion", "Acknowledgments", "Illustration Credits", "Index" })
        {
            var heading = BookChapterExportHelper.GetPreviewStyleHeading(title, storageChapterNumber: 15, narrativeOrdinal: 15);
            Assert.DoesNotContain("Chapter", heading, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(title.Split(' ')[0], heading, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Front_matter_dedupe_keeps_one_copyright_and_drops_source_title_and_toc()
    {
        var chapters = new List<ChapterDto>
        {
            new() { ChapterNumber = 1, Title = "Zero to One", Content = "<p>Zero to One</p><p>Peter Thiel</p>" },
            new() { ChapterNumber = 2, Title = "Copyright", Content = "<p>Copyright © 2014 Peter Thiel. All rights reserved. ISBN 978-0-8041-3929-8.</p>" },
            new() { ChapterNumber = 3, Title = "Table of Contents", Content = "<p>Preface</p><p>Chapter 1</p>" },
            new() { ChapterNumber = 4, Title = "The Challenge of the Future", Content = "<p>Every moment in business happens only once.</p>" },
            new() { ChapterNumber = 15, Title = "Conclusion", Content = "<p>If even the most farsighted.</p>" }
        };

        var kept = BookHtmlNormalizer.OmitDuplicateFrontMatter(chapters, keepOriginalCopyright: true, bookTitle: "Zero to One");
        Assert.DoesNotContain(kept, c => BookHtmlNormalizer.IsTitlePage(c, "zero to one"));
        Assert.DoesNotContain(kept, c => BookHtmlNormalizer.IsTableOfContents(c));
        Assert.Contains(kept, c => c.Title == "Copyright");
        Assert.Contains(kept, c => c.Title == "Conclusion");

        var opt = new BookPdfExportOptions { KeepOriginalCopyrightPage = true, InteriorStyle = "Novel" };
        var ph = BookManuscriptHtmlFormatter.CreateBaseContext("Zero to One", null, null, null, "saad");
        var html = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(kept, ph, opt);
        Assert.DoesNotContain("Chapter 15", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Chapter 16<", html, StringComparison.Ordinal);
        Assert.Contains("Conclusion", html, StringComparison.Ordinal);
        Assert.Contains("has-drop-cap", html, StringComparison.Ordinal);
        Assert.Contains("If even", html, StringComparison.Ordinal);
        var indexHtml = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(
            [new ChapterDto { ChapterNumber = 18, Title = "Index", Content = "<p>A</p><p>average, 12</p>" }],
            ph, opt);
        Assert.DoesNotContain("has-drop-cap", indexHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Chapter 18", indexHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Integrity_passes_the_zero_to_one_paragraphs_and_fails_the_reordered_output()
    {
        var source = PowerLaw + Cosmology + Solar + Copyright;
        var pass = BookIntegrityChecker.CheckHtml(source, source);
        Assert.True(pass.Passed, string.Join("; ", pass.Hits.Select(h => h.Kind + " " + h.Context)));
        Assert.Equal(0, pass.Deletions);
        Assert.Equal(0, pass.Reorders);
        Assert.True(pass.Similarity >= BookIntegrityChecker.PassSimilarity);

        var broken = "<p>most of the average. Most of the differences every day are between relative dominance and failure. " +
                     "companies they work with are by definition that investors and entrepreneurs perceive</p>";
        var fail = BookIntegrityChecker.CheckHtml(PowerLaw, broken);
        Assert.False(fail.Passed);
        Assert.False(fail.StructureOk);
        Assert.True(fail.Similarity < 0.99);
        Assert.True(fail.Deletions + fail.Reorders > 0);
    }

    [Fact]
    public void Integrity_handles_a_long_text_and_an_image_heavy_fragment()
    {
        var paras = string.Concat(Enumerable.Range(1, 420).Select(i => $"<p>Paragraph {i} stays in order across the whole book.</p>"));
        var textReport = BookIntegrityChecker.CheckHtml(paras, paras);
        Assert.True(textReport.Passed);
        Assert.True(textReport.SourceWords > 2000);

        var images = string.Concat(Enumerable.Range(1, 50).Select(i =>
        {
            var ext = (i % 4) switch { 0 => "svg", 1 => "png", 2 => "webp", _ => "jpg" };
            return $"<figure><img src=\"/uploads/fig-{i}.{ext}\" alt=\"\" /></figure>";
        }));
        var imageReport = BookIntegrityChecker.CheckHtml(images + "<p>Captions follow each figure.</p>", images + "<p>Captions follow each figure.</p>");
        Assert.Equal(50, imageReport.SourceImages);
        Assert.Equal(50, imageReport.OutputImages);
        Assert.True(imageReport.ImagesMatch);
    }

    [Fact]
    public void Document_author_comes_from_opf_creator_not_an_account_name()
    {
        var name = DocumentAuthorResolver.ReadCreatorFromOpf(
            """<metadata><dc:creator id="creator">Peter Thiel</dc:creator></metadata>""");
        Assert.Equal("Peter Thiel", name);
    }

    [Fact]
    public void Heic_upload_is_rejected_with_a_convert_message()
    {
        Assert.True(ChapterDocumentImportService.IsHeicUpload("photo.HEIC", "application/octet-stream"));
        Assert.True(ChapterDocumentImportService.IsHeicUpload("scan.bin", "image/heif"));
        Assert.Contains("Convert to JPG or PNG", ChapterDocumentImportService.HeicUploadMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Flattened_html_is_detected_when_inline_tags_disappear()
    {
        Assert.True(BookOriginalReimportService.SavedHtmlDroppedInlineTags(
            "<p>10<sup>30</sup></p>",
            "<p>1030</p>"));
        Assert.False(BookOriginalReimportService.SavedHtmlDroppedInlineTags(
            "<p>10<sup>30</sup></p>",
            "<p>10<sup>30</sup></p>"));
    }

    [Fact]
    public void Print_html_uses_one_margin_box_and_the_text_block_for_images()
    {
        var html = EBookDashboard.Services.PdfExport.BookPreviewPrintHtmlBuilder.Build(
            "Zero to One", "Peter Thiel", "", null, null, false, "", "", "<p>Body</p>",
            new BookPdfExportOptions { InteriorStyle = "Novel", TrimWidthIn = 6, TrimHeightIn = 9 },
            "6in 9in", "tpl-novel", "fmt-style-traditional", "interior-novel", null);
        Assert.Contains("--margin-inside: 0.75in", html, StringComparison.Ordinal);
        Assert.Contains("--margin-outside: 0.5in", html, StringComparison.Ordinal);
        Assert.Contains("--margin-top: 0.6in", html, StringComparison.Ordinal);
        Assert.Contains("--margin-bottom: 0.6in", html, StringComparison.Ordinal);
        Assert.Contains("--page-height: 9in", html, StringComparison.Ordinal);
        Assert.Contains("--text-block-h: calc(var(--page-height) - var(--margin-top) - var(--margin-bottom))", html, StringComparison.Ordinal);
        Assert.Contains("@page :left", html, StringComparison.Ordinal);
        Assert.Contains("margin-left: 0.5in", html, StringComparison.Ordinal);
        Assert.Contains("margin-right: 0.75in", html, StringComparison.Ordinal);
        Assert.Contains("@page :right", html, StringComparison.Ordinal);
        Assert.DoesNotContain("18mm 0 16mm 0", html, StringComparison.Ordinal);
        Assert.Contains("max-height: var(--text-block-h)", html, StringComparison.Ordinal);
        Assert.Contains("padding: 0 !important", html, StringComparison.Ordinal);
        Assert.Contains("width: auto", html, StringComparison.Ordinal);
        Assert.Contains(".copyright-page { break-before: left", html, StringComparison.Ordinal);
        Assert.Contains(".toc-page { break-before: right", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Source_title_image_is_page_one_and_the_source_contents_list_is_removed()
    {
        var chapters = new List<ChapterDto>
        {
            new()
            {
                ChapterNumber = 1,
                Title = "Title Page",
                Content = "<h1>Title Page</h1><figure><img src=\"data:image/png;base64,AAAA\" alt=\"plate\"></figure>"
            },
            new()
            {
                ChapterNumber = 2,
                Title = "Copyright",
                Content = "<p>Copyright © 2014. All rights reserved.</p><p>Contents</p><p>Preface: Zero to One</p><p>1 The Challenge of the Future</p><p>2 Party Like It's 1999</p><p>About the Authors</p><p>ISBN 978-0-8041-3929-8.</p>"
            },
            new() { ChapterNumber = 3, Title = "Preface", Content = "<p>The preface body stays.</p>" },
            new() { ChapterNumber = 4, Title = "1 The Challenge of the Future", Content = "<p>Every moment in business happens only once.</p>" }
        };

        var art = BookHtmlNormalizer.PullSourceTitlePage(chapters, useSourceTitlePage: true);
        Assert.Contains("class=\"title-page source-title-page", art, StringComparison.Ordinal);
        Assert.Contains("<img", art, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Title Page", art, StringComparison.Ordinal);
        Assert.DoesNotContain(chapters, c => c.Title == "Title Page");

        var copyright = BookHtmlNormalizer.PullCopyrightPage(chapters, keepOriginal: true);
        Assert.Contains("copyright-page", copyright, StringComparison.Ordinal);
        Assert.Contains("All rights reserved", copyright, StringComparison.Ordinal);
        Assert.Contains("ISBN", copyright, StringComparison.Ordinal);
        Assert.DoesNotContain("The Challenge of the Future", copyright, StringComparison.Ordinal);
        Assert.DoesNotContain(chapters, c => c.Title == "Copyright");
        Assert.Contains(chapters, c => c.Title == "Preface");

        var combined = new List<ChapterDto>
        {
            new()
            {
                ChapterNumber = 1,
                Title = "Title Page",
                Content = "<figure><img src=\"data:image/png;base64,AAAA\" alt=\"plate\"></figure><h2>Copyright © 2014 by Peter Thiel</h2><p>All rights reserved. Crown Business is a trademark and CROWN and the Rising Sun colophon are registered trademarks of Random House LLC.</p><p>Contents</p><p>Preface: Zero to One</p><p>1 The Challenge of the Future</p><p>2 Party Like It's 1999</p><p>About the Authors</p><p>1. New business enterprises.</p>"
            },
            new() { ChapterNumber = 2, Title = "Preface", Content = "<p>The preface body stays.</p>" }
        };
        BookHtmlNormalizer.TakePrintedFrontMatter(combined, true, true, out var combinedTitle, out var combinedCopyright);
        Assert.Contains("<img", combinedTitle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Title Page", combinedTitle, StringComparison.Ordinal);
        Assert.Contains("trademarks of Random House", combinedCopyright, StringComparison.Ordinal);
        Assert.Contains("1. New business enterprises.", combinedCopyright, StringComparison.Ordinal);
        Assert.DoesNotContain("The Challenge of the Future", combinedCopyright, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", combinedCopyright, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(combined, c => c.Title == "Preface");
    }

    [Fact]
    public void Chapter_opener_shows_the_number_once()
    {
        var eyebrow = InteriorPageMarkup.BuildFormatterChapterTitleHtml(
            "2 Party Like It's 1999", narrativeOrdinal: 2, interiorStyle: "Novel", chapterNumberStyle: "Eyebrow");
        Assert.Contains(">Chapter 2<", eyebrow, StringComparison.Ordinal);
        Assert.Contains("Party Like It", eyebrow, StringComparison.Ordinal);
        Assert.Contains("1999<", eyebrow, StringComparison.Ordinal);
        Assert.DoesNotContain("1999 Party", eyebrow, StringComparison.Ordinal);
        Assert.DoesNotContain(">2 Party", eyebrow, StringComparison.Ordinal);

        var inline = InteriorPageMarkup.BuildFormatterChapterTitleHtml(
            "Party Like It's 1999", narrativeOrdinal: 2, interiorStyle: "Novel", chapterNumberStyle: "Inline");
        Assert.DoesNotContain("fmt-ch-eyebrow", inline, StringComparison.Ordinal);
        Assert.Contains(">2 Party Like It", inline, StringComparison.Ordinal);
        Assert.Contains("1999<", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_mismatch_names_the_plate_that_is_not_in_the_pdf()
    {
        var pdf = Path.Combine(FindRepoForAudit(), "test-output", "zero-to-one-rerun.pdf");
        if (!File.Exists(pdf))
            return;
        var html = """
            <img alt="illustration page 3" data-px="1151x1733" data-source-page="3" />
            <img alt="missing-plate.png" data-px="9x9" data-source-page="99" />
            """;
        var missing = EBookDashboard.Services.PdfExport.PrintImageAudit.MissingFromPdf(html, File.ReadAllBytes(pdf));
        Assert.Contains(missing, name => name.Contains("missing-plate.png", StringComparison.Ordinal));
    }

    private static string FindRepoForAudit()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "newEbook.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return @"c:\Users\Hp\Desktop\newEbook";
    }

    [Fact]
    public void Missing_local_image_fails_before_print_and_names_the_file()
    {
        var ex = Assert.Throws<PdfImageLoadException>(() =>
            ChromiumPdfExporter.InlineLocalImageSources("""<img src="/uploads/plates/missing-plate.png" alt="">"""));
        Assert.Contains("missing-plate.png", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_book_html_is_split_into_chapter_chunks()
    {
        var sections = string.Concat(Enumerable.Range(1, 30).Select(i =>
            $"""<section class="chapter" id="ch-{i}"><p>{new string('a', 8000)}</p></section>"""));
        var html = """<html><head></head><body><div class="title-page">Title</div><div class="manuscript-root">"""
                   + sections + "</div></body></html>";
        Assert.True(PrintHtmlChunker.ShouldChunk(html));
        var chunks = PrintHtmlChunker.Split(html);
        Assert.True(chunks.Count > 1);
        Assert.Contains("title-page", chunks[0], StringComparison.Ordinal);
        Assert.DoesNotContain("title-page", chunks[1], StringComparison.Ordinal);
        Assert.Contains("id=\"ch-30\"", chunks[^1], StringComparison.Ordinal);
    }
}
