using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Contains("page-header", html, StringComparison.Ordinal);
        Assert.Contains("page-body", html, StringComparison.Ordinal);
        Assert.Contains("book-page-running-head", html, StringComparison.Ordinal);
        Assert.Contains("TOCMEASURE_1_END", html, StringComparison.Ordinal);
        Assert.DoesNotContain("chapter-heading", html, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizeInteriorStyle_maps_formatter_aliases()
    {
        Assert.Equal("ElegantTradePOD", InteriorExportTheme.NormalizeInteriorStyle("PODElegantTrade"));
        Assert.Equal("Minimalist", InteriorExportTheme.NormalizeInteriorStyle("CleanMinimalist"));
    }

    [Theory]
    [InlineData("PODElegantTrade", "Medium", "1.6", "11", "1.6")]
    [InlineData("CleanMinimalist", "Small", "1.4", "10", "1.4")]
    public void InteriorStyle_textSize_lineSpacing_change_pdf_css_tokens(
        string style, string textSize, string lineSpacing, string expectedPt, string expectedLh)
    {
        var opt = new BookPdfExportOptions
        {
            InteriorStyle = style,
            TextSize = textSize,
            LineSpacing = lineSpacing
        };
        var props = InteriorLayoutTokens.BuildCssCustomProperties(opt);
        Assert.Contains(FormattableString.Invariant($"--ilt-body-pt: {expectedPt}pt"), props, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"--ilt-body-lh: {expectedLh}"), props, StringComparison.Ordinal);
        var css = InteriorExportTheme.BuildPdfThemeCss(opt);
        Assert.Contains("page-header", css, StringComparison.Ordinal);
        Assert.Contains(InteriorSpacingTheme.PageHeaderBodyGapCss, css, StringComparison.Ordinal);
        Assert.Contains("export-meta", css, StringComparison.Ordinal);
        Assert.Contains("display: none", css, StringComparison.Ordinal);
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
        Assert.Equal("#fdfbf7", bg, StringComparer.OrdinalIgnoreCase);
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
        Assert.Contains("Palatino", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFormatterSyncCss_uses_kdp_inch_padding_and_text_measure()
    {
        var css = InteriorLayoutTokens.BuildFormatterSyncCss(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains("--ilt-pad-top: 0.625in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-pad-right: 0.625in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-pad-bottom: 0.875in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-pad-left: 0.8125in", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-text-max: 100%", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-chapter-drop: 1.25in", css, StringComparison.Ordinal);
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
    public void BuildSharedReaderLayoutCss_adds_running_head_gap_on_print_page_fragments()
    {
        var css = InteriorLayoutTokens.BuildSharedReaderLayoutCss();
        Assert.Contains("box-decoration-break", css, StringComparison.Ordinal);
        Assert.Contains("--ilt-running-head-gap-below", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCssCustomProperties_includes_running_head_gap_below()
    {
        var props = InteriorLayoutTokens.BuildCssCustomProperties(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains("--ilt-running-head-gap-below:", props, StringComparison.Ordinal);
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
    [InlineData("Novel", "Small", "1.4", "10pt", "1.4")]
    [InlineData("Novel", "Medium", "1.6", "11pt", "1.6")]
    [InlineData("Novel", "Large", "2", "12pt", "2")]
    [InlineData("Classic", "Medium", "1.8", "17pt", "1.8")]
    [InlineData("Classic", "Large", "1.6", "19pt", "1.6")]
    [InlineData("Minimalist", "Small", "1.6", "10pt", "1.6")]
    [InlineData("ElegantTrade", "Medium", "1.4", "11pt", "1.4")]
    [InlineData("ElegantTrade", "Large", "2", "12pt", "2")]
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
    [InlineData("Traditional", "Traditional")]
    [InlineData("Fine book", "FineBook")]
    [InlineData("Clean", "Clean")]
    [InlineData("POD", "POD")]
    [InlineData("Elegant trade", "ElegantTrade")]
    [InlineData("Elegant trade (POD)", "ElegantTradePOD")]
    [InlineData("Contemporary", "Contemporary")]
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
    public void BookPdfPlatformLayout_ebook_uses_6x9_trim_with_zero_chromium_margins()
    {
        // 6×9 interior styles (Modern/Clean/FineBook/POD) keep the 6×9 trim with zero Chromium
        // margins (per-style insets live in the sheet padding). Trim now follows the interior style.
        var spec = BookPdfPlatformLayout.Resolve(new BookPdfExportOptions { Format = "Ebook", InteriorStyle = "Modern" });
        Assert.Contains("6in", spec.PageSizeCss, StringComparison.Ordinal);
        Assert.False(spec.UseBuiltInFormat);
        Assert.Equal("0", spec.MarginTop);
        Assert.Equal("0", spec.MarginBottom);
        Assert.Equal("0", spec.MarginLeft);
        Assert.Equal("0", spec.MarginRight);

        // Novel maps to the 5×8 mass-market trim so the PDF shape matches the per-style preview.
        var novel = BookPdfPlatformLayout.Resolve(new BookPdfExportOptions { Format = "Ebook", InteriorStyle = "Novel" });
        Assert.Contains("5in", novel.PageSizeCss, StringComparison.Ordinal);
        Assert.Contains("8in", novel.PageSizeCss, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFormatterInteriorCss_targets_formatter_preview_and_pdf()
    {
        var css = InteriorExportTheme.BuildFormatterInteriorCss();
        Assert.Contains("#book-formatter-root #paginatedReaderShell.interior-elegant-trade .reader-page-body", css, StringComparison.Ordinal);
        Assert.Contains(".book-pdf-body.interior-elegant-trade .reader-page-body", css, StringComparison.Ordinal);
        Assert.Contains("EB Garamond", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPerInteriorCss_formatter_wrap_keeps_kdp_page_padding()
    {
        var css = InteriorLayoutTokens.BuildPerInteriorCss("ElegantTrade");
        Assert.Contains("#book-formatter-root #paginatedReaderShell.interior-elegant-trade .book-page-content-wrap", css, StringComparison.Ordinal);
        Assert.Contains("0.7in", css, StringComparison.Ordinal);
        Assert.Contains("0.62in", css, StringComparison.Ordinal);
        Assert.DoesNotContain(".book-page-content-wrap, .book-pdf-body.interior-elegant-trade .book-preview-sheet { padding: 0 !important; }", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPdfThemeCss_keeps_sheet_padding_and_in_content_running_head()
    {
        var css = InteriorExportTheme.BuildPdfThemeCss(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains(".book-pdf-body .book-preview-sheet > .page-header", css, StringComparison.Ordinal);
        Assert.Contains(InteriorSpacingTheme.PageHeaderBodyGapCss, css, StringComparison.Ordinal);
        Assert.DoesNotContain("padding: 0 !important", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Pdf_export_chromium_margins_reserve_running_head_band_in_html_padding()
    {
        static double Inches(string v) =>
            double.Parse(v.Replace("in", ""), System.Globalization.CultureInfo.InvariantCulture);

        var top = Inches(InteriorLayoutTokens.PdfExportChromiumMargins.Top);
        Assert.True(top >= 1.1, $"Reference top inset should include pad + running head; got {top:0.####}in");
        Assert.True(Inches(InteriorLayoutTokens.PdfExportChromiumMargins.Inside) >= 0.8);
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

    [Fact]
    public void BuildTocHtml_includes_measured_page_numbers()
    {
        var ph = BookManuscriptHtmlFormatter.CreateBaseContext("Test Book", null, null, "Fiction", "Author");
        var chapters = new List<ChapterDto>
        {
            new() { ChapterNumber = 1, Title = "Opening", Content = "<p class=\"manuscript-p\">Body</p>" },
            new() { ChapterNumber = 2, Title = "Next", Content = "<p class=\"manuscript-p\">More</p>" }
        };
        var pages = new List<int> { 7, 19 };
        var html = InteriorFrontMatterBuilder.BuildTocHtml(chapters, ph, pages);

        Assert.Equal(2, pages.Count);
        Assert.Contains(">7</span>", html, StringComparison.Ordinal);
        Assert.Contains(">19</span>", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#ch-1\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("…", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildTocHtml_pdfTargetCounters_emits_counter_spans_without_baked_numbers()
    {
        var ph = BookManuscriptHtmlFormatter.CreateBaseContext("Test Book", null, null, "Fiction", "Author");
        var chapters = new List<ChapterDto>
        {
            new() { ChapterNumber = 1, Title = "Opening", Content = "<p class=\"manuscript-p\">Body</p>" }
        };
        var html = InteriorFrontMatterBuilder.BuildTocHtml(chapters, ph, pdfTargetCounters: true);

        Assert.Contains("toc-page-ref--counter", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#ch-1\" class=\"toc-page-ref toc-page-ref--counter\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">5</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("…", html, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPdfThemeCss_includes_toc_target_counter_rules()
    {
        var css = InteriorExportTheme.BuildPdfThemeCss(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains("target-counter", css, StringComparison.Ordinal);
        Assert.Contains("toc-page-ref--counter", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAiWriterThemeBridgeCss_sets_body_and_heading_color_variables()
    {
        var css = InteriorExportTheme.BuildAiWriterThemeBridgeCss(new BookPdfExportOptions { InteriorStyle = "Novel" });
        Assert.Contains("--body-color:", css, StringComparison.Ordinal);
        Assert.Contains("--heading-color:", css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFormatterInteriorCss_uses_theme_color_variables()
    {
        var css = InteriorExportTheme.BuildFormatterInteriorCss();
        Assert.Contains("var(--heading-color", css, StringComparison.Ordinal);
        Assert.Contains("var(--body-color", css, StringComparison.Ordinal);
    }


    [Fact]
    public void BookFormattingSettings_round_trips_export_options()
    {
        var opt = new BookPdfExportOptions
        {
            InteriorStyle = "Classic",
            TextSize = "Large",
            LineSpacing = "1.8",
            Format = "Paperback",
            IncludeCoverPage = false,
            PageBackgroundColor = "#faf0e6"
        };
        var settings = BookFormattingSettings.FromExportOptions(opt);
        var roundTrip = settings.ToExportOptions();

        Assert.Equal("Classic", roundTrip.InteriorStyle);
        Assert.Equal("Large", roundTrip.TextSize);
        Assert.Equal("1.8", roundTrip.LineSpacing);
        Assert.Equal("Paperback", roundTrip.Format);
        Assert.False(roundTrip.IncludeCoverPage);
        Assert.Equal("#faf0e6", roundTrip.PageBackgroundColor, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BookFormattingSettings_kdp_margins_inside_exceeds_outside()
    {
        var settings = BookFormattingSettings.FromExportOptions(new BookPdfExportOptions());
        Assert.True(settings.MarginInsideIn > settings.MarginOutsideIn);
        Assert.Equal("6x9", settings.TrimSize);
    }

    [Fact]
    public async Task BuildBookHtml_includes_print_theme_and_chapter_structure()
    {
        var env = new FakeWebHostEnvironment { WebRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot") };
        var svc = new BookRenderService(
            env,
            new ConfigurationBuilder().Build(),
            NullLogger<BookRenderService>.Instance,
            new FixedTocPageNumberMeasurer(firstPage: 7));
        var details = new BookDetailsResponseDto
        {
            Success = true,
            BookId = 42,
            BookTitle = "Test Novel",
            AuthorName = "Jane Author",
            Genre = "Fiction",
            Chapters =
            [
                new ChapterDto { ChapterNumber = 1, Title = "Opening", Content = "<p class=\"manuscript-p\">First paragraph.</p>" }
            ]
        };
        var render = await svc.BuildBookHtmlAsync(new BookRenderRequest
        {
            Details = details,
            ExportOptions = new BookPdfExportOptions { InteriorStyle = "Novel", TextSize = "Medium", LineSpacing = "1.6" },
            DisplayTitle = details.BookTitle,
            DisplayAuthor = details.AuthorName,
            DisplayGenre = details.Genre
        });

        Assert.Contains("book-pdf-body", render.Html, StringComparison.Ordinal);
        Assert.Contains("reader-chapter-block", render.Html, StringComparison.Ordinal);
        Assert.Contains("Palatino", render.Html, StringComparison.Ordinal);
        Assert.Contains("toc-page-ref", render.Html, StringComparison.Ordinal);
        Assert.Contains("class=\"toc-page-ref\">", render.Html, StringComparison.Ordinal);
        Assert.Contains("href=\"#ch-1\"", render.Html, StringComparison.Ordinal);
        // Novel is the 5×8 mass-market trim (matches the per-style preview shape).
        Assert.Contains("5in", render.Layout.PageSizeCss, StringComparison.Ordinal);
        Assert.Equal("Novel", render.Settings.InteriorStyle);
    }

    [Fact]
    public void Trade_elegant_preset_medium_yields_about_30_lines_per_continuation_page()
    {
        var opt = new BookPdfExportOptions
        {
            InteriorStyle = "ElegantTrade",
            TextSize = "Medium",
            LineSpacing = "1.6"
        };
        var lines = InteriorSampleDocumentBuilder.EstimateBodyLinesPerContinuationPage(opt);
        Assert.InRange(lines, 29.0, 35.0);
    }

    [Fact]
    public void Sample_document_builder_produces_multi_chapter_markup()
    {
        var html = InteriorSampleDocumentBuilder.BuildSampleChapterSections(
            3, 12, new BookPdfExportOptions { InteriorStyle = "ElegantTrade", TextSize = "Medium", LineSpacing = "1.6" });
        Assert.Contains("page-header", html, StringComparison.Ordinal);
        Assert.Contains("manuscript-p", html, StringComparison.Ordinal);
        Assert.Contains("id=\"ch-3\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildEpub_has_toc_and_never_emits_chapter_zero()
    {
        var svc = new EpubExportService();
        var details = new BookDetailsResponseDto
        {
            Success = true,
            BookId = 7,
            BookTitle = "Numbering Test",
            AuthorName = "Author",
            Chapters =
            [
                // Mis-stored as ChapterNumber 0 with a literal "Chapter 0" title — must not leak into export.
                new ChapterDto { ChapterNumber = 0, Title = "Chapter 0", Content = "<p>First.</p>" },
                new ChapterDto { ChapterNumber = 2, Title = "The Journey", Content = "<p>Second.</p>" }
            ]
        };

        var bytes = await svc.BuildEpubAsync(details, null, details.BookTitle, details.AuthorName);

        using var ms = new MemoryStream(bytes);
        using var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read);

        string ReadEntry(string path)
        {
            var entry = zip.GetEntry(path);
            Assert.NotNull(entry);
            using var reader = new StreamReader(entry!.Open());
            return reader.ReadToEnd();
        }

        var nav = ReadEntry("OEBPS/nav.xhtml");
        var ncx = ReadEntry("OEBPS/toc.ncx");
        var opf = ReadEntry("OEBPS/content.opf");

        // Both TOC documents present and referenced.
        Assert.Contains("epub:type=\"toc\"", nav, StringComparison.Ordinal);
        Assert.Contains("<navMap>", ncx, StringComparison.Ordinal);
        Assert.Contains("<spine toc=\"ncx\">", opf, StringComparison.Ordinal);
        Assert.Contains("toc.ncx", opf, StringComparison.Ordinal);

        // No "Chapter 0" anywhere; numbering starts at 1; real title preserved.
        Assert.DoesNotContain("Chapter 0", nav, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Chapter 0", ncx, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chapter 1", nav, StringComparison.Ordinal);
        Assert.Contains("The Journey", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void Interior_typography_presets_maps_clean_minimalist_sans_stack()
    {
        var t = InteriorTypographyPresets.Resolve(new BookPdfExportOptions
        {
            InteriorStyle = "CleanMinimalist",
            TextSize = "Medium",
            LineSpacing = "1.6"
        });
        Assert.Contains("Libre Baskerville", t.BodyFontStack, StringComparison.Ordinal);
        Assert.Equal("11", t.BodyFontSizePt);
    }

    [Fact]
    public void ExtractNamedCoverAssets_reads_full_cover_base64_from_split_api()
    {
        var json = """{"status":"success","full_cover_base64":"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="}""";
        var assets = CoverExternalApiHelper.ExtractNamedCoverAssetsFromApiResponse(json);
        Assert.StartsWith("data:image/png;base64,", assets.Wrap, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedTocPageNumberMeasurer : ITocPageNumberMeasurer
    {
        private readonly int _firstPage;
        private readonly int _pageStep;

        public FixedTocPageNumberMeasurer(int firstPage = 7, int pageStep = 12)
        {
            _firstPage = firstPage;
            _pageStep = pageStep;
        }

        public Task<IReadOnlyList<int>> MeasureChapterStartPagesAsync(
            string fullBookHtml,
            BookPdfExportOptions exportOptions,
            BookPdfPlatformLayout.PdfLayoutSpec layout,
            string bookTitle,
            IReadOnlyList<string> chapterTitles,
            int expectedChapterCount,
            CancellationToken cancellationToken = default)
        {
            var pages = new List<int>(expectedChapterCount);
            for (var i = 0; i < expectedChapterCount; i++)
                pages.Add(_firstPage + i * _pageStep);
            return Task.FromResult<IReadOnlyList<int>>(pages);
        }
    }

    private sealed class FakeWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
