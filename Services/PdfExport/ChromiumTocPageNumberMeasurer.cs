using System.Globalization;
using System.Text;
using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using UglyToad.PdfPig;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// Measures chapter start pages by rendering probe HTML to PDF (same Chromium pipeline), then reading marker text per page via PdfPig.
/// </summary>
public sealed class ChromiumTocPageNumberMeasurer : ITocPageNumberMeasurer
{
    private readonly ILogger<ChromiumTocPageNumberMeasurer> _logger;
    private readonly IConfiguration _configuration;

    public ChromiumTocPageNumberMeasurer(
        ILogger<ChromiumTocPageNumberMeasurer> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> MeasureChapterStartPagesAsync(
        string fullBookHtml,
        BookPdfExportOptions exportOptions,
        BookPdfPlatformLayout.PdfLayoutSpec layout,
        string bookTitle,
        IReadOnlyList<string> chapterTitles,
        int expectedChapterCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullBookHtml))
            throw new ArgumentException("HTML is required.", nameof(fullBookHtml));
        if (expectedChapterCount <= 0)
            return Array.Empty<int>();

        exportOptions ??= new BookPdfExportOptions();
        exportOptions.Normalize();

        var headerTemplate = exportOptions.IncludeCoverPage
            ? string.Empty
            : PdfRunningHeaderFooter.BuildHeader(exportOptions.InteriorStyle, bookTitle);
        var footerTemplate = exportOptions.IncludeCoverPage
            ? string.Empty
            : PdfRunningHeaderFooter.BuildFooter(exportOptions.InteriorStyle);

        var exporter = new ChromiumPdfExporter(_logger, _configuration);
        var pdfBytes = await exporter.ExportAsync(
            fullBookHtml, layout, headerTemplate, footerTemplate, cancellationToken);

        var pages = ExtractChapterPagesViaPdfPig(pdfBytes, expectedChapterCount);
        if (pages.Count != expectedChapterCount || pages.Any(p => p <= 0))
        {
            _logger.LogWarning(
                "PdfPig TOC markers incomplete (got [{Pages}]); trying HTML layout fallback.",
                string.Join(", ", pages));

            pages = await MeasureChapterPagesViaHtmlLayoutAsync(
                fullBookHtml, layout, expectedChapterCount, cancellationToken);

            if (pages.Count != expectedChapterCount || pages.Any(p => p <= 0))
            {
                throw new InvalidOperationException(
                    $"TOC measurement could not resolve start pages for all chapters (got [{string.Join(", ", pages)}]).");
            }
        }

        _logger.LogInformation(
            "Chromium TOC measurement: {Chapters} chapters, includeCover={Cover}, pages=[{Pages}]",
            pages.Count,
            exportOptions.IncludeCoverPage,
            string.Join(", ", pages.Take(Math.Min(8, pages.Count))));

        return pages;
    }

    private static List<int> ExtractChapterPagesViaPdfPig(byte[] pdfBytes, int chapterCount)
    {
        var pageTexts = new List<(int Page, string Text)>();
        using (var doc = PdfDocument.Open(pdfBytes))
        {
            foreach (var page in doc.GetPages())
            {
                var sb = new StringBuilder();
                foreach (var word in page.GetWords())
                    sb.Append(word.Text);
                pageTexts.Add((page.Number, sb.ToString()));
            }
        }

        var tocPages = pageTexts
            .Where(x => x.Text.Contains("contents", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Page)
            .ToList();
        var minPage = tocPages.Count > 0 ? tocPages.Max() : 0;

        var pages = new List<int>(chapterCount);
        for (var i = 1; i <= chapterCount; i++)
        {
            var marker = "TOCMEASURE_" + i.ToString(CultureInfo.InvariantCulture) + "_END";
            var hitPage = pageTexts
                .Where(x => x.Page > minPage && x.Text.Contains(marker, StringComparison.Ordinal))
                .Select(x => x.Page)
                .FirstOrDefault();
            pages.Add(hitPage);
            if (hitPage > minPage) minPage = hitPage;
        }

        return pages;
    }

    private async Task<List<int>> MeasureChapterPagesViaHtmlLayoutAsync(
        string html,
        BookPdfPlatformLayout.PdfLayoutSpec layout,
        int chapterCount,
        CancellationToken cancellationToken)
    {
        var contentHeightPx = ComputeContentHeightPx(layout) ?? 720.0;
        var executablePath = await ChromiumLaunchHelper.ResolveExecutableAsync(_configuration, cancellationToken);

        await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
        {
            Headless = true,
            ExecutablePath = executablePath,
            Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
        });
        await using var page = await browser.NewPageAsync();
        await page.EmulateMediaTypeAsync(PuppeteerSharp.Media.MediaType.Print);
        await page.SetContentAsync(html, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = 120_000
        });

        var result = await page.EvaluateFunctionAsync<int[]>(
            @"(chapterCount, contentHeightPx) => {
                const pages = [];
                let minPage = 0;
                const toc = document.querySelector('.toc-page');
                if (toc) {
                    const y = toc.getBoundingClientRect().top + window.scrollY;
                    minPage = Math.max(0, Math.floor(y / contentHeightPx));
                }
                for (let i = 1; i <= chapterCount; i++) {
                    const el = document.querySelector('#ch-' + i + ' .toc-measure-marker')
                        || document.querySelector('#ch-' + i);
                    if (!el) { pages.push(-1); continue; }
                    const y = el.getBoundingClientRect().top + window.scrollY;
                    const pageNum = Math.max(1, Math.floor(y / contentHeightPx) + 1);
                    pages.push(pageNum > minPage ? pageNum : -1);
                    if (pageNum > minPage) minPage = pageNum;
                }
                return pages;
            }",
            chapterCount,
            contentHeightPx);

        return result?.ToList() ?? [];
    }

    private static double? ComputeContentHeightPx(BookPdfPlatformLayout.PdfLayoutSpec layout)
    {
        var pageIn = layout.PdfHeight != null && layout.PdfHeight.Contains("in", StringComparison.OrdinalIgnoreCase)
            ? ParseInches(layout.PdfHeight)
            : layout.UseBuiltInFormat ? 11.69 : 9.0;
        var top = ParseInches(layout.MarginTop);
        var bottom = ParseInches(layout.MarginBottom);
        if (pageIn is null || top is null || bottom is null) return null;
        var content = (pageIn.Value - top.Value - bottom.Value) * 96.0;
        return content > 100 ? content : null;
    }

    private static double? ParseInches(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var s = value.Trim().ToLowerInvariant();
        if (s.EndsWith("in", StringComparison.Ordinal)) s = s[..^2];
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
