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
    public void ResolveDefaultPageBackground_elegant_trade_matches_formatter_sheet()
    {
        var bg = InteriorExportTheme.ResolveDefaultPageBackground("ElegantTrade");
        Assert.Equal("#fcf9f3", bg, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPdfThemeCss_uses_custom_page_background_override()
    {
        var css = InteriorExportTheme.BuildPdfThemeCss(new BookPdfExportOptions
        {
            InteriorStyle = "Classic",
            PageBackgroundColor = "#f0e6d2"
        });
        Assert.Contains("--page-bg: #f0e6d2", css, StringComparison.Ordinal);
        Assert.Contains("background-color: var(--page-bg) !important", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPdfThemeCss_includes_formatter_interior_shell_rules()
    {
        var css = InteriorExportTheme.BuildPdfThemeCss(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains("interior-novel", css, StringComparison.Ordinal);
        Assert.Contains("Merriweather", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFormatterSyncCss_uses_kdp_inch_padding_and_text_measure()
    {
        var css = InteriorLayoutTokens.BuildFormatterSyncCss(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains("--ilt-pad-top: 0.85in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-pad-right: 0.55in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-pad-bottom: 0.8in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-pad-left: 0.75in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-text-max: 4.2in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-chapter-drop: 26mm", css, StringComparison.Ordinal);
        Assert.Contains(".toc-leader", css, StringComparison.Ordinal);
        Assert.Contains("book-page-running-head", css, StringComparison.Ordinal);
        Assert.Contains("fmt-mat-novel", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_and_pdf_css_share_identical_layout_custom_properties()
    {
        var opt = new BookPdfExportOptions
        {
            InteriorStyle = "Novel",
            TextSize = "Medium",
            LineSpacing = "1.8",
            PageBackgroundColor = "#fffdf8"
        };
        var previewCss = InteriorLayoutTokens.BuildFormatterSyncCss(opt);
        var pdfCss = InteriorExportTheme.BuildPdfThemeCss(opt);
        var props = InteriorLayoutTokens.BuildCssCustomProperties(opt);
        foreach (var token in new[]
        {
            "--ilt-pad-top:", "--ilt-pad-right:", "--ilt-pad-bottom:", "--ilt-pad-left:",
            "--ilt-text-max:", "--ilt-chapter-drop:", "--ilt-body-lh:", "--ilt-body-pt:",
            "--ilt-para-space:", "--ilt-text-indent:"
        })
        {
            Assert.Contains(token, previewCss, StringComparison.Ordinal);
            Assert.Contains(token, pdfCss, StringComparison.Ordinal);
            Assert.Contains(token, props, StringComparison.Ordinal);
        }
        Assert.Contains("--ilt-body-lh: 1.8", props, StringComparison.Ordinal);
        Assert.Contains("font-size:var(--ilt-body-pt) !important", pdfCss, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardLayoutTokens_emits_unified_page_background_and_preview_pane_width()
    {
        var css = DashboardLayoutTokens.BuildCssCustomProperties();
        Assert.Contains("--dash-page-bg: #f5f7f9", css, StringComparison.Ordinal);
        Assert.Contains("--dash-preview-pane-w: 68%", css, StringComparison.Ordinal);
        Assert.Contains("--dash-flow-gap: 2.75rem", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(24.0)]
    [InlineData(26.0)]
    [InlineData(28.0)]
    public void Chapter_drop_mm_converts_to_css_length(double mm)
    {
        Assert.Equal($"{mm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}mm",
            InteriorSpacingTheme.Mm(mm));
    }

    [Fact]
    public void NormalizeForManuscript_strips_status_data_string_wrapper()
    {
        var raw = """{"status":"success","data":"<p class=\"manuscript-p\">Drop zone secured.</p>"}""";
        var clean = ChapterContentNormalizer.NormalizeForManuscript(raw);
        Assert.DoesNotContain("status", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"data\"", clean, StringComparison.Ordinal);
        Assert.Contains("Drop zone secured", clean, StringComparison.Ordinal);
        Assert.Contains("manuscript-p", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeForManuscript_strips_status_data_content_object_wrapper()
    {
        var raw = """{"status":"success","data":{"content":"<p>Battle royale begins.</p>"}}""";
        var clean = ChapterContentNormalizer.NormalizeForManuscript(raw);
        Assert.Equal("<p>Battle royale begins.</p>", clean);
    }

    [Fact]
    public void PrepareChapterBodyForExport_never_emits_json_wrapper()
    {
        var raw = """{"status":"success","data":"<p>Erangel fog rolls in.</p>"}""";
        var ph = BookManuscriptHtmlFormatter.CreateBaseContext("PUBG Guide", null, null, "Gaming", "Author");
        var html = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(raw, ph, "Chapter 1");
        Assert.DoesNotContain("{\"status\"", html, StringComparison.Ordinal);
        Assert.Contains("Erangel fog", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildChapterSectionsHtml_includes_book_preview_sheet()
    {
        var html = InteriorPrintDocumentBuilder.BuildChapterSectionsHtml(
            [new ChapterDto { ChapterNumber = 1, Title = "Scene One", Content = "Hello world." }],
            BookManuscriptHtmlFormatter.CreateBaseContext("Book", null, null, null, "Author"),
            new BookPdfExportOptions { InteriorStyle = "Novel", TextSize = "Medium", LineSpacing = "1.6" });

        Assert.Contains("book-preview-sheet", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPdfThemeCss_includes_heading_keep_with_next_rules()
    {
        var css = InteriorExportTheme.BuildPdfThemeCss(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains("page-break-after: avoid", css, StringComparison.Ordinal);
        Assert.Contains("page-break-before: avoid", css, StringComparison.Ordinal);
        Assert.Contains(".reader-page-body h2 + *", css, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("Novel", "Small", "1.4", "10.5pt", "1.4")]
    [InlineData("Novel", "Medium", "1.6", "12pt", "1.6")]
    [InlineData("Novel", "Large", "2", "15pt", "2")]
    [InlineData("Classic", "Medium", "1.8", "12.75pt", "1.8")]
    [InlineData("Classic", "Large", "1.6", "14.25pt", "1.6")]
    [InlineData("Minimalist", "Small", "1.6", "10.5pt", "1.6")]
    [InlineData("ElegantTrade", "Medium", "1.4", "12pt", "1.4")]
    [InlineData("ElegantTrade", "Large", "2", "14.25pt", "2")]
    public void BuildPdfThemeCss_maps_text_size_and_line_spacing_to_pdf_tokens(
        string interiorStyle, string textSize, string lineSpacing, string expectedPt, string expectedLh)
    {
        var css = InteriorExportTheme.BuildPdfThemeCss(new BookPdfExportOptions
        {
            InteriorStyle = interiorStyle,
            TextSize = textSize,
            LineSpacing = lineSpacing
        });

        Assert.Contains($"--ilt-body-pt: {expectedPt}", css, StringComparison.Ordinal);
        Assert.Contains($"--ilt-body-lh: {expectedLh}", css, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Traditional", "Novel")]
    [InlineData("Fine book", "Classic")]
    [InlineData("Clean", "Minimalist")]
    [InlineData("POD", "ElegantTrade")]
    [InlineData("Elegant trade", "ElegantTrade")]
    [InlineData("", "Novel")]
    public void NormalizeInteriorStyle_maps_ui_badge_labels_to_canonical_styles(string uiValue, string expected)
    {
        Assert.Equal(expected, InteriorExportTheme.NormalizeInteriorStyle(uiValue));
    }

    [Fact]
    public void ApplyRequestOverrides_request_values_win_over_db_defaults()
    {
        var opt = new BookPdfExportOptions { InteriorStyle = "Novel", TextSize = "Medium", LineSpacing = "1.6" };
        opt.ApplyRequestOverrides(new ExportBookPdfRequest
        {
            BookId = 1,
            InteriorStyle = "Classic",
            TextSize = "Large",
            LineSpacing = "2"
        });

        Assert.Equal("Classic", opt.InteriorStyle);
        Assert.Equal("Large", opt.TextSize);
        Assert.Equal("2", opt.LineSpacing);
    }

    [Theory]
    [InlineData("Novel")]
    [InlineData("Classic")]
    [InlineData("Minimalist")]
    [InlineData("ElegantTrade")]
    public void Interior_specs_keep_binding_gutter_wider_than_outside_edge(string style)
    {
        static double Inches(string v) =>
            double.Parse(v.Replace("in", ""), System.Globalization.CultureInfo.InvariantCulture);

        var spec = InteriorLayoutTokens.Get(style);
        Assert.True(Inches(spec.SheetPadding.Left) > Inches(spec.SheetPadding.Right),
            $"{style}: left (gutter) padding should exceed right padding.");

        var m = InteriorLayoutTokens.KdpTrim6x9Default;
        Assert.True(Inches(m.Inside) > Inches(m.Outside), "Print margin box: inside should exceed outside.");
    }

    [Fact]
    public void BookPdfPlatformLayout_ebook_uses_6x9_kdp_trim_like_preview()
    {
        var spec = BookPdfPlatformLayout.Resolve(new BookPdfExportOptions { Format = "Ebook" });
        Assert.Contains("6in", spec.PageSizeCss, StringComparison.Ordinal);
        Assert.False(spec.UseBuiltInFormat);
        Assert.Equal(InteriorLayoutTokens.KdpTrim6x9Default.Top, spec.MarginTop);
        Assert.Equal(InteriorLayoutTokens.KdpTrim6x9Default.Inside, spec.MarginLeft);
    }

    [Fact]
    public void BuildTocHtml_uses_professional_print_structure()
    {
        var ph = BookManuscriptHtmlFormatter.CreateBaseContext("Test Book", null, null, "Fiction", "Author");
        var chapters = new List<ChapterDto>
        {
            new() { ChapterNumber = 1, Title = "Opening", Content = "<p class=\"manuscript-p\">Body</p><h2 class=\"manuscript-h2\">Scene</h2>" }
        };
        var html = InteriorFrontMatterBuilder.BuildTocHtml(chapters, ph);

        Assert.Contains("toc-block", html, StringComparison.Ordinal);
        Assert.Contains("toc-leader", html, StringComparison.Ordinal);
        Assert.Contains("toc-sub-text", html, StringComparison.Ordinal);
        Assert.DoesNotContain("toc-hint", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Heading:", html, StringComparison.Ordinal);
    }
}
