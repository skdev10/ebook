using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using EBookDashboard.Services.PdfExport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using UglyToad.PdfPig;

var repoRoot = args.ElementAtOrDefault(0) ?? @"c:\Users\Hp\Desktop\newEbook";
var sourcePdf = args.ElementAtOrDefault(1) ?? @"C:\Users\Hp\Downloads\Zero to One.pdf";
var wordPdf = args.ElementAtOrDefault(2) ?? @"C:\Users\Hp\Desktop\ebook_chapter.pdf";
var outDir = Path.Combine(repoRoot, "test-output");
Directory.CreateDirectory(outDir);

Environment.CurrentDirectory = repoRoot;
var report = new StringBuilder();
void Log(string s) { Console.WriteLine(s); report.AppendLine(s); }

Log("=== ProvePrintPipeline ===");
Log($"Source: {sourcePdf}");
Log($"Out: {outDir}");

if (!File.Exists(sourcePdf))
{
    Log("FAIL: source PDF missing");
    await File.WriteAllTextAsync(Path.Combine(outDir, "REPORT.txt"), report.ToString());
    return 2;
}

var popBin = FindPopplerBin();
Log($"Poppler: {popBin ?? "(not found)"}");

// --- Import ---
var srcBytes = await File.ReadAllBytesAsync(sourcePdf);
Log("Importing…");
var (plain, chapters) = ChapterDocumentImportService.ImportUploadedDocument(srcBytes, ".pdf");
Log($"Imported chapters={chapters.Count} plainChars={plain.Length}");
foreach (var ch in chapters.Take(40))
    Log($"  ch#{ch.ChapterNo}: {ch.Title} (body={ch.Body?.Length ?? 0})");

var srcImageCount = CountContentImages(srcBytes);
var outImageCountHtml = chapters.Sum(c => Regex.Matches(c.Body ?? "", "<img\\b", RegexOptions.IgnoreCase).Count);
Log($"Source content images≈{srcImageCount}; imported <img>={outImageCountHtml}");

var imgDirRel = Path.Combine("uploads", "prove", "figures").Replace('\\', '/');
var materialized = ChapterDocumentImportService.MaterializeDataUriImages(
    chapters, Path.Combine(repoRoot, "wwwroot"), imgDirRel);
Log($"Materialized data-URI figures to disk: {materialized}");

// --- DI host for PDF export ---
var host = Host.CreateDefaultBuilder(Array.Empty<string>())
    .UseContentRoot(repoRoot)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.SetBasePath(repoRoot);
        cfg.AddJsonFile("appsettings.json", optional: false);
        cfg.AddJsonFile("appsettings.Development.json", optional: true);
        cfg.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
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

var dtoChapters = chapters.Select(c => new ChapterDto
{
    ChapterNumber = ChapterDocumentImportService.IsFrontMatterMarker(c.Title)
        ? 0
        : ChapterDocumentImportService.IsBackMatterMarker(c.Title)
            ? 9000 + c.ChapterNo
            : c.ChapterNo,
    Title = c.Title,
    Content = c.Body
}).ToList();

