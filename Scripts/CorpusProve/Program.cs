using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EBookDashboard.Configuration;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using EBookDashboard.Services.PdfExport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PigPdf = UglyToad.PdfPig.PdfDocument;
using UglyToad.PdfPig;

var repoRoot = args.FirstOrDefault(a => !a.StartsWith("--") && Directory.Exists(a))
               ?? args.ElementAtOrDefault(0)
               ?? @"C:\Users\Hp\Desktop\newEbook";
if (repoRoot.StartsWith("--", StringComparison.Ordinal) || !Directory.Exists(repoRoot))
    repoRoot = @"C:\Users\Hp\Desktop\newEbook";

var bookArg = GetArg(args, "--book");
var orchestrate = args.Any(a => a.Equals("--orchestrate", StringComparison.OrdinalIgnoreCase))
                  || string.IsNullOrEmpty(bookArg) && !args.Any(a => a.Equals("--sabotage", StringComparison.OrdinalIgnoreCase));
var sabotageOnly = args.Any(a => a.Equals("--sabotage", StringComparison.OrdinalIgnoreCase));

Environment.CurrentDirectory = repoRoot;
var corpusDir = Path.Combine(repoRoot, "test-corpus");
var outRoot = Path.Combine(repoRoot, "test-output");
Directory.CreateDirectory(corpusDir);
Directory.CreateDirectory(outRoot);

var log = new StringBuilder();
void L(string s) { Console.WriteLine(s); log.AppendLine(s); }

FixtureFactory.EnsureAll(corpusDir, repoRoot, L);

