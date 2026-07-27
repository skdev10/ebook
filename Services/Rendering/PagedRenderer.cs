using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using EBookDashboard.Models;
using EBookDashboard.Services.PdfExport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace EBookDashboard.Services.Rendering;

/// <summary>Physical/display page number for a single <see cref="BookSection"/> after pagination.</summary>
public sealed record SectionPageInfo(int SectionId, int DisplayPageNumber, bool IsRoman);

/// <summary>Result of a full pagination pass over a manuscript version + layout profile.</summary>
public sealed class PaginationResult
{
    public int TotalPages { get; set; }
    public int FrontMatterPageCount { get; set; }
    public int BodyPageCount { get; set; }

    /// <summary>Key = <see cref="BookSection"/>.Id.</summary>
    public Dictionary<int, SectionPageInfo> SectionStartPages { get; set; } = new();

    /// <summary>Only populated when the caller requested the composed PDF bytes.</summary>
    public byte[]? PdfBytes { get; set; }
}

/// <summary>
/// Paginates and (optionally) renders a manuscript version to PDF using headless Chromium
/// (PuppeteerSharp — same launch pattern as <see cref="ChromiumPdfExporter"/>). Front matter and
/// body/back matter are rendered as two separate Chromium print passes so body page numbers can
/// restart at 1 while front matter uses Roman numerals; a lightweight PDFsharp overlay pass then
/// stamps the correct numeral style onto every physical page (Chromium's print engine has no way
/// to vary page-number formatting mid-document). Section start pages are located by scanning the
/// rendered PDF text layer (via PdfPig) for invisible "SECMARK{id}" markers.
/// </summary>
public sealed class PagedRenderer
{
    private static readonly ConcurrentDictionary<string, (DateTime CachedAt, PaginationResult Result)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(30);

    private readonly BookHtmlBuilder _htmlBuilder;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PagedRenderer> _logger;

    public PagedRenderer(
        BookHtmlBuilder htmlBuilder,
        IConfiguration configuration,
        IWebHostEnvironment env,
        ILogger<PagedRenderer> logger)
    {
        _htmlBuilder = htmlBuilder;
        _configuration = configuration;
        _env = env;
        _logger = logger;
    }