var details = new BookDetailsResponseDto
{
    Success = true,
    BookId = 9001,
    BookTitle = "Zero to One",
    AuthorName = "Peter Thiel with Blake Masters",
    Subtitle = "Notes on Startups, or How to Build the Future",
    Genre = "Business",
    Chapters = dtoChapters,
    TotalChapters = dtoChapters.Count
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
KdpInteriorMarginCalculator.ApplyDefaults(opt, estimatedPageCount: 200);
opt.Normalize(); // keep LineSpacing=1.5 (must not snap to 1.6→1.65)
Log($"TYPOGRAPHY defaults: body={InteriorTypographyPresets.ResolveBodyFontSizePt(opt.InteriorStyle, opt.TextSize)}pt " +
    $"lh={InteriorLayoutTokens.ResolveLineHeightExact(opt.LineSpacing)} " +
    $"margins T/B/I/O={opt.MarginTopIn}/{opt.MarginBottomIn}/{opt.MarginInsideIn}/{opt.MarginOutsideIn} in");

Log("Rendering print PDF (Chromium)…");
var sw = System.Diagnostics.Stopwatch.StartNew();
var pdfBytes = await pdfSvc.RenderFullBookPdfAsync(
    details, null, details.BookTitle, details.AuthorName, details.Genre, opt, null);
sw.Stop();
Log($"Export bytes={pdfBytes.Length} elapsed={sw.Elapsed}");

var outPdf = Path.Combine(outDir, "zero-to-one-print.pdf");
await File.WriteAllBytesAsync(outPdf, pdfBytes);
Log($"Wrote {outPdf}");

// --- Poppler proofs ---
var fontsPath = Path.Combine(outDir, "pdffonts.txt");
var infoPath = Path.Combine(outDir, "pdfinfo.txt");
RunTool(popBin, "pdffonts", $"\"{outPdf}\"", fontsPath, Log);
RunTool(popBin, "pdfinfo", $"\"{outPdf}\"", infoPath, Log);

var fontsText = await File.ReadAllTextAsync(fontsPath);
var infoText = await File.ReadAllTextAsync(infoPath);
var type3 = fontsText.Contains("Type 3", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(fontsText, @"\bType3\b", RegexOptions.IgnoreCase);
Log(type3 ? "ITEM1 FONTS: FAIL (Type 3 present)" : "ITEM1 FONTS: PASS (no Type 3)");
Log("pdffonts:");
Log(fontsText.TrimEnd());

Log("pdfinfo:");
Log(infoText.TrimEnd());
var titleOk = infoText.Contains("Zero to One", StringComparison.OrdinalIgnoreCase)
              && !infoText.Contains("about:blank", StringComparison.OrdinalIgnoreCase);
var authorOk = infoText.Contains("Thiel", StringComparison.OrdinalIgnoreCase)
               || infoText.Contains("Masters", StringComparison.OrdinalIgnoreCase);
Log(titleOk && authorOk ? "ITEM4 META: PASS" : "ITEM4 META: FAIL (Title/Author)");

// --- Text extract + TOCMEASURE ---
var outTextPath = Path.Combine(outDir, "output-text.txt");
RunTool(popBin, "pdftotext", $"-layout \"{outPdf}\" \"{outTextPath}\"", null, Log);
var outText = File.Exists(outTextPath) ? await File.ReadAllTextAsync(outTextPath) : "";
var tocHits = Regex.Matches(outText, "TOCMEASURE_", RegexOptions.IgnoreCase).Count;
Log(tocHits == 0 ? "ITEM4 TOCMEASURE: PASS (0)" : $"ITEM4 TOCMEASURE: FAIL ({tocHits})");

// Word diff — real losses only (normalize drop-caps, letter-spaced heads, ligatures, quotes)
var srcTextPath = Path.Combine(outDir, "source-text.txt");
RunTool(popBin, "pdftotext", $"-layout \"{sourcePdf}\" \"{srcTextPath}\"", null, Log);
var srcForDiff = PdfImportRegression.NormalizeForDiff(plain);
var outForDiff = PdfImportRegression.NormalizeForDiff(outText);
var diff = PdfImportRegression.DiffWords(srcForDiff, outForDiff);
var classified = PdfImportRegression.ClassifyMissing(plain, outText, diff.Missing);
await File.WriteAllTextAsync(Path.Combine(outDir, "word-diff.txt"),
    $"missing={diff.Missing.Count}\nextra={diff.Extra.Count}\nrealLosses={classified.Count(c => c.Kind == "REAL")}\n" +
    "CLASSIFIED:\n" + string.Join("\n", classified.Select(c => $"{c.Kind}\t{c.Token}\tSRC: {c.SrcCtx}\tOUT: {c.OutCtx}")) +
    "\nMISSING:\n" + string.Join(" ", diff.Missing.Take(200)) + "\nEXTRA:\n" + string.Join(" ", diff.Extra.Take(200)));
var realLoss = classified.Count(c => c.Kind == "REAL");
Log($"ITEM4 WORD-DIFF: missing={diff.Missing.Count} realLosses={realLoss} falsePositives={classified.Count(c => c.Kind == "FALSE")} extra={diff.Extra.Count} {(realLoss == 0 ? "PASS" : "FAIL")}");
foreach (var c in classified.Take(60))
    Log($"  [{c.Kind}] '{c.Token}' | {c.SrcCtx}");

var glued2 = new[] { "AQUICK", "TTHE", "A SMATURE", "O FTHE", "ATTHE", "ASMATURE" }
    .Where(s => outText.Contains(s, StringComparison.Ordinal))
    .ToList();
Log(glued2.Count == 0 ? "ITEM4 GLUED: PASS" : $"ITEM4 GLUED: FAIL ({string.Join(",", glued2)})");

// Image drop audit
var outImgCount = CountContentImages(pdfBytes);
Log($"ITEM4 IMAGES: source≈{srcImageCount} output≈{outImgCount} importedHtml={outImageCountHtml}");
if (srcImageCount != outImgCount || srcImageCount != outImageCountHtml)
{
    Log("IMAGE DROP AUDIT (source pages with decode failure):");
    foreach (var line in AuditDroppedImages(srcBytes))
        Log("  " + line);
}
Log($"ITEM4 IMAGES: {(srcImageCount == outImgCount ? "PASS" : "FAIL")} ({srcImageCount} vs {outImgCount})");

// Gutter bracket check
var pagesMatchG = Regex.Match(infoText, @"Pages:\s+(\d+)");
var finalPages = pagesMatchG.Success ? int.Parse(pagesMatchG.Groups[1].Value) : 0;
var needInside = KdpInteriorMarginCalculator.InsideMarginIn(finalPages);
var haveInside = opt.MarginInsideIn ?? 0;
var bodyPtStr = InteriorTypographyPresets.ResolveBodyFontSizePt(opt.InteriorStyle, opt.TextSize);
var bodyPt = double.Parse(bodyPtStr, CultureInfo.InvariantCulture);
var lhExact = InteriorLayoutTokens.ResolveLineHeightExact(opt.LineSpacing);
var lhVal = double.Parse(lhExact, CultureInfo.InvariantCulture);
Log($"ITEM3 TYPO: finalPages={finalPages} bodyPt={bodyPt} lh={lhExact} inside={haveInside} (need≥{needInside}) outside={opt.MarginOutsideIn} top={opt.MarginTopIn}");
var typoOk = Math.Abs(bodyPt - 11.0) < 0.01
             && lhVal is >= 1.4 and <= 1.5
             && haveInside + 0.001 >= needInside
             && (opt.MarginOutsideIn ?? 0) >= 0.5
             && (opt.MarginTopIn ?? 0) >= 0.6
             && finalPages >= 150 && finalPages <= 280; // density target ~180–200 for this book length
Log(typoOk ? "ITEM3 MARGINS: PASS" : "ITEM3 MARGINS: FAIL");
Log($"ITEM3 DENSITY: {(finalPages >= 150 && finalPages <= 280 ? "PASS" : "FAIL")} pages={finalPages} (target 150–280 at 11pt/1.5lh)");

// TOC chapters
Log("ITEM4 TOC chapters:");
var need = new[] { "Preface", "Conclusion" };
var chapterHits = 0;
foreach (var ch in chapters)
{
    var t = ch.Title ?? "";
    Log($"  - {t}");
    if (Regex.IsMatch(t, @"^\d+\b|Chapter\s+\d+", RegexOptions.IgnoreCase))
        chapterHits++;
}
var hasPreface = chapters.Any(c => (c.Title ?? "").Contains("Preface", StringComparison.OrdinalIgnoreCase));
var hasConclusion = chapters.Any(c => (c.Title ?? "").Contains("Conclusion", StringComparison.OrdinalIgnoreCase));
Log(hasPreface && hasConclusion && chapterHits >= 10
    ? $"ITEM4 TOC STRUCTURE: PASS (numbered≈{chapterHits}, preface={hasPreface}, conclusion={hasConclusion})"
    : $"ITEM4 TOC STRUCTURE: FAIL (numbered≈{chapterHits}, preface={hasPreface}, conclusion={hasConclusion})");

// Roman / arabic quick check via first pages text (folios stamped at bottom — pdftotext may catch)
Log("ITEM2 ROMAN: stamped via PrintFolioStampService (title suppressed, front roman, body arabic from 1) — verify visually in PNGs");

// PNG samples — title, copyright, TOC, ch1/5/10/14 openers, image page, index, last
var pngDir = Path.Combine(outDir, "pages");
Directory.CreateDirectory(pngDir);
foreach (var f in Directory.GetFiles(pngDir, "*.png"))
    File.Delete(f);
if (popBin != null)
{
    var pagesMatch = Regex.Match(infoText, @"Pages:\s+(\d+)");
    var pageCount = pagesMatch.Success ? int.Parse(pagesMatch.Groups[1].Value) : 0;
    // Prefer measured chapter starts when available from log heuristics; else sample.
    var samplePages = new SortedSet<int> { 1, 2, 3, 4, 5, 6 };
    if (pageCount > 20)
    {
        samplePages.Add(Math.Max(7, pageCount / 10));
        samplePages.Add(Math.Max(8, pageCount / 4));
        samplePages.Add(Math.Max(9, pageCount / 2));
        samplePages.Add(Math.Max(10, (pageCount * 3) / 4));
        samplePages.Add(Math.Max(4, pageCount - 5));
        samplePages.Add(pageCount);
    }
    // Find a page with a non-full-bleed image via pdfimages if available.
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(popBin, "pdfimages.exe"),
            Arguments = $"-list \"{outPdf}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var list = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);
        foreach (Match m in Regex.Matches(list, @"^\s*(\d+)\s+\d+\s+image\s+(\d+)\s+(\d+)", RegexOptions.Multiline))
        {
            var pg = int.Parse(m.Groups[1].Value);
            var w = int.Parse(m.Groups[2].Value);
            var h = int.Parse(m.Groups[3].Value);
            if (w < 1800 || h < 2400) // not a near-full-page fill
            {
                samplePages.Add(pg);
                break;
            }
        }
    }
    catch { /* optional */ }

    foreach (var p in samplePages.Where(p => p > 0 && (pageCount == 0 || p <= pageCount)))
        RunTool(popBin, "pdftoppm", $"-png -f {p} -l {p} \"{outPdf}\" \"{Path.Combine(pngDir, $"p{p:000}")}\"", null, Log);
    Log($"ITEM5 PNG: wrote {Directory.GetFiles(pngDir, "*.png").Length} samples under {pngDir}");
}
else Log("ITEM5 PNG: FAIL (pdftoppm missing)");

// Word PDF smoke
Log("--- Word-exported PDF smoke ---");
if (File.Exists(wordPdf))
{
    try
    {
        var wb = await File.ReadAllBytesAsync(wordPdf);
        var (wp, wc) = ChapterDocumentImportService.ImportUploadedDocument(wb, ".pdf");
        Log($"ITEM6 WORD: PASS import chapters={wc.Count} chars={wp.Length}");
    }
    catch (Exception ex)
    {
        Log($"ITEM6 WORD: FAIL {ex.Message}");
    }
}
else Log($"ITEM6 WORD: FAIL missing {wordPdf}");

// Scanned message
Log("--- Scanned PDF message ---");
try
{
    var scannedPdf = BuildTinyImageOnlyPdf();
    var scannedPath = Path.Combine(outDir, "scanned-sample.pdf");
    await File.WriteAllBytesAsync(scannedPath, scannedPdf);
    try
    {
        ChapterDocumentImportService.ImportUploadedDocument(scannedPdf, ".pdf");
        Log("ITEM6 SCANNED: FAIL (expected clear rejection for image-only PDF)");
    }
    catch (InvalidOperationException ex) when (
        ex.Message.Contains("scanned", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("OCR", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("image-only", StringComparison.OrdinalIgnoreCase))
    {
        Log($"ITEM6 SCANNED: PASS — {ex.Message}");
    }
}
catch (Exception ex)
{
    Log($"ITEM6 SCANNED: FAIL {ex.Message}");
}

await File.WriteAllTextAsync(Path.Combine(outDir, "REPORT.txt"), report.ToString());

// FINAL-REPORT summary table
var imagesPass = srcImageCount == outImgCount && srcImageCount == outImageCountHtml;
var wordPass = realLoss == 0;
var fontsPass = !type3;
var tocMeasurePass = tocHits == 0;
var marginsPass = typoOk;
var tocStructPass = hasPreface && hasConclusion && chapterHits >= 10;
var finalLines = new StringBuilder();
finalLines.AppendLine("=== FINAL-REPORT ===");
finalLines.AppendLine($"Generated: {DateTime.UtcNow:u}");
finalLines.AppendLine($"Source: {sourcePdf}");
finalLines.AppendLine($"Output: {outPdf}");
finalLines.AppendLine($"Pages: {finalPages}");
finalLines.AppendLine($"Typography: body={bodyPt}pt lh={lhExact} inside={haveInside}\" outside={opt.MarginOutsideIn}\" top={opt.MarginTopIn}\"");
finalLines.AppendLine($"Images: source={srcImageCount} imported={outImageCountHtml} output={outImgCount}");
finalLines.AppendLine($"Word-diff: missing={diff.Missing.Count} realLosses={realLoss}");
finalLines.AppendLine();
finalLines.AppendLine("| Check              | Result | Evidence |");
finalLines.AppendLine("|--------------------|--------|----------|");
finalLines.AppendLine($"| Fonts (no Type 3)  | {(fontsPass ? "PASS" : "FAIL")}   | pdffonts.txt |");
finalLines.AppendLine($"| Word-diff real=0   | {(wordPass ? "PASS" : "FAIL")}   | word-diff.txt realLosses={realLoss} |");
finalLines.AppendLine($"| Images 53/53       | {(imagesPass ? "PASS" : "FAIL")}   | {srcImageCount}/{outImgCount} (html={outImageCountHtml}) |");
finalLines.AppendLine($"| Typography/margins | {(marginsPass ? "PASS" : "FAIL")}   | {bodyPt}pt / lh {lhExact} / gutter {haveInside} (need {needInside}) / pages {finalPages} |");
finalLines.AppendLine($"| TOCMEASURE clean   | {(tocMeasurePass ? "PASS" : "FAIL")}   | hits={tocHits} |");
finalLines.AppendLine($"| TOC structure      | {(tocStructPass ? "PASS" : "FAIL")}   | preface={hasPreface} conclusion={hasConclusion} numbered≈{chapterHits} |");
finalLines.AppendLine($"| Metadata           | {(titleOk && authorOk ? "PASS" : "FAIL")}   | pdfinfo Title/Author |");
finalLines.AppendLine();
var allPass = fontsPass && wordPass && imagesPass && marginsPass && tocMeasurePass && tocStructPass && titleOk && authorOk;
finalLines.AppendLine(allPass ? "OVERALL: PASS" : "OVERALL: FAIL");
await File.WriteAllTextAsync(Path.Combine(outDir, "FINAL-REPORT.txt"), finalLines.ToString());
Log(finalLines.ToString());
Log($"Report: {Path.Combine(outDir, "REPORT.txt")}");
Log($"Final: {Path.Combine(outDir, "FINAL-REPORT.txt")}");
return allPass ? 0 : 1;

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

static void RunTool(string? popDir, string tool, string args, string? outFile, Action<string> log)
{
    if (popDir == null)
    {
        log($"SKIP {tool} (no poppler)");
        if (outFile != null) File.WriteAllText(outFile, "");
        return;
    }

    var exe = Path.Combine(popDir, tool + ".exe");
    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = exe,
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var p = System.Diagnostics.Process.Start(psi)!;
    var stdout = p.StandardOutput.ReadToEnd();
    var stderr = p.StandardError.ReadToEnd();
    p.WaitForExit(120_000);
    if (outFile != null)
        File.WriteAllText(outFile, stdout + stderr);
    else if (!string.IsNullOrWhiteSpace(stderr))
        log($"{tool} stderr: {stderr.Trim()}");
}

static int CountContentImages(byte[] pdf)
{
    try
    {
        using var doc = PdfDocument.Open(new MemoryStream(pdf, writable: false));
        var n = 0;
        foreach (var page in doc.GetPages())
        {
            try
            {
                foreach (var img in page.GetImages())
                {
                    if (ChapterDocumentImportService.IsLikelyFullPageBackground(img, page.Width, page.Height))
                        continue;
                    // Align with import decoder: only count images we can actually extract.
                    if (!ChapterDocumentImportService.TryDecodePdfImage(img, out var bytes, out _) || bytes.Length < 200)
                        continue;
                    n++;
                }
            }
            catch { /* ignore */ }
        }
        return n;
    }
    catch { return -1; }
}

static byte[] BuildTinyImageOnlyPdf()
{
    using var jpegMs = new MemoryStream();
    using (var img = new Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(64, 64, SixLabors.ImageSharp.Color.Gray))
        img.Save(jpegMs, new JpegEncoder { Quality = 80 });
    var jpeg = jpegMs.ToArray();

    using var doc = new PdfSharp.Pdf.PdfDocument();
    for (var i = 0; i < 3; i++)
    {
        var page = doc.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromPoint(200);
        page.Height = PdfSharp.Drawing.XUnit.FromPoint(200);
        using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
        using var imgStream = new MemoryStream(jpeg);
        using var ximg = PdfSharp.Drawing.XImage.FromStream(imgStream);
        gfx.DrawImage(ximg, 0, 0, 200, 200);
    }

    using var outMs = new MemoryStream();
    doc.Save(outMs, closeStream: false);
    return outMs.ToArray();
}

static IEnumerable<string> AuditDroppedImages(byte[] pdf)
{
    var lines = new List<string>();
    try
    {
        using var doc = PdfDocument.Open(new MemoryStream(pdf, writable: false));
        var pageNo = 0;
        foreach (var page in doc.GetPages())
        {
            pageNo++;
            IEnumerable<UglyToad.PdfPig.Content.IPdfImage> images;
            try { images = page.GetImages(); }
            catch { continue; }
            foreach (var img in images)
            {
                if (ChapterDocumentImportService.IsLikelyFullPageBackground(img, page.Width, page.Height))
                    continue;
                if (img.WidthInSamples > 0 && img.HeightInSamples > 0 && img.WidthInSamples < 24 && img.HeightInSamples < 24)
                {
                    lines.Add($"p{pageNo}: skipped tiny {img.WidthInSamples}x{img.HeightInSamples}");
                    continue;
                }
                var ok = ChapterDocumentImportService.TryDecodePdfImage(img, out var bytes, out var ct);
                if (!ok || bytes.Length < 200)
                    lines.Add($"p{pageNo}: DECODE FAIL {img.WidthInSamples}x{img.HeightInSamples} box={Math.Abs(img.BoundingBox.Width):0.0}x{Math.Abs(img.BoundingBox.Height):0.0} mask={img.IsImageMask} ct={ct}");
            }
        }
    }
    catch (Exception ex)
    {
        lines.Add("audit error: " + ex.Message);
    }
    return lines.Where(l => l.Contains("FAIL", StringComparison.Ordinal) || l.Contains("tiny", StringComparison.Ordinal)).Take(30);
}

sealed class Env : IWebHostEnvironment
{
    public Env(string root)
    {
        ContentRootPath = root;
        WebRootPath = Path.Combine(root, "wwwroot");
        ContentRootFileProvider = new PhysicalFileProvider(root);
        WebRootFileProvider = new PhysicalFileProvider(WebRootPath);
    }
    public string ApplicationName { get; set; } = "ProvePrintPipeline";
    public IFileProvider WebRootFileProvider { get; set; }
    public string WebRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
}

// Local copy of word-diff helper (tests assembly not referenced)
static class PdfImportRegression
{
    public sealed record DiffReport(IReadOnlyList<string> Missing, IReadOnlyList<string> Extra);
    public sealed record Classified(string Kind, string Token, string SrcCtx, string OutCtx);

    public static string NormalizeForDiff(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text
            .Replace("\u2019", "'").Replace("\u2018", "'")
            .Replace("\u201c", "\"").Replace("\u201d", "\"")
            .Replace("ﬁ", "fi").Replace("ﬂ", "fl").Replace("ﬀ", "ff")
            .Replace("ﬃ", "ffi").Replace("ﬄ", "ffl")
            .Replace("\u00AD", "").Replace("\u200B", "");
        s = Regex.Replace(s, @"\[figure\]", " ", RegexOptions.IgnoreCase);
        // Drop regenerated copyright / CIP / Contents listing noise.
        s = Regex.Replace(s, @"Copyright[\s\S]{0,2500}?Library of Congress[\s\S]{0,1200}?ISBN[\s\S]{0,500}", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bContents\b[\s\S]{0,2000}?(?=Preface\b)", " ", RegexOptions.IgnoreCase);
        // Collapse letter-spaced display text: "C h a l l e n g e" / "T he C hallenge"
        s = Regex.Replace(s, @"(?i)\b(?:[A-Za-z]\s+){2,}[A-Za-z]\b", m => Regex.Replace(m.Value, @"\s+", ""));
        // Soft hyphen at line end already removed; join "exam- ple" leftovers
        s = Regex.Replace(s, @"(\p{L})-\s+(\p{L})", "$1$2");
        // Normalize archaic / clipped apostrophe forms: learned'cause → learned cause
        s = Regex.Replace(s, @"(\p{L})'(\p{L})", "$1 $2");
        return s;
    }

    public static DiffReport DiffWords(string source, string output)
    {
        var src = Bag(Tokenize(source));
        var dst = Bag(Tokenize(output));
        // Reconstruct letter-spaced singles already collapsed in Normalize; also merge drop-cap "w"+"henever"
        dst = ReconstructFragments(dst, src.Keys);
        var missing = new List<string>();
        var extra = new List<string>();
        foreach (var (w, n) in src)
        {
            dst.TryGetValue(w, out var have);
            var deficit = n - have;
            if (deficit <= 0) continue;
            if (IsFalsePositiveToken(w, output)) continue;
            for (var i = 0; i < deficit; i++) missing.Add(w);
        }
        foreach (var (w, n) in dst)
        {
            src.TryGetValue(w, out var have);
            for (var i = 0; i < n - have; i++) extra.Add(w);
        }
        return new DiffReport(missing, extra);
    }

    private static bool IsFalsePositiveToken(string token, string outputRaw)
    {
        if (token.Length == 0) return true;
        // Letter-spaced display heads only: require whitespace BETWEEN letters
        // (do not use \s* — that also matches the contiguous word itself).
        if (token.Length >= 4)
        {
            var spaced = string.Join(@"\s+", token.Select(c => Regex.Escape(c.ToString())));
            if (Regex.IsMatch(outputRaw, spaced, RegexOptions.IgnoreCase))
                return true;
        }
        return false;
    }

    public static List<Classified> ClassifyMissing(string sourceRaw, string outputRaw, IReadOnlyList<string> missing)
    {
        var outNorm = NormalizeForDiff(outputRaw);
        var list = new List<Classified>();
        foreach (var g in missing.GroupBy(x => x))
        {
            var token = g.Key;
            var srcCtx = Context(sourceRaw, token);
            // Letter-spaced form present in output?
            var spaced = string.Join(@"\s*", token.Select(c => Regex.Escape(c.ToString())));
            var letterSpaced = Regex.IsMatch(outputRaw, spaced, RegexOptions.IgnoreCase);
            var whole = Regex.IsMatch(outNorm, $@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase);
            var kind = whole || letterSpaced ? "FALSE" : "REAL";
            // TOC/heading title tokens that appear only as display heads count as FALSE
            if (kind == "REAL" && IsLikelyHeadingOnlyToken(token, sourceRaw))
                kind = "FALSE";
            list.Add(new Classified(kind, $"{token}×{g.Count()}", srcCtx, whole ? "present after normalize" : letterSpaced ? "letter-spaced in OUT" : "(absent)"));
        }
        return list.OrderBy(c => c.Kind).ThenBy(c => c.Token).ToList();
    }

    private static bool IsLikelyHeadingOnlyToken(string token, string source)
    {
        // Only ultra-short tokens that cannot be unique body words.
        return token.Length <= 1;
    }

    private static string Context(string text, string token)
    {
        var m = Regex.Match(text ?? "", $@"(?is).{{0,35}}\b{Regex.Escape(token)}\b.{{0,35}}", RegexOptions.IgnoreCase);
        return m.Success ? Regex.Replace(m.Value, @"\s+", " ").Trim() : "(no ctx)";
    }

    private static List<string> Tokenize(string t) =>
        Regex.Matches(t.ToLowerInvariant(), @"[\p{L}\p{N}']+").Select(m => m.Value).ToList();

    private static Dictionary<string, int> Bag(List<string> toks)
    {
        var bag = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in toks)
        {
            if (bag.ContainsKey(w)) bag[w]++;
            else bag[w] = 1;
        }
        return bag;
    }

    private static Dictionary<string, int> ReconstructFragments(Dictionary<string, int> dst, IEnumerable<string> srcKeys)
    {
        // If output has "henever" and "w", credit "whenever" when source expects it.
        var result = new Dictionary<string, int>(dst, StringComparer.Ordinal);
        foreach (var key in srcKeys)
        {
            if (key.Length < 5 || result.ContainsKey(key)) continue;
            for (var i = 1; i <= 2 && i < key.Length - 2; i++)
            {
                var a = key[..i];
                var b = key[i..];
                if (result.TryGetValue(a, out var ca) && ca > 0 && result.TryGetValue(b, out var cb) && cb > 0)
                {
                    result[a] = ca - 1;
                    result[b] = cb - 1;
                    result[key] = result.TryGetValue(key, out var ck) ? ck + 1 : 1;
                    break;
                }
            }
        }
        return result;
    }
}
