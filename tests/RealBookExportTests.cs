using System.Text;
using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using EBookDashboard.Services.PdfExport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;

namespace EBookDashboard.Tests;

/// <summary>Full Chromium export. Skipped unless RUN_REAL_BOOK=1 so the rest of the suite stays fast.</summary>
public class RealBookExportTests
{
    private readonly ITestOutputHelper _output;

    public RealBookExportTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Zero_to_one_chromium_export_writes_integrity_report()
    {
        if (!Enabled()) return;
        var path = FindZeroToOne();
        Assert.False(string.IsNullOrEmpty(path), "Zero to One PDF/EPUB was not found.");
        var bytes = await File.ReadAllBytesAsync(path!);
        var ext = Path.GetExtension(path);
        var imported = ChapterDocumentImportService.ImportUploadedDocument(bytes, ext);
        var author = DocumentAuthorResolver.Resolve(bytes, ext) ?? "Peter Thiel";
        var chapters = imported.Chapters.Select(c => new ChapterDto
        {
            ChapterNumber = c.ChapterNo,
            Title = c.Title,
            Content = c.Body
        }).ToList();
        var details = new BookDetailsResponseDto
        {
            Success = true,
            BookId = 267,
            BookTitle = "Zero to One",
            AuthorName = author,
            Chapters = chapters,
            TotalChapters = chapters.Count
        };
        var opt = new BookPdfExportOptions
        {
            InteriorStyle = "Novel",
            IncludeCoverPage = false,
            KeepOriginalCopyrightPage = true,
            TrimWidthIn = 6,
            TrimHeightIn = 9
        };
        var env = TestHost.Create();
        var config = new ConfigurationBuilder().Build();
        var render = new BookRenderService(env, config, new TestLogger<BookRenderService>(), new ChromiumTocPageNumberMeasurer(new TestLogger<ChromiumTocPageNumberMeasurer>(), config));
        var built = await render.BuildBookHtmlAsync(new BookRenderRequest
        {
            Details = details,
            ExportOptions = opt,
            DisplayTitle = "Zero to One",
            DisplayAuthor = author
        });
        var exporter = new ChromiumPdfExporter(new TestLogger<ChromiumPdfExporter>(), config);
        var pdf = await exporter.ExportAsync(built.Html, built.Layout, "", "");
        pdf = PrintFolioStampService.Stamp(pdf, built.FrontMatterPageCount, "Zero to One", author, suppressFrontPages: new[] { 0 });
        var source = string.Concat(chapters.Select(c => c.Content));
        var layout = BookIntegrityChecker.StripLayoutFurniture(
            BookIntegrityChecker.TryExtractLayoutText(path!) ?? "");
        var report = BookIntegrityChecker.CheckRoundTrip(layout, source, pdf, chapters, built.Html);
        var outDir = Path.Combine(FindRepo()!, "test-output");
        Directory.CreateDirectory(outDir);
        var jsonPath = Path.Combine(outDir, "zero-to-one-integrity.json");
        await File.WriteAllTextAsync(jsonPath, BookIntegrityChecker.ToJson(report));
        await File.WriteAllBytesAsync(Path.Combine(outDir, "zero-to-one-rerun.pdf"), pdf);
        var rendered = PdfDocument.Open(pdf);
        var pdfDims = new List<string>();
        foreach (var page in rendered.GetPages())
        {
            foreach (var img in page.GetImages() ?? Enumerable.Empty<UglyToad.PdfPig.Content.IPdfImage>())
                pdfDims.Add(img.WidthInSamples + "x" + img.HeightInSamples);
        }
        var reading = BookIntegrityChecker.ReadReadingOrderPlain(pdf);
        var summary = new StringBuilder();
        summary.AppendLine("pages=" + rendered.NumberOfPages);
        summary.AppendLine("importSimilarity=" + report.ImportSimilarity.ToString("P2"));
        summary.AppendLine("printSimilarity=" + report.PrintSimilarity.ToString("P2"));
        summary.AppendLine("similarity=" + report.Similarity.ToString("P2"));
        summary.AppendLine("images=" + report.SourceImages + "->" + report.OutputImages);
        summary.AppendLine("pdfImageDims=" + pdfDims.Count + " " + string.Join(", ", pdfDims));
        summary.AppendLine("passed=" + report.Passed);
        summary.AppendLine("deletions=" + report.Deletions + " reorders=" + report.Reorders + " insertions=" + report.Insertions);
        summary.AppendLine("bodySimilarity=" + report.BodySimilarity.ToString("P2")
            + " bodyDeletions=" + report.BodyDeletions
            + " bodyReorders=" + report.BodyReorders
            + " bodyInsertions=" + report.BodyInsertions);
        foreach (var hit in report.BodyHits)
            summary.AppendLine("body-" + hit.Kind + " p" + hit.Page + " " + hit.Context);
        foreach (var needle in new[] { "companies they work with", "factor of", "Will They Come", "only 1/" })
        {
            var at = reading.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            summary.AppendLine("---- " + needle + " ----");
            summary.AppendLine(at < 0 ? "MISSING" : reading.Substring(Math.Max(0, at - 40), Math.Min(420, reading.Length - Math.Max(0, at - 40))).Replace('\n', ' '));
        }
        await File.WriteAllTextAsync(Path.Combine(outDir, "zero-to-one-summary.txt"), summary.ToString());
        var shotPages = FindShotPages(rendered);
        var pageCount = rendered.NumberOfPages;
        rendered.Dispose();
        WritePageShots(Path.Combine(outDir, "zero-to-one-rerun.pdf"), shotPages, Path.Combine(outDir, "screens"));
        _output.WriteLine(summary.ToString());

        var readable = reading.Replace("\uFB01", "fi").Replace("\uFB02", "fl");
        Assert.InRange(pageCount, 200, 220);
        Assert.Contains("companies they work with are by definition average", readable, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("factor of 1030", reading, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If You Build It, Will They Come?", reading, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COME?\nCOME?", reading, StringComparison.Ordinal);
        Assert.Contains("only 1/", reading, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trademarks of Random House", reading, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("randomcrown", reading, StringComparison.OrdinalIgnoreCase);
        // Old-style "1999" can sort ahead of the title in the PDF text layer. The print HTML is the order on the page.
        Assert.Matches(
            new System.Text.RegularExpressions.Regex(
                "fmt-ch-title\">Party Like It(?:&#39;|&#x27;|'|\u2019)s 1999",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            built.Html);
        Assert.DoesNotContain("fmt-ch-title\">1999", built.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Title Page<", built.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, report.TitlePageCount);
        Assert.Equal(1, report.TocPageCount);
        Assert.True(report.StructureOk, string.Join("; ", report.StructureNotes));
        Assert.Equal(0, report.BodyDeletions);
        Assert.Equal(0, report.BodyReorders);
        Assert.Equal(report.SourceImages, report.OutputImages);
        var screens = Path.Combine(outDir, "screens");
        foreach (var shot in shotPages)
        {
            Assert.True(shot.Page > 0, "No page found for " + shot.Name);
            Assert.True(Directory.GetFiles(screens, shot.Name + "-*.png").Length > 0, "Missing screenshot " + shot.Name);
        }
    }

    [Fact]
    public void Zero_to_one_rerun_snippets()
    {
        var pdfPath = Path.Combine(FindRepo() ?? "", "test-output", "zero-to-one-rerun.pdf");
        if (!File.Exists(pdfPath)) return;
        var text = ExtractSnippets(File.ReadAllBytes(pdfPath));
        var sourcePdf = Path.Combine(FindRepo()!, "test-corpus", "zero-to-one.pdf");
        if (File.Exists(sourcePdf))
            text = "SOURCE\n" + ExtractSnippets(File.ReadAllBytes(sourcePdf)) + "\nOUTPUT\n" + text;
        var outPath = Path.Combine(FindRepo()!, "test-output", "zero-to-one-snippets.txt");
        File.WriteAllText(outPath, text);
        using var doc = PdfDocument.Open(pdfPath);
        File.AppendAllText(outPath, "\nPAGES " + doc.NumberOfPages + "\n");
        _output.WriteLine("pages=" + doc.NumberOfPages);
        _output.WriteLine(text);
    }

    [Fact]
    public void Rescore_zero_to_one_pdf()
    {
        var repo = FindRepo();
        var pdfPath = Path.Combine(repo ?? "", "test-output", "zero-to-one-rerun.pdf");
        var srcPath = Path.Combine(repo ?? "", "test-corpus", "zero-to-one.pdf");
        if (!File.Exists(pdfPath) || !File.Exists(srcPath)) return;
        var imported = ChapterDocumentImportService.ImportUploadedDocument(File.ReadAllBytes(srcPath), ".pdf");
        var chapters = imported.Chapters.Select(c => new ChapterDto
        {
            ChapterNumber = c.ChapterNo,
            Title = c.Title,
            Content = c.Body
        }).ToList();
        var source = string.Concat(chapters.Select(c => c.Content));
        var layout = BookIntegrityChecker.StripLayoutFurniture(
            BookIntegrityChecker.TryExtractLayoutText(srcPath) ?? "");
        var report = BookIntegrityChecker.CheckRoundTrip(layout, source, File.ReadAllBytes(pdfPath), chapters);
        var summary = new
        {
            report.Passed,
            report.Similarity,
            report.ImportSimilarity,
            report.PrintSimilarity,
            report.SourceWords,
            report.OutputWords,
            report.Deletions,
            report.Insertions,
            report.Reorders,
            report.SourceImages,
            report.OutputImages,
            report.ImagesMatch,
            report.StructureOk,
            report.TitlePageCount,
            report.CopyrightPageCount,
            report.TocPageCount,
            report.SectionLabels,
            report.StructureNotes,
            report.BodySimilarity,
            report.BodyDeletions,
            report.BodyReorders,
            report.BodyInsertions,
            Hits = report.Hits.Take(40),
            BodyHits = report.BodyHits
        };
        File.WriteAllText(
            Path.Combine(repo!, "test-output", "zero-to-one-integrity-summary.json"),
            System.Text.Json.JsonSerializer.Serialize(summary, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task Broken_image_fails_the_export_with_its_name()
    {
        if (!Enabled()) return;
        var html = """
            <!DOCTYPE html><html><head><meta charset="utf-8"></head>
            <body><p>Before</p><img src="data:image/png;base64,AAAA" alt="broken-plate.png"><p>After</p></body></html>
            """;
        var config = new ConfigurationBuilder().Build();
        var exporter = new ChromiumPdfExporter(new TestLogger<ChromiumPdfExporter>(), config);
        var layout = BookPdfPlatformLayout.Resolve(new BookPdfExportOptions(), BookPdfLayoutOptions.FromConfiguration(config));
        var ex = await Assert.ThrowsAsync<PdfImageLoadException>(() => exporter.ExportAsync(html, layout, "", ""));
        Assert.Contains("broken-plate.png", ex.Message, StringComparison.Ordinal);
        _output.WriteLine(ex.Message);
    }

    [Fact]
    public async Task Four_hundred_page_text_is_chunked()
    {
        if (!Enabled()) return;
        var textPath = Path.Combine(FindRepo()!, "test-corpus", "large-800.txt");
        if (!File.Exists(textPath)) return;
        var text = await File.ReadAllTextAsync(textPath);
        var imported = ChapterDocumentImportService.ImportUploadedDocument(Encoding.UTF8.GetBytes(text), ".txt");
        var chapters = imported.Chapters.Select(c => new ChapterDto
        {
            ChapterNumber = c.ChapterNo,
            Title = string.IsNullOrWhiteSpace(c.Title) ? "Chapter " + c.ChapterNo : c.Title,
            Content = c.Body
        }).ToList();
        if (chapters.Count == 0)
            chapters.Add(new ChapterDto { ChapterNumber = 1, Title = "Chapter 1", Content = "<p>" + text + "</p>" });
        var htmlWeight = chapters.Sum(c => (c.Content ?? "").Length);
        _output.WriteLine($"chapters={chapters.Count} htmlChars={htmlWeight}");
        var opt = new BookPdfExportOptions { InteriorStyle = "Novel", IncludeCoverPage = false };
        var env = TestHost.Create();
        var config = new ConfigurationBuilder().Build();
        var render = new BookRenderService(env, config, new TestLogger<BookRenderService>(), new ChromiumTocPageNumberMeasurer(new TestLogger<ChromiumTocPageNumberMeasurer>(), config));
        var details = new BookDetailsResponseDto
        {
            Success = true,
            BookId = 800,
            BookTitle = "Long Text",
            AuthorName = "Test Author",
            Chapters = chapters
        };
        var built = await render.BuildBookHtmlAsync(new BookRenderRequest
        {
            Details = details,
            ExportOptions = opt,
            DisplayTitle = "Long Text",
            DisplayAuthor = "Test Author"
        });
        Assert.True(PrintHtmlChunker.ShouldChunk(built.Html));
        var exporter = new ChromiumPdfExporter(new TestLogger<ChromiumPdfExporter>(), config);
        var pdf = await exporter.ExportAsync(built.Html, built.Layout, "", "");
        using var doc = PdfDocument.Open(pdf);
        _output.WriteLine("pages=" + doc.NumberOfPages);
        Assert.True(doc.NumberOfPages >= 400, "Expected at least 400 pages, got " + doc.NumberOfPages);
    }

    private static (string Name, int Page)[] FindShotPages(PdfDocument doc)
    {
        int PageOf(Func<string, bool> match)
        {
            foreach (var page in doc.GetPages())
            {
                var text = page.Text ?? "";
                if (match(text))
                    return page.Number;
            }
            return -1;
        }

        var shots = new List<(string Name, int Page)>();
        for (var i = 1; i <= Math.Min(8, doc.NumberOfPages); i++)
            shots.Add(("page-" + i.ToString("000"), i));
        shots.Add(("chapter-2", PageOf(t =>
            t.Contains("Party Like", StringComparison.OrdinalIgnoreCase)
            && !t.Contains("Contents", StringComparison.OrdinalIgnoreCase))));
        return shots.ToArray();
    }

    private static void WritePageShots(string pdfPath, (string Name, int Page)[] shots, string directory)
    {
        Directory.CreateDirectory(directory);
        var exe = ResolvePdftoppm();
        if (exe == null)
            return;
        foreach (var shot in shots)
        {
            if (shot.Page <= 0)
                continue;
            var prefix = Path.Combine(directory, shot.Name);
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-png");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add("110");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(shot.Page.ToString());
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add(shot.Page.ToString());
            psi.ArgumentList.Add(pdfPath);
            psi.ArgumentList.Add(prefix);
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(60000);
        }
    }

    private static string? ResolvePdftoppm()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "pdftoppm.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        return Directory.Exists(local)
            ? Directory.EnumerateFiles(local, "pdftoppm.exe", SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    private static bool Enabled() =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_REAL_BOOK"), "1", StringComparison.Ordinal);

    private static string? FindZeroToOne()
    {
        var repo = FindRepo();
        if (repo == null) return null;
        var candidates = new[]
        {
            Path.Combine(repo, "TestBooks"),
            Path.Combine(repo, "test-corpus", "zero-to-one.pdf")
        };
        if (Directory.Exists(candidates[0]))
        {
            var hit = Directory.EnumerateFiles(candidates[0]).FirstOrDefault(f =>
                f.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }

        return File.Exists(candidates[1]) ? candidates[1] : null;
    }

    private static string? FindRepo()
    {
        var fromEnv = Environment.GetEnvironmentVariable("EBOOK_REPO");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(Path.Combine(fromEnv, "newEbook.csproj")))
            return fromEnv;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "newEbook.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        const string known = @"c:\Users\Hp\Desktop\newEbook";
        return File.Exists(Path.Combine(known, "newEbook.csproj")) ? known : null;
    }

    private static string ExtractSnippets(byte[] pdf)
    {
        var needles = new[]
        {
            "most of the companies",
            "companies they work with",
            "factor of",
            "cylindrical cells",
            "trademark",
            "If even",
            "If You Build It",
            "COME?",
            "Chapter 15",
            "Illustration Credits"
        };
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
        {
            var text = page.Text ?? "";
            foreach (var needle in needles)
            {
                var at = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (at < 0) continue;
                var start = Math.Max(0, at - 80);
                var len = Math.Min(420, text.Length - start);
                sb.Append("p").Append(page.Number).Append(": ").Append(text.Substring(start, len).Replace('\n', ' ')).AppendLine();
            }
        }

        return sb.ToString();
    }

    private sealed class TestHost : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "tests";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public static TestHost Create()
        {
            var repo = FindRepo() ?? "";
            return new TestHost
            {
                WebRootPath = Path.Combine(repo, "wwwroot"),
                ContentRootPath = repo
            };
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