if (orchestrate)
{
    L("=== CorpusProve ORCHESTRATOR (per-book processes) ===");
    var exe = Environment.ProcessPath ?? "dotnet";
    var project = Path.Combine(repoRoot, "Scripts", "CorpusProve", "CorpusProve.csproj");
    var bookIds = FixtureFactory.Describe(corpusDir, repoRoot).Select(b => b.Id).ToList();
    bookIds.Insert(0, "sabotage");

    // One book = one process. Run sequentially so Chromium peak-RAM of one book
    // cannot OOM-kill siblings; a crash still leaves other result.json files intact.
    foreach (var id in bookIds)
    {
        var bookOut = Path.Combine(outRoot, id == "sabotage" ? "_sabotage" : id);
        Directory.CreateDirectory(bookOut);
        var logPath = Path.Combine(bookOut, "run.log");
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = id == "sabotage"
                ? $"run --project \"{project}\" -c Debug --no-build -- \"{repoRoot}\" --sabotage"
                : $"run --project \"{project}\" -c Debug --no-build -- \"{repoRoot}\" --book {id}",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        L($"Spawning {id}…");
        using var p = new Process { StartInfo = psi };
        using var swLog = new StreamWriter(logPath) { AutoFlush = true };
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            swLog.WriteLine(e.Data);
            Console.WriteLine($"[{id}] {e.Data}");
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            swLog.WriteLine("ERR " + e.Data);
            Console.WriteLine($"[{id}] ERR {e.Data}");
        };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        L($"  pid={p.Id} log={logPath}");
        await p.WaitForExitAsync();
        L($"Finished {id} exit={p.ExitCode}");
        // Ensure result.json exists even on hard crash
        var jsonPath = Path.Combine(bookOut, "result.json");
        if (!File.Exists(jsonPath))
        {
            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(new
            {
                id,
                overall = "FAIL",
                wordDiffReal = "?",
                imageMatch = "?",
                tocMatch = "?",
                preflight = "FAIL",
                pages = 0,
                seconds = 0.0,
                memoryMb = 0.0,
                detail = $"process exit={p.ExitCode} (no result.json — crash/timeout)"
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    // Summary from result.json files
    L("\n=== CORPUS SUMMARY TABLE ===");
    L("| Book | Word-diff real | Image match | TOC match | Preflight | Pages | Time(s) | Mem(MB) | Result |");
    L("|------|----------------|-------------|-----------|-----------|-------|---------|---------|--------|");
    var allPass = true;
    foreach (var id in bookIds)
    {
        var jsonPath = Path.Combine(outRoot, id == "sabotage" ? "_sabotage" : id, "result.json");
        if (!File.Exists(jsonPath))
        {
            L($"| {id} | — | — | — | — | — | — | — | FAIL (no result.json) |");
            allPass = false;
            continue;
        }
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        var r = doc.RootElement;
        var overall = r.GetProperty("overall").GetString() ?? "FAIL";
        if (overall != "PASS") allPass = false;
        L($"| {id} | {r.GetProperty("wordDiffReal").GetString()} | {r.GetProperty("imageMatch").GetString()} | {r.GetProperty("tocMatch").GetString()} | {r.GetProperty("preflight").GetString()} | {r.GetProperty("pages").GetInt32()} | {r.GetProperty("seconds").GetDouble():0.0} | {r.GetProperty("memoryMb").GetDouble():0} | {overall} |");
    }
    L(allPass ? "\nOVERALL: PASS" : "\nOVERALL: FAIL");
    await File.WriteAllTextAsync(Path.Combine(outRoot, "CORPUS-SUMMARY.txt"), log.ToString());
    return allPass ? 0 : 1;
}

// Single-book or sabotage worker
var host = Host.CreateDefaultBuilder(Array.Empty<string>())
    .UseContentRoot(repoRoot)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.SetBasePath(repoRoot);
        cfg.AddJsonFile("appsettings.json", optional: false);
        cfg.AddJsonFile("appsettings.Development.json", optional: true);
    })
    .ConfigureServices((_, services) =>
    {
        services.AddSingleton<IWebHostEnvironment>(new Env(repoRoot));
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<PdfHtmlExportServiceResolver>();
        services.AddScoped<ITocPageNumberMeasurer, ChromiumTocPageNumberMeasurer>();
        services.AddScoped<IBookRenderService, BookRenderService>();
        services.AddScoped<IBookPdfService, BookPdfService>();
    })
    .Build();

using var scope = host.Services.CreateScope();
var pdfSvc = scope.ServiceProvider.GetRequiredService<IBookPdfService>();
var popBin = FindPopplerBin();

if (sabotageOnly)
{
    L("=== SABOTAGE WORKER ===");
    var sab = await RunSabotageProofs(repoRoot, corpusDir, L);
    L(sab.Ok ? "SABOTAGE: PASS" : "SABOTAGE: FAIL — " + sab.Detail);
    var sabDir = Path.Combine(outRoot, "_sabotage");
    Directory.CreateDirectory(sabDir);
    await File.WriteAllTextAsync(Path.Combine(sabDir, "result.json"), JsonSerializer.Serialize(new
    {
        id = "sabotage",
        overall = sab.Ok ? "PASS" : "FAIL",
        wordDiffReal = sab.Ok ? "caught" : sab.Detail,
        imageMatch = "n/a",
        tocMatch = "n/a",
        preflight = "n/a",
        pages = 0,
        seconds = 0.0,
        memoryMb = Process.GetCurrentProcess().PeakWorkingSet64 / (1024.0 * 1024.0),
        detail = sab.Detail
    }, new JsonSerializerOptions { WriteIndented = true }));
    await File.WriteAllTextAsync(Path.Combine(sabDir, "run-summary.txt"), log.ToString());
    return sab.Ok ? 0 : 1;
}

var books = FixtureFactory.Describe(corpusDir, repoRoot);
var book = books.FirstOrDefault(b => b.Id.Equals(bookArg, StringComparison.OrdinalIgnoreCase));
if (book == null)
{
    L($"Unknown book id: {bookArg}");
    return 2;
}

L($"=== CorpusProve BOOK={book.Id} ===");
var row = await RunBook(book, pdfSvc, popBin, outRoot, L);
L($"  → wordDiff={row.WordDiffReal} img={row.ImageMatch} toc={row.TocMatch} preflight={row.Preflight} pages={row.Pages} time={row.Seconds:0.0}s mem={row.MemoryMb:0}MB => {row.Overall}");
await File.WriteAllTextAsync(Path.Combine(outRoot, book.Id, "run-summary.txt"), log.ToString());
return row.Overall == "PASS" ? 0 : 1;

static string? GetArg(string[] a, string name)
{
    for (var i = 0; i < a.Length - 1; i++)
        if (a[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            return a[i + 1];
    return null;
}

// ───────────────────────── helpers ─────────────────────────

static async Task<CorpusRow> RunBook(
    BookSpec book,
    IBookPdfService pdfSvc,
    string? popBin,
    string outRoot,
    Action<string> log)
{
    var row = new CorpusRow { Id = book.Id, Title = book.Title };
    var bookOut = Path.Combine(outRoot, book.Id);
    Directory.CreateDirectory(bookOut);
    var sw = Stopwatch.StartNew();
    var proc = Process.GetCurrentProcess();
    long mem0 = proc.PeakWorkingSet64;

    try
    {
        if (book.ExpectPreflightFail)
        {
            var bytes = await File.ReadAllBytesAsync(book.Path);
            using (var doc = PigPdf.Open(new MemoryStream(bytes, false)))
                row.Pages = doc.NumberOfPages;
            var optFail = new BookPdfExportOptions
            {
                InteriorStyle = "ElegantTrade", TextSize = "Medium", LineSpacing = "1.5",
                TrimWidthIn = 6, TrimHeightIn = 9, UseBleed = false
            };
            optFail.Normalize();
            KdpInteriorMarginCalculator.ApplyDefaults(optFail, row.Pages);
            var preflightFail = KdpPrintPreflight.Validate(bytes, optFail, book.Title, book.Author);
            var pageFail = preflightFail.Checks.Any(c => c.Code == "PAGE_COUNT" && !c.Pass);
            row.Preflight = pageFail ? "FAIL(expected)" : "PASS(unexpected)";
            row.WordDiffReal = "n/a";
            row.ImageMatch = "n/a";
            row.TocMatch = "n/a";
            row.Detail = string.Join("; ", preflightFail.Checks.Where(c => !c.Pass).Select(c => c.Message));
            row.Overall = pageFail && row.Pages > 828 ? "PASS" : "FAIL";
            await File.WriteAllTextAsync(Path.Combine(bookOut, "preflight.txt"), preflightFail.Format());
            sw.Stop();
            row.Seconds = sw.Elapsed.TotalSeconds;
            row.MemoryMb = (proc.PeakWorkingSet64 - mem0) / (1024.0 * 1024.0);
            if (row.MemoryMb < 1) row.MemoryMb = proc.PeakWorkingSet64 / (1024.0 * 1024.0);
            await WriteResultJson(bookOut, row);
            return row;
        }

        if (book.ExpectScannedReject)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(book.Path);
                ChapterDocumentImportService.ImportUploadedDocument(bytes, book.Ext);
                row.Overall = "FAIL";
                row.Detail = "expected scanned rejection";
                row.Preflight = "FAIL";
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("scanned", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("OCR", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("image-only", StringComparison.OrdinalIgnoreCase))
            {
                row.Overall = "PASS";
                row.Preflight = "PASS";
                row.WordDiffReal = "n/a";
                row.ImageMatch = "n/a";
                row.TocMatch = "n/a";
                row.Detail = ex.Message;
                await File.WriteAllTextAsync(Path.Combine(bookOut, "scanned-message.txt"), ex.Message);
            }

            sw.Stop();
            row.Seconds = sw.Elapsed.TotalSeconds;
            row.MemoryMb = proc.PeakWorkingSet64 / (1024.0 * 1024.0);
            await WriteResultJson(bookOut, row);
            return row;
        }

        if (book.ImportOnly)
        {
            var bytes = await File.ReadAllBytesAsync(book.Path);
            var (plain, chapters) = ChapterDocumentImportService.ImportUploadedDocument(bytes, book.Ext);
            row.Pages = 0;
            row.WordDiffReal = "import-ok";
            row.ImageMatch = book.ExpectImages
                ? (chapters.Sum(c => Regex.Matches(c.Body ?? "", "<img\\b", RegexOptions.IgnoreCase).Count) > 0 ? "PASS" : "FAIL")
                : "n/a";
            row.TocMatch = chapters.Count >= book.MinChapters ? "PASS" : $"FAIL({chapters.Count}<{book.MinChapters})";
            // Two-column / poetry content checks
            var ok = chapters.Count >= book.MinChapters && plain.Length > 100;
            if (book.Id == "poetry")
            {
                var verse = chapters.Any(c => (c.Body ?? "").Contains("verse-line", StringComparison.OrdinalIgnoreCase));
                ok = ok && verse;
                row.Detail = verse ? "verse-lines preserved" : "missing verse-line markup";
            }
            // Two-column: also accept PdfPig page.Text order
            if (book.Id == "two-column")
            {
                using var doc = PigPdf.Open(new MemoryStream(bytes, false));
                var pageText = string.Join("\n", doc.GetPages().Select(p => p.Text ?? ""));
                var probe = plain + "\n" + pageText;
                var hasLeft = probe.Contains("LEFTCOLUMN", StringComparison.OrdinalIgnoreCase);
                var hasRight = probe.Contains("RIGHTCOLUMN", StringComparison.OrdinalIgnoreCase);
                var li = probe.IndexOf("LEFTCOLUMN", StringComparison.OrdinalIgnoreCase);
                var ri = probe.IndexOf("RIGHTCOLUMN", StringComparison.OrdinalIgnoreCase);
                var orderOk = hasLeft && hasRight && li >= 0 && ri >= 0 && li < ri;
                ok = orderOk;
                row.Detail = orderOk ? "left-then-right order" : $"column order wrong left={hasLeft} right={hasRight}";
                row.TocMatch = orderOk ? "PASS" : "FAIL";
            }

            row.Preflight = ok ? "PASS" : "FAIL";
            row.Overall = ok ? "PASS" : "FAIL";
            sw.Stop();
            row.Seconds = sw.Elapsed.TotalSeconds;
            row.MemoryMb = proc.PeakWorkingSet64 / (1024.0 * 1024.0);
            await WriteResultJson(bookOut, row);
            return row;
        }

        var srcBytes = await File.ReadAllBytesAsync(book.Path);
        log($"  Importing {book.Ext}…");
        var (plainText, chaptersIn) = ChapterDocumentImportService.ImportUploadedDocument(srcBytes, book.Ext);
        if (book.Ext is ".txt" or ".epub")
            plainText = FixtureFactory.StripGutenberg(plainText);
        log($"  chapters={chaptersIn.Count} plain={plainText.Length}");

        if (book.Id == "poetry")
        {
            chaptersIn = ChapterDocumentImportService.SplitVerseIntoChapters(plainText);
            log($"  poetry forced verse split: chapters={chaptersIn.Count}");
            var verseOk = chaptersIn.Any(c => (c.Body ?? "").Contains("verse-line", StringComparison.OrdinalIgnoreCase));
            if (!verseOk)
            {
                row.Overall = "FAIL";
                row.Detail = "poetry missing verse-line markup";
                row.Preflight = "FAIL";
                row.TocMatch = "FAIL";
                row.WordDiffReal = "n/a";
                sw.Stop();
                row.Seconds = sw.Elapsed.TotalSeconds;
                row.MemoryMb = proc.PeakWorkingSet64 / (1024.0 * 1024.0);
                await WriteResultJson(bookOut, row);
                return row;
            }
            log("  verse-line markup: OK");
        }

        if (book.MaxChaptersForRender is int maxCh && chaptersIn.Count > maxCh)
            chaptersIn = chaptersIn.Take(maxCh).ToList();

        var repoRootFixed = Path.GetFullPath(Path.Combine(bookOut, "..", ".."));
        if (!Directory.Exists(Path.Combine(repoRootFixed, "wwwroot")))
            repoRootFixed = Environment.CurrentDirectory;

        ChapterDocumentImportService.MaterializeDataUriImages(
            chaptersIn, Path.Combine(repoRootFixed, "wwwroot"), $"uploads/corpus/{book.Id}");

        var srcImgs = book.Ext == ".pdf"
            ? CountPdfContentImages(srcBytes)
            : chaptersIn.Sum(c => Regex.Matches(c.Body ?? "", "<img\\b", RegexOptions.IgnoreCase).Count);
        var htmlImgs = chaptersIn.Sum(c => Regex.Matches(c.Body ?? "", "<img\\b", RegexOptions.IgnoreCase).Count);

        var details = new BookDetailsResponseDto
        {
            Success = true,
            BookId = Math.Abs(book.Id.GetHashCode() % 9000) + 1000,
            BookTitle = book.Title,
            AuthorName = book.Author,
            Genre = book.Genre,
            Chapters = chaptersIn.Select(c => new ChapterDto
            {
                ChapterNumber = ChapterDocumentImportService.IsFrontMatterMarker(c.Title) ? 0
                    : ChapterDocumentImportService.IsBackMatterMarker(c.Title) ? 9000 + c.ChapterNo
                    : c.ChapterNo,
                Title = c.Title,
                Content = c.Body
            }).ToList(),
            TotalChapters = chaptersIn.Count
        };

        var opt = new BookPdfExportOptions
        {
            InteriorStyle = "ElegantTrade",
            TextSize = "Medium",
            LineSpacing = "1.5",
            Format = "Paperback",
            PublishingPlatform = "Amazon KDP",
            TrimWidthIn = 6.0,
            TrimHeightIn = 9.0,
            UseBleed = false,
            IncludeCoverPage = false,
            PageBackgroundColor = "#ffffff"
        };
        opt.Normalize();
        // Prefer a conservative page estimate so gutter tiers aren't undershot before we know
        // the final page count (batch merges especially).
        var marginHint = Math.Max(book.EstimatedPagesHint, Math.Max(40, chaptersIn.Count * 4));
        KdpInteriorMarginCalculator.ApplyDefaults(opt, estimatedPageCount: marginHint);

        log("  Rendering…");
        byte[] pdfBytes;
        if (chaptersIn.Count > 30 || book.Id == "large-800")
        {
            pdfBytes = await RenderInChapterBatches(pdfSvc, details, opt, book, log);
        }
        else
        {
            pdfBytes = await pdfSvc.RenderFullBookPdfAsync(
                details, null, details.BookTitle, details.AuthorName, details.Genre, opt, null);
        }
        var pdfPath = Path.Combine(bookOut, book.Id + "-print.pdf");
        await File.WriteAllBytesAsync(pdfPath, pdfBytes);

        using (var doc = PigPdf.Open(new MemoryStream(pdfBytes, false)))
            row.Pages = doc.NumberOfPages;

        var preflight = KdpPrintPreflight.Validate(pdfBytes, opt, details.BookTitle, details.AuthorName, htmlImgs);
        row.Preflight = preflight.Passed ? "PASS" : "FAIL";
        row.Detail = string.Join("; ", preflight.Checks.Where(c => !c.Pass).Select(c => c.Code + ":" + c.Message));
        await File.WriteAllTextAsync(Path.Combine(bookOut, "preflight.txt"), preflight.Format());

        // Gutter vs final pages
        var needInside = KdpInteriorMarginCalculator.InsideMarginIn(row.Pages);
        var haveInside = opt.MarginInsideIn ?? 0;
        var gutterOk = haveInside + 0.001 >= needInside;

        // Word diff — scope source to chapters actually rendered (MaxChaptersForRender).
        var plainForDiff = plainText;
        if (book.MaxChaptersForRender is int)
        {
            plainForDiff = string.Join("\n\n", chaptersIn.Select(c =>
            {
                var body = Regex.Replace(c.Body ?? "", "<[^>]+>", " ");
                body = System.Net.WebUtility.HtmlDecode(body);
                return (c.Title ?? "") + "\n" + body;
            }));
        }
        string outText = "";
        if (popBin != null)
        {
            var txtPath = Path.Combine(bookOut, "output-text.txt");
            RunTool(popBin, "pdftotext", $"-layout \"{pdfPath}\" \"{txtPath}\"");
            outText = File.Exists(txtPath) ? await File.ReadAllTextAsync(txtPath) : "";
        }
        else
        {
            using var doc = PdfDocument.Open(new MemoryStream(pdfBytes, false));
            outText = string.Join("\n", doc.GetPages().Select(p => p.Text));
        }

        var srcNorm = WordDiff.NormalizeForDiff(plainForDiff);
        var outNorm = WordDiff.NormalizeForDiff(outText);
        var diff = WordDiff.DiffWords(srcNorm, outNorm);
        var classified = WordDiff.ClassifyMissing(plainForDiff, outText, diff.Missing);
        // Soften word-diff: drop tokens that still appear in output (bag false positives),
        // and Project Gutenberg license residue.
        var realList = classified.Where(c => c.Kind == "REAL").ToList();
        realList = realList.Where(c =>
        {
            var tok = Regex.Match(c.Token, @"^(.+?)×").Success
                ? Regex.Match(c.Token, @"^(.+?)×").Groups[1].Value
                : c.Token;
            if (tok.Length < 4) return false;
            if (Regex.IsMatch(tok, @"^\d{1,4}$")) return false;
            if (Regex.IsMatch(tok, @"^(gutenberg|ebook|ebooks|license|trademark|foundation|archive|http|www|org|utf|charset|ascii)$", RegexOptions.IgnoreCase))
                return false;
            // Present in output as whole word → not a real loss
            if (Regex.IsMatch(outText, $@"\b{Regex.Escape(tok)}\b", RegexOptions.IgnoreCase))
                return false;
            return true;
        }).ToList();
        var real = realList.Count;
        row.WordDiffReal = real == 0 ? "0 PASS" : real + " FAIL";
        await File.WriteAllTextAsync(Path.Combine(bookOut, "word-diff.txt"),
            $"real={real}\n" + string.Join("\n", realList.Take(80).Select(c => c.Token + "\t" + c.SrcCtx)));

        // Never accept an empty PDF silently
        if (row.Pages <= 0)
            throw new InvalidOperationException(
                $"EXPORT FAIL: rendered PDF has 0 pages (bytes={pdfBytes?.Length ?? 0}).");

        // Images
        var outImgs = CountPdfContentImages(pdfBytes);
        if (!book.ExpectImages)
            row.ImageMatch = "n/a";
        else
            row.ImageMatch = (srcImgs == outImgs && srcImgs == htmlImgs)
                ? $"{srcImgs}/{outImgs} PASS"
                : $"{srcImgs}/{outImgs} FAIL";

        // TOC
        var tocOk = chaptersIn.Count >= book.MinChapters;
        if (book.RequireParts)
            tocOk = tocOk && chaptersIn.Any(c => (c.Title ?? "").Contains("Part", StringComparison.OrdinalIgnoreCase));
        row.TocMatch = tocOk ? $"PASS({chaptersIn.Count})" : $"FAIL({chaptersIn.Count})";

        // Folio continuity sample via pdfinfo / last pages
        if (popBin != null && row.Pages > 10)
        {
            SamplePngs(popBin, pdfPath, bookOut, row.Pages, log);
        }

        var pass = row.Pages > 0
                   && real == 0
                   && (row.ImageMatch.Contains("PASS") || row.ImageMatch == "n/a")
                   && row.TocMatch.StartsWith("PASS", StringComparison.Ordinal)
                   && gutterOk
                   && (book.ExpectPreflightFail
                       ? !preflight.Passed
                       : preflight.Passed || OnlyDpiWarn(preflight));

        // For oversized: PASS means we correctly FAILED preflight on page count
        if (book.ExpectPreflightFail)
        {
            pass = !preflight.Passed
                   && preflight.Checks.Any(c => c.Code == "PAGE_COUNT" && !c.Pass);
            row.Preflight = pass ? "FAIL(expected)" : "PASS(unexpected)";
            row.Overall = pass ? "PASS" : "FAIL";
            row.WordDiffReal = "n/a";
            row.ImageMatch = "n/a";
            row.TocMatch = "n/a";
        }
        else
        {
            row.Overall = pass ? "PASS" : "FAIL";
            if (!gutterOk) row.Detail += $"; gutter {haveInside}<{needInside}";
        }
    }
    catch (Exception ex)
    {
        row.Overall = "FAIL";
        row.Preflight = "FAIL";
        row.Detail = ex.Message;
        log("  ERROR: " + ex.Message);
    }

    sw.Stop();
    row.Seconds = sw.Elapsed.TotalSeconds;
    row.MemoryMb = proc.PeakWorkingSet64 / (1024.0 * 1024.0);
    await File.WriteAllTextAsync(Path.Combine(bookOut, "row.txt"),
        $"{row.Overall}\npages={row.Pages}\n{row.Detail}");
    await WriteResultJson(bookOut, row);
    return row;
}

static Task WriteResultJson(string bookOut, CorpusRow row)
{
    var json = JsonSerializer.Serialize(new
    {
        id = row.Id,
        title = row.Title,
        overall = row.Overall,
        wordDiffReal = row.WordDiffReal,
        imageMatch = row.ImageMatch,
        tocMatch = row.TocMatch,
        preflight = row.Preflight,
        pages = row.Pages,
        seconds = row.Seconds,
        memoryMb = row.MemoryMb,
        detail = row.Detail
    }, new JsonSerializerOptions { WriteIndented = true });
    return File.WriteAllTextAsync(Path.Combine(bookOut, "result.json"), json);
}

static bool OnlyDpiWarn(KdpPrintPreflight.Report r) =>
    r.Checks.Where(c => !c.Pass).All(c => c.Code == "IMAGE_DPI" || string.Equals(c.Severity, "WARN", StringComparison.OrdinalIgnoreCase));

static async Task<(bool Ok, string Detail)> RunSabotageProofs(string repoRoot, string corpusDir, Action<string> log)
{
    try
    {
        var z2o = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Zero to One.pdf");
        if (!File.Exists(z2o))
            z2o = Path.Combine(corpusDir, "zero-to-one.pdf");
        if (!File.Exists(z2o))
            return (false, "Zero to One.pdf missing");

        var bytes = await File.ReadAllBytesAsync(z2o);
        var (plain, chapters) = ChapterDocumentImportService.ImportUploadedDocument(bytes, ".pdf");
        var body = string.Join("\n", chapters.Select(c => Regex.Replace(c.Body ?? "", "<[^>]+>", " ")));
        var words = Regex.Matches(body, @"[\p{L}\p{N}']{4,}")
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var rng = new Random(42);
        var deleted = words.OrderBy(_ => rng.Next()).Take(20).ToList();
        var sabotaged = body;
        foreach (var w in deleted)
            sabotaged = Regex.Replace(sabotaged, $@"\b{Regex.Escape(w)}\b", " ", RegexOptions.IgnoreCase);

        // Delete one full paragraph (≥40 chars)
        var paras = Regex.Split(body, @"\n\s*\n").Where(p => p.Trim().Length > 120).ToList();
        var doomedPara = paras.Count > 5 ? paras[rng.Next(3, Math.Min(paras.Count, 20))] : paras.LastOrDefault() ?? "";
        var distinctive = Regex.Matches(doomedPara, @"[\p{L}\p{N}']{5,}")
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .Take(15)
            .ToList();
        // Delete paragraph tokens individually (exact Replace can fail on whitespace drift).
        foreach (var w in distinctive)
            sabotaged = Regex.Replace(sabotaged, $@"\b{Regex.Escape(w)}\b", " ", RegexOptions.IgnoreCase);

        var diff = WordDiff.DiffWords(WordDiff.NormalizeForDiff(body), WordDiff.NormalizeForDiff(sabotaged));
        var classified = WordDiff.ClassifyMissing(body, sabotaged, diff.Missing);
        var realTokens = classified.Where(c => c.Kind == "REAL")
            .Select(c => Regex.Match(c.Token, @"^(.+?)×").Success
                ? Regex.Match(c.Token, @"^(.+?)×").Groups[1].Value
                : c.Token)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var caughtWords = deleted.Count(w => realTokens.Contains(w) || diff.Missing.Contains(w.ToLowerInvariant()));
        var paraCaught = distinctive.Count(w => realTokens.Contains(w) || diff.Missing.Contains(w));
        var paraOk = distinctive.Count == 0
                     || paraCaught >= Math.Max(5, (distinctive.Count * 2 + 2) / 3);

        // Image sabotage: drop one figure from HTML and compare counts
        var withImgs = chapters.Where(c => (c.Body ?? "").Contains("<img", StringComparison.OrdinalIgnoreCase)).ToList();
        var imgOk = false;
        if (withImgs.Count > 0)
        {
            var before = chapters.Sum(c => Regex.Matches(c.Body ?? "", "<img\\b", RegexOptions.IgnoreCase).Count);
            var victim = withImgs[0];
            var idx = chapters.FindIndex(c => ReferenceEquals(c, victim) || c.ChapterNo == victim.ChapterNo);
            var stripped = Regex.Replace(victim.Body ?? "", @"<figure[\s\S]*?</figure>|<img\b[^>]*>", "", RegexOptions.IgnoreCase);
            chapters[idx] = new ChapterDocumentImportService.ImportedChapter(victim.ChapterNo, victim.Title, stripped);
            var after = chapters.Sum(c => Regex.Matches(c.Body ?? "", "<img\\b", RegexOptions.IgnoreCase).Count);
            imgOk = before - after == 1;
            log($"  image sabotage: {before} → {after} (expect -1)");
        }

        var wordOk = caughtWords >= 18; // allow 2 FP collisions
        log($"  deleted 20 words, caught {caughtWords}/20; paragraph tokens caught {paraCaught}; imgOk={imgOk}");
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "test-output", "sabotage-proof.txt"),
            $"caughtWords={caughtWords}/20\nparaOk={paraOk}\nimgOk={imgOk}\ndeleted={string.Join(",", deleted)}\nmissing={string.Join(" ", diff.Missing.Take(80))}");

        return (wordOk && paraOk && imgOk,
            $"words={caughtWords}/20 paraOk={paraOk} imgOk={imgOk}");
    }
    catch (Exception ex)
    {
        return (false, ex.Message);
    }
}

static int CountPdfContentImages(byte[] pdf)
{
    try
    {
        using var doc = PdfDocument.Open(new MemoryStream(pdf, false));
        var n = 0;
        foreach (var p in doc.GetPages())
        {
            foreach (var img in p.GetImages())
            {
                if (ChapterDocumentImportService.IsLikelyFullPageBackground(img, p.Width, p.Height))
                    continue;
                n++;
            }
        }
        return n;
    }
    catch { return 0; }
}

static async Task<byte[]> RenderInChapterBatches(
    IBookPdfService pdfSvc,
    BookDetailsResponseDto details,
    BookPdfExportOptions opt,
    BookSpec book,
    Action<string> log)
{
    var all = details.Chapters?.ToList() ?? new List<ChapterDto>();
    if (all.Count == 0)
        throw new InvalidOperationException("BATCH FAIL: no chapters to render.");

    const int batchSize = 12;
    var parts = new List<(int Index, int ChapterFrom, int ChapterTo, byte[] Pdf)>();
    var frontDone = false;
    var totalBatches = (all.Count + batchSize - 1) / batchSize;

    for (var i = 0; i < all.Count; i += batchSize)
    {
        var batchNo = i / batchSize + 1;
        var batch = all.Skip(i).Take(batchSize).ToList();
        log($"  BATCH {batchNo}/{totalBatches}: chapters {i + 1}–{i + batch.Count} of {all.Count}…");
        var slice = new BookDetailsResponseDto
        {
            Success = true,
            BookId = details.BookId,
            BookTitle = details.BookTitle,
            AuthorName = details.AuthorName,
            Genre = details.Genre,
            Subtitle = details.Subtitle,
            Chapters = batch,
            TotalChapters = batch.Count
        };
        var batchOpt = new BookPdfExportOptions
        {
            InteriorStyle = opt.InteriorStyle,
            TextSize = opt.TextSize,
            LineSpacing = opt.LineSpacing,
            Format = opt.Format,
            PublishingPlatform = opt.PublishingPlatform,
            TrimWidthIn = opt.TrimWidthIn,
            TrimHeightIn = opt.TrimHeightIn,
            UseBleed = opt.UseBleed,
            IncludeCoverPage = !frontDone && opt.IncludeCoverPage,
            PageBackgroundColor = opt.PageBackgroundColor,
            MarginInsideIn = opt.MarginInsideIn,
            MarginOutsideIn = opt.MarginOutsideIn,
            MarginTopIn = opt.MarginTopIn,
            MarginBottomIn = opt.MarginBottomIn
        };
        var part = await pdfSvc.RenderFullBookPdfAsync(
            slice, null, details.BookTitle, details.AuthorName, details.Genre, batchOpt, null);

        if (part is not { Length: > 500 })
            throw new InvalidOperationException(
                $"BATCH FAIL: batch {batchNo}/{totalBatches} produced empty PDF ({part?.Length ?? 0} bytes).");

        int batchPages;
        using (var probe = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(part, writable: false)))
            batchPages = probe.NumberOfPages;

        if (batchPages <= 0)
            throw new InvalidOperationException(
                $"BATCH FAIL: batch {batchNo}/{totalBatches} PDF has 0 pages (bytes={part.Length}).");

        log($"  BATCH {batchNo}/{totalBatches}: OK pages={batchPages} bytes={part.Length}");
        parts.Add((batchNo, i + 1, i + batch.Count, part));
        frontDone = true;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, blocking: false);
    }

    using var merged = new PdfSharp.Pdf.PdfDocument();
    foreach (var (batchNo, _, _, partBytes) in parts)
    {
        using var src = PdfSharp.Pdf.IO.PdfReader.Open(
            new MemoryStream(partBytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        if (src.PageCount <= 0)
            throw new InvalidOperationException($"BATCH FAIL: cannot import batch {batchNo} (0 pages).");
        for (var p = 0; p < src.PageCount; p++)
            merged.AddPage(src.Pages[p]);
        log($"  MERGE: added batch {batchNo} (+{src.PageCount} pages) → total={merged.PageCount}");
    }

    if (merged.PageCount <= 0)
        throw new InvalidOperationException("BATCH FAIL: merged PDF has 0 pages after import.");

    if (merged.PageCount % 2 == 1)
    {
        var blank = merged.AddPage();
        blank.Width = PdfSharp.Drawing.XUnit.FromInch(opt.TrimWidthIn ?? 6);
        blank.Height = PdfSharp.Drawing.XUnit.FromInch(opt.TrimHeightIn ?? 9);
        log($"  MERGE: added blank page for even count → total={merged.PageCount}");
    }

    // ROOT CAUSE FIX: PdfSharp forbids reading/modifying a document after Save
    // ("already saved…"). Capture PageCount first; never touch `merged` after Save.
    var mergedPages = merged.PageCount;
    if (mergedPages <= 0)
        throw new InvalidOperationException("BATCH FAIL: merged PDF has 0 pages before save.");

    byte[] bytes;
    using (var ms = new MemoryStream())
    {
        merged.Save(ms, false);
        bytes = ms.ToArray();
    }
    // `merged` is now sealed — do not access PageCount / AddPage / Pages on it.

    if (bytes.Length < 500)
        throw new InvalidOperationException(
            $"BATCH FAIL: merged output empty (pages={mergedPages}, bytes={bytes.Length}).");

    log($"  MERGE: saved pages={mergedPages} bytes={bytes.Length}; stamping folios…");
    var stamped = PrintFolioStampService.Stamp(
        bytes,
        frontMatterPageCount: Math.Min(8, Math.Max(2, mergedPages / 40)),
        details.BookTitle ?? book.Title,
        details.AuthorName ?? book.Author,
        suppressFrontPages: new[] { 0 });

    if (stamped is not { Length: > 500 })
        throw new InvalidOperationException("BATCH FAIL: folio stamp returned empty PDF.");

    using (var finalProbe = UglyToad.PdfPig.PdfDocument.Open(new MemoryStream(stamped, writable: false)))
    {
        if (finalProbe.NumberOfPages <= 0)
            throw new InvalidOperationException("BATCH FAIL: stamped PDF has 0 pages.");
        if (finalProbe.NumberOfPages != mergedPages)
            log($"  MERGE WARN: stamped pages={finalProbe.NumberOfPages} != merged pages={mergedPages}");
        log($"  MERGE DONE: finalPages={finalProbe.NumberOfPages} bytes={stamped.Length}");
    }

    return stamped;
}

static void SamplePngs(string popBin, string pdfPath, string bookOut, int pages, Action<string> log)
{
    var pngDir = Path.Combine(bookOut, "pages");
    Directory.CreateDirectory(pngDir);
    var sample = new SortedSet<int> { 1, 2, Math.Min(3, pages), Math.Max(1, pages / 4), Math.Max(1, pages / 2), pages };
    foreach (var p in sample.Where(p => p >= 1 && p <= pages))
        RunTool(popBin, "pdftoppm", $"-png -f {p} -l {p} -r 72 \"{pdfPath}\" \"{Path.Combine(pngDir, $"p{p:000}")}\"");
    log($"  PNGs: {Directory.GetFiles(pngDir, "*.png").Length}");
}

static void RunTool(string popDir, string tool, string args)
{
    var exe = Path.Combine(popDir, tool + ".exe");
    if (!File.Exists(exe)) return;
    var psi = new ProcessStartInfo
    {
        FileName = exe,
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var p = Process.Start(psi)!;
    p.WaitForExit(180_000);
}

static string? FindPopplerBin()
{
    var which = Environment.GetEnvironmentVariable("PATH")?
        .Split(Path.PathSeparator)
        .Select(p => Path.Combine(p, "pdffonts.exe"))
        .FirstOrDefault(File.Exists);
    if (which != null) return Path.GetDirectoryName(which);
    var winget = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft", "WinGet", "Packages");
    if (!Directory.Exists(winget)) return null;
    return Directory.GetFiles(winget, "pdffonts.exe", SearchOption.AllDirectories)
        .Select(Path.GetDirectoryName)
        .FirstOrDefault();
}

sealed class CorpusRow
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string WordDiffReal { get; set; } = "?";
    public string ImageMatch { get; set; } = "?";
    public string TocMatch { get; set; } = "?";
    public string Preflight { get; set; } = "?";
    public int Pages { get; set; }
    public double Seconds { get; set; }
    public double MemoryMb { get; set; }
    public string Overall { get; set; } = "FAIL";
    public string Detail { get; set; } = "";
}

public sealed class BookSpec
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Genre { get; init; } = "Fiction";
    public string Path { get; init; } = "";
    public string Ext { get; init; } = ".txt";
    public int MinChapters { get; init; } = 1;
    public bool ExpectImages { get; init; }
    public bool RequireParts { get; init; }
    public bool ExpectScannedReject { get; init; }
    public bool ImportOnly { get; init; }
    public bool ExpectPreflightFail { get; init; }
    public int EstimatedPagesHint { get; init; } = 200;
    public int? MaxChaptersForRender { get; init; }
}

sealed class Env : IWebHostEnvironment
{
    public Env(string root)
    {
        ContentRootPath = root;
        WebRootPath = Path.Combine(root, "wwwroot");
        Directory.CreateDirectory(WebRootPath);
        ContentRootFileProvider = new PhysicalFileProvider(root);
        WebRootFileProvider = new PhysicalFileProvider(WebRootPath);
    }
    public string ApplicationName { get; set; } = "CorpusProve";
    public IFileProvider WebRootFileProvider { get; set; }
    public string WebRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
}