    public async Task<PaginationResult> PaginateAsync(
        Project project,
        ManuscriptVersion version,
        LayoutProfile profile,
        bool producePdfBytes,
        CancellationToken cancellationToken = default)
    {
        var layoutHash = ComputeLayoutHash(project, profile);
        var cacheKey = $"{version.Id}:{layoutHash}";
        if (Cache.TryGetValue(cacheKey, out var cached)
            && DateTime.UtcNow - cached.CachedAt < CacheTtl
            && (!producePdfBytes || cached.Result.PdfBytes != null))
        {
            return cached.Result;
        }

        var allSections = version.Sections.OrderBy(s => s.OrderIndex).ToList();
        var frontSections = allSections.Where(s => s.MatterType == MatterType.Front).ToList();
        var bodySections = allSections.Where(s => s.MatterType != MatterType.Front).ToList();

        byte[]? frontPdf = null;
        if (frontSections.Count > 0)
            frontPdf = await RenderScopeAsync(project, version, profile, SectionScope.FrontOnly, cancellationToken);

        byte[] bodyPdf = bodySections.Count > 0
            ? await RenderScopeAsync(project, version, profile, SectionScope.BodyAndBack, cancellationToken)
            : await RenderScopeAsync(project, version, profile, SectionScope.All, cancellationToken);

        var frontPageCount = frontPdf != null ? CountPdfPages(frontPdf) : 0;
        var bodyPageCount = CountPdfPages(bodyPdf);

        var sectionPages = new Dictionary<int, SectionPageInfo>();
        if (frontPdf != null)
            MapMarkersToPages(frontPdf, frontSections, sectionPages, isRoman: true);
        MapMarkersToPages(bodyPdf, bodySections.Count > 0 ? bodySections : allSections, sectionPages, isRoman: false);

        byte[]? finalBytes = null;
        if (producePdfBytes)
        {
            try
            {
                finalBytes = ComposeFinalPdf(frontPdf, bodyPdf, profile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PDFsharp page-number overlay failed; falling back to un-numbered pages.");
                finalBytes = ConcatenateWithoutOverlay(frontPdf, bodyPdf);
            }
        }

        var result = new PaginationResult
        {
            TotalPages = frontPageCount + bodyPageCount,
            FrontMatterPageCount = frontPageCount,
            BodyPageCount = bodyPageCount,
            SectionStartPages = sectionPages,
            PdfBytes = finalBytes
        };

        Cache[cacheKey] = (DateTime.UtcNow, result);
        return result;
    }

    private async Task<byte[]> RenderScopeAsync(
        Project project,
        ManuscriptVersion version,
        LayoutProfile profile,
        SectionScope scope,
        CancellationToken cancellationToken)
    {
        var html = _htmlBuilder.BuildHtml(project, version, profile, new BookHtmlBuildOptions
        {
            Scope = scope,
            IncludeSectionMarkers = true,
            PreviewMode = false
        });

        var executablePath = await ChromiumLaunchHelper.ResolveExecutableAsync(_configuration, cancellationToken);
        var launchOptions = new LaunchOptions
        {
            Headless = true,
            ExecutablePath = executablePath,
            Args =
            [
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-dev-shm-usage",
                "--font-render-hinting=none",
                "--disable-gpu"
            ]
        };

        await using var browser = await Puppeteer.LaunchAsync(launchOptions);
        await using var page = await browser.NewPageAsync();
        await page.EmulateMediaTypeAsync(MediaType.Print);
        await page.SetContentAsync(html, new NavigationOptions
        {
            WaitUntil = [WaitUntilNavigation.DOMContentLoaded, WaitUntilNavigation.Load],
            Timeout = 120_000
        });
        await page.EvaluateFunctionAsync(@"async () => {
            if (document.fonts && document.fonts.ready) await document.fonts.ready;
            await new Promise(r => setTimeout(r, 300));
        }");

        return await page.PdfDataAsync(new PdfOptions
        {
            PrintBackground = true,
            PreferCSSPageSize = true,
            DisplayHeaderFooter = false,
            MarginOptions = new MarginOptions { Top = "0", Bottom = "0", Left = "0", Right = "0" }
        });
    }

    private static int CountPdfPages(byte[] pdfBytes)
    {
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfBytes, new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = true });
        return doc.NumberOfPages;
    }

    private static void MapMarkersToPages(
        byte[] pdfBytes,
        List<BookSection> sections,
        Dictionary<int, SectionPageInfo> into,
        bool isRoman)
    {
        if (sections.Count == 0) return;

        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdfBytes, new UglyToad.PdfPig.ParsingOptions { UseLenientParsing = true });
        var pages = doc.GetPages().ToList();

        foreach (var section in sections)
        {
            var marker = $"SECMARK{section.Id}";
            for (var i = 0; i < pages.Count; i++)
            {
                var text = pages[i].Text ?? string.Empty;
                if (text.Contains(marker, StringComparison.Ordinal))
                {
                    var displayPage = i + 1;
                    into[section.Id] = new SectionPageInfo(section.Id, displayPage, isRoman);
                    break;
                }
            }
        }
    }

    /// <summary>Merges the two Chromium-rendered PDFs and stamps roman/arabic folios via PDFsharp.</summary>
    private byte[] ComposeFinalPdf(byte[]? frontPdf, byte[] bodyPdf, LayoutProfile profile)
    {
        var fontDir = Path.Combine(_env.WebRootPath ?? string.Empty, "fonts", "pdf");
        Directory.CreateDirectory(fontDir);
        ExportPdfFontResolver.EnsureRegistered(fontDir);

        using var outputDoc = new PdfDocument();
        var font = CreateFolioFont();
        var brush = XBrushes.Black;

        void AppendScope(byte[] bytes, Func<int, string> formatter)
        {
            using var ms = new MemoryStream(bytes);
            using var src = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            for (var i = 0; i < src.PageCount; i++)
            {
                var imported = outputDoc.AddPage(src.Pages[i]);
                if (!profile.ShowPageNumbers) continue;

                var displayIndex = i + 1;
                var label = formatter(displayIndex);
                var isRecto = displayIndex % 2 == 1;

                using var gfx = XGraphics.FromPdfPage(imported, XGraphicsPdfPageOptions.Append);
                var size = gfx.MeasureString(label, font);
                var (x, y) = ResolveFolioPosition(profile.PageNumberPosition, imported.Width.Point, imported.Height.Point, isRecto, size);
                gfx.DrawString(label, font, brush, x, y);
            }
        }

        if (frontPdf != null)
            AppendScope(frontPdf, n => ToRoman(n).ToLowerInvariant());
        AppendScope(bodyPdf, n => n.ToString(CultureInfo.InvariantCulture));

        using var outMs = new MemoryStream();
        outputDoc.Save(outMs, false);
        return outMs.ToArray();
    }

    /// <summary>
    /// "DejaVu Sans" (embedded via <see cref="ExportPdfFontResolver"/>, or present as a system font
    /// on most Linux hosts) is preferred, but Windows dev machines have neither — so we fall back
    /// through a short list of near-universal system fonts before giving up on folio numbers.
    /// </summary>
    private static XFont CreateFolioFont()
    {
        string[] candidates = { "DejaVu Sans", "Arial", "Segoe UI", "Helvetica", "Liberation Sans" };
        foreach (var family in candidates)
        {
            try
            {
                return new XFont(family, 9);
            }
            catch (InvalidOperationException)
            {
                // Family not resolvable on this host — try the next candidate.
            }
        }

        throw new InvalidOperationException("No usable font found for PDF page-number overlay.");
    }

    private static byte[] ConcatenateWithoutOverlay(byte[]? frontPdf, byte[] bodyPdf)
    {
        using var outputDoc = new PdfDocument();
        void AppendScope(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var src = PdfReader.Open(ms, PdfDocumentOpenMode.Import);
            for (var i = 0; i < src.PageCount; i++)
                outputDoc.AddPage(src.Pages[i]);
        }
        if (frontPdf != null) AppendScope(frontPdf);
        AppendScope(bodyPdf);
        using var outMs = new MemoryStream();
        outputDoc.Save(outMs, false);
        return outMs.ToArray();
    }

    private static (double X, double Y) ResolveFolioPosition(string position, double pageWidthPt, double pageHeightPt, bool isRecto, XSize textSize)
    {
        const double edgeInsetPt = 28;
        var posLower = (position ?? string.Empty).ToLowerInvariant();
        var y = posLower.Contains("top") ? edgeInsetPt : pageHeightPt - edgeInsetPt;

        double x;
        if (posLower.Contains("outside"))
            x = isRecto ? pageWidthPt - edgeInsetPt - textSize.Width : edgeInsetPt;
        else if (posLower.Contains("inside"))
            x = isRecto ? edgeInsetPt : pageWidthPt - edgeInsetPt - textSize.Width;
        else
            x = (pageWidthPt - textSize.Width) / 2;

        return (x, y);
    }

    private static string ToRoman(int number)
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

    private static string ComputeLayoutHash(Project project, LayoutProfile profile)
    {
        var raw = string.Join('|',
            project.TrimWidthIn, project.TrimHeightIn, project.HasBleed, project.ProjectType,
            profile.IsEbookProfile, profile.MarginTopIn, profile.MarginBottomIn, profile.MarginOutsideIn, profile.MarginInsideIn,
            profile.BleedMode, profile.BodyFontFamily, profile.BodyFontSizePt, profile.LineSpacing, profile.TextAlignment,
            profile.FirstLineIndentIn, profile.NoIndentOnFirstPara, profile.H1FontFamily, profile.H1FontSizePt,
            profile.H2FontFamily, profile.H2FontSizePt, profile.PageNumberPosition, profile.ShowPageNumbers,
            profile.ChaptersStartOnRecto);
        return Convert.ToHexString(System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(raw)));
    }
}
