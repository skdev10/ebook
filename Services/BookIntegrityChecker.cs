using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EBookDashboard.Models.DTO;
using HtmlAgilityPack;
using UglyToad.PdfPig;

namespace EBookDashboard.Services;

/// <summary>
/// Compares normalized source words with the print result. A pass requires no
/// deletions or reorders of body text and similarity of at least 99.5%.
/// </summary>
public static class BookIntegrityChecker
{
    public const double PassSimilarity = 0.995;

    public sealed class Report
    {
        public bool Passed { get; set; }
        public double Similarity { get; set; }
        public int SourceWords { get; set; }
        public int OutputWords { get; set; }
        public int Deletions { get; set; }
        public int Insertions { get; set; }
        public int Reorders { get; set; }
        public int SourceImages { get; set; }
        public int OutputImages { get; set; }
        public bool ImagesMatch { get; set; }
        public bool StructureOk { get; set; }
        public int TitlePageCount { get; set; }
        public int CopyrightPageCount { get; set; }
        public int TocPageCount { get; set; }
        /// <summary>Independent source extraction versus imported HTML. 1 when that comparison was not run.</summary>
        public double ImportSimilarity { get; set; } = 1;
        /// <summary>Imported HTML versus the output PDF. 1 when that comparison was not run.</summary>
        public double PrintSimilarity { get; set; } = 1;
        /// <summary>Chapter body only, with generated title, copyright, and contents pages removed.</summary>
        public double BodySimilarity { get; set; } = 1;
        public int BodyDeletions { get; set; }
        public int BodyReorders { get; set; }
        public int BodyInsertions { get; set; }
        public List<DiffHit> BodyHits { get; set; } = new();
        public List<string> SectionLabels { get; set; } = new();
        public List<string> StructureNotes { get; set; } = new();
        public List<DiffHit> Hits { get; set; } = new();
    }

    public sealed class DiffHit
    {
        public string Kind { get; set; } = "";
        public int Page { get; set; }
        public string Context { get; set; } = "";
    }

    public static Report CheckHtml(string? sourceHtml, string? outputHtml, IReadOnlyList<ChapterDto>? chapters = null)
    {
        var sourceWords = Tokenize(PlainFromHtml(sourceHtml));
        var outputWords = Tokenize(PlainFromHtml(outputHtml));
        var report = Diff(sourceWords, outputWords);
        report.SourceImages = CountImages(sourceHtml);
        report.OutputImages = CountImages(outputHtml);
        report.ImagesMatch = report.SourceImages == report.OutputImages;
        ApplyStructure(report, chapters, outputHtml);
        report.ImportSimilarity = report.Similarity;
        report.PrintSimilarity = report.Similarity;
        Seal(report);
        return report;
    }

    /// <summary>
    /// Two comparisons: independent extraction versus the import, then the import versus the output PDF.
    /// Similarity is the lower of the two. Structure cannot pass when that score is below 99%.
    /// </summary>
    public static Report CheckRoundTrip(
        string? independentPlain,
        string? importedHtml,
        byte[] pdfBytes,
        IReadOnlyList<ChapterDto>? chapters = null,
        string? printHtml = null)
    {
        var importDiff = Diff(Tokenize(independentPlain), Tokenize(PlainFromHtml(importedHtml)));
        var report = CheckAgainstPdf(importedHtml, pdfBytes, chapters, printHtml);
        report.ImportSimilarity = importDiff.Similarity;
        report.PrintSimilarity = report.Similarity;
        report.Similarity = Math.Min(report.ImportSimilarity, report.PrintSimilarity);
        report.Deletions += importDiff.Deletions;
        report.Insertions += importDiff.Insertions;
        report.Reorders += importDiff.Reorders;
        foreach (var hit in importDiff.Hits.Take(20))
        {
            hit.Kind = "import-" + hit.Kind;
            report.Hits.Insert(0, hit);
        }
        if (chapters != null)
            ApplyBodyDiff(report, chapters, pdfBytes);
        Seal(report);
        return report;
    }

    /// <summary>
    /// Imported text versus the PDF, including the copyright page. The generated title page
    /// and the generated contents list are left out. The source contents list is removed
    /// before the comparison because the print keeps only the generated contents page.
    /// </summary>
    public static Report DiffBody(IReadOnlyList<ChapterDto>? chapters, byte[] pdfBytes)
    {
        var bodyHtml = string.Concat((chapters ?? Array.Empty<ChapterDto>())
            .Where(ch => !BookHtmlNormalizer.IsTableOfContents(ch))
            .Select(ch => BookHtmlNormalizer.IsTitlePage(ch) || BookHtmlNormalizer.IsCopyright(ch)
                ? BookHtmlNormalizer.CopyrightProse(ch.Content)
                : ch.Content));
        var (outputWords, outputPages) = ExtractPdfWords(pdfBytes, skipFrontMatter: true);
        return Diff(Tokenize(PlainFromHtml(bodyHtml)), outputWords, outputPages, hitLimit: 500);
    }

    private static void ApplyBodyDiff(Report report, IReadOnlyList<ChapterDto> chapters, byte[] pdfBytes)
    {
        var body = DiffBody(chapters, pdfBytes);
        report.BodySimilarity = body.Similarity;
        report.BodyDeletions = body.Deletions;
        report.BodyReorders = body.Reorders;
        report.BodyInsertions = body.Insertions;
        report.BodyHits = body.Hits;
        if (body.Deletions > 0 || body.Reorders > 0)
            report.StructureNotes.Add($"Body text has {body.Deletions} deletions and {body.Reorders} reorders.");
    }

    public static Report CheckAgainstPdf(
        string? sourceHtml,
        byte[] pdfBytes,
        IReadOnlyList<ChapterDto>? chapters = null,
        string? printHtml = null)
    {
        var (outputWords, outputPages) = ExtractPdfWords(pdfBytes);
        var sourceWords = Tokenize(PlainFromHtml(sourceHtml));
        var report = Diff(sourceWords, outputWords, outputPages);
        report.SourceImages = CountImages(sourceHtml);
        report.OutputImages = CountPdfImages(pdfBytes);
        report.ImagesMatch = report.SourceImages == report.OutputImages
                             || (report.SourceImages == 0 && report.OutputImages == 0);
        ApplyStructure(report, chapters, printHtml ?? "");
        CountPrintHtml(report, printHtml);
        ApplyFrontMatterIntegrity(report, printHtml);
        if (report.TitlePageCount == 0 && report.CopyrightPageCount == 0)
            CountPdfFrontMatter(report, pdfBytes);
        report.PrintSimilarity = report.Similarity;
        if (!report.ImagesMatch)
            report.StructureNotes.Add($"Images {report.SourceImages} in the import, {report.OutputImages} in the PDF.");
        Seal(report);
        return report;
    }

    /// <summary>pdftotext -layout when Poppler is installed. This is the independent extraction.</summary>
    public static string? TryExtractLayoutText(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            return null;
        var exe = ResolvePdftotext();
        if (exe == null)
            return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            psi.ArgumentList.Add("-layout");
            psi.ArgumentList.Add("-enc");
            psi.ArgumentList.Add("UTF-8");
            psi.ArgumentList.Add(pdfPath);
            psi.ArgumentList.Add("-");
            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
                return null;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit(180000);
            return process.ExitCode == 0 ? text : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Drop running headers, footers, and bare page numbers from a pdftotext -layout dump.</summary>
    public static string StripLayoutFurniture(string? layout)
    {
        if (string.IsNullOrEmpty(layout))
            return "";
        var pages = layout.Split('\f');
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            var lines = page.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0)
                continue;
            CountFurniture(counts, lines[0]);
            if (lines.Count > 1)
                CountFurniture(counts, lines[^1]);
        }

        var drop = new HashSet<string>(
            counts.Where(kv => kv.Value >= 4).Select(kv => kv.Key),
            StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var page in pages)
        {
            foreach (var raw in page.Split('\n'))
            {
                var key = FurnitureKey(raw);
                if (key.Length > 0 && drop.Contains(key))
                    continue;
                if (Regex.IsMatch(raw.Trim(), @"^(?:\d{1,4}|[ivxlcdm]{1,6})$", RegexOptions.IgnoreCase))
                    continue;
                sb.AppendLine(raw);
            }
        }

        return sb.ToString();
    }

    public static string ToJson(Report report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

    public static string ToHtml(Report report)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><meta charset=\"utf-8\"><title>Book integrity</title><body>");
        sb.Append("<h1>").Append(report.Passed ? "Pass" : "Fail").Append("</h1>");
        sb.Append("<p>Similarity ").Append(report.Similarity.ToString("P2"))
            .Append(" · source words ").Append(report.SourceWords)
            .Append(" · deletions ").Append(report.Deletions)
            .Append(" · reorders ").Append(report.Reorders)
            .Append(" · insertions ").Append(report.Insertions)
            .Append(" · images ").Append(report.SourceImages).Append(" → ").Append(report.OutputImages)
            .Append("</p><ul>");
        foreach (var note in report.StructureNotes)
            sb.Append("<li>").Append(WebUtility.HtmlEncode(note)).Append("</li>");
        foreach (var hit in report.Hits.Take(80))
            sb.Append("<li><strong>").Append(WebUtility.HtmlEncode(hit.Kind)).Append("</strong> ")
                .Append(WebUtility.HtmlEncode(hit.Context)).Append("</li>");
        sb.Append("</ul></body>");
        return sb.ToString();
    }

    /// <summary>Writes JSON and HTML next to each other. Returns the JSON path.</summary>
    public static string Save(string directory, string bookKey, Report report)
    {
        Directory.CreateDirectory(directory);
        var safe = Regex.Replace(bookKey, @"[^\w\-]+", "-");
        var jsonPath = Path.Combine(directory, safe + ".json");
        File.WriteAllText(jsonPath, ToJson(report));
        File.WriteAllText(Path.Combine(directory, safe + ".html"), ToHtml(report));
        return jsonPath;
    }

    public static int CountImages(string? html) =>
        Regex.Matches(html ?? "", "<img\\b", RegexOptions.IgnoreCase).Count;

    /// <summary>Reading-order text of a PDF, with spaces restored from glyph gaps.</summary>
    public static string ReadReadingOrderPlain(byte[] pdf)
    {
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
        {
            var pageWords = page.GetWords()?.ToList() ?? new List<UglyToad.PdfPig.Content.Word>();
            var lines = PdfWordLineBuilder.Build(pageWords, page.Width);
            foreach (var line in lines)
            {
                var text = PdfImportScriptMarkup.StripMarkers(line.Text).Trim();
                if (text.Length > 0)
                    sb.AppendLine(text);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string PlainFromHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        html = Regex.Replace(html, @"</(p|h[1-6]|li|div|figcaption|tr)>", " </$1>", RegexOptions.IgnoreCase);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var text = doc.DocumentNode.InnerText ?? "";
        return WebUtility.HtmlDecode(text);
    }

    public static string[] Tokenize(string? text)
    {
        var s = (text ?? "").ToLowerInvariant();
        s = s.Replace("\uFB00", "ff").Replace("\uFB01", "fi").Replace("\uFB02", "fl")
            .Replace("\uFB03", "ffi").Replace("\uFB04", "ffl");
        s = s.Replace('₀', '0').Replace('₁', '1').Replace('₂', '2').Replace('₃', '3').Replace('₄', '4')
            .Replace('₅', '5').Replace('₆', '6').Replace('₇', '7').Replace('₈', '8').Replace('₉', '9')
            .Replace('⁰', '0').Replace('¹', '1').Replace('²', '2').Replace('³', '3').Replace('⁴', '4')
            .Replace('⁵', '5').Replace('⁶', '6').Replace('⁷', '7').Replace('⁸', '8').Replace('⁹', '9');
        s = s.Replace('\u00ad', '-');
        s = s.Replace('“', '"').Replace('”', '"').Replace('‘', '\'').Replace('’', '\'');
        // An em dash is punctuation. Treating it as a hyphen glued "ready— for".
        s = s.Replace('—', ' ').Replace('–', ' ');
        // "of'98" and "the'60s" are the same words as "of 98" and "the 60s".
        s = Regex.Replace(s, @"([a-z])'(\d)", "$1 $2");
        // A short piece after a line-end hyphen is a broken word ("invest- ors").
        // A longer piece is a real compound ("20th- century") and stays hyphenated
        // so it splits into the same tokens as the source.
        s = Regex.Replace(s, @"(\w)-\s+(\w{1,3})\b", "$1$2");
        s = Regex.Replace(s, @"(\w)-\s+(\w{4,})", "$1-$2");
        var matches = Regex.Matches(s, @"[\p{L}\p{N}]+(?:[’'][\p{L}\p{N}]+)?");
        return matches.Select(m => m.Value).Where(w => w.Length > 0).ToArray();
    }

    /// <summary>
    /// Difflib-style alignment. On a mismatch it resyncs to the next shared 3-word phrase
    /// instead of giving up after a fixed window.
    /// </summary>
    public static Report Diff(IReadOnlyList<string> source, IReadOnlyList<string> output, IReadOnlyList<int>? outputPages = null, int hitLimit = 40)
    {
        var report = new Report
        {
            SourceWords = source.Count,
            OutputWords = output.Count
        };
        if (source.Count == 0)
        {
            report.Similarity = output.Count == 0 ? 1 : 0;
            report.Insertions = output.Count;
            return report;
        }

        var blocks = MatchingBlocks(source, output);
        var matched = 0;
        var srcAt = 0;
        var outAt = 0;
        foreach (var block in blocks)
        {
            AbsorbGlue(report, source, output, outputPages, ref srcAt, block.I, ref outAt, block.J, ref matched, hitLimit);
            matched += block.K;
            srcAt = block.I + block.K;
            outAt = block.J + block.K;
        }

        AbsorbGlue(report, source, output, outputPages, ref srcAt, source.Count, ref outAt, output.Count, ref matched, hitLimit);

        report.Similarity = (double)matched / source.Count;
        return report;
    }

    private readonly struct MatchBlock
    {
        public MatchBlock(int i, int j, int k)
        {
            I = i;
            J = j;
            K = k;
        }

        public int I { get; }
        public int J { get; }
        public int K { get; }
    }

    /// <summary>
    /// Difflib matching blocks: the longest contiguous run, then the same on each side.
    /// A short phrase early in the book cannot jump the cursor past the real body.
    /// </summary>
    private static List<MatchBlock> MatchingBlocks(IReadOnlyList<string> source, IReadOnlyList<string> output)
    {
        var index = IndexWords(output, out _);
        var found = new List<MatchBlock>();
        var queue = new Stack<(int Alo, int Ahi, int Blo, int Bhi)>();
        queue.Push((0, source.Count, 0, output.Count));
        while (queue.Count > 0)
        {
            var (alo, ahi, blo, bhi) = queue.Pop();
            if (alo >= ahi || blo >= bhi)
                continue;
            var block = FindLongestMatch(source, output, index, alo, ahi, blo, bhi);
            if (block.K <= 0)
                continue;
            found.Add(block);
            if (alo < block.I && blo < block.J)
                queue.Push((alo, block.I, blo, block.J));
            if (block.I + block.K < ahi && block.J + block.K < bhi)
                queue.Push((block.I + block.K, ahi, block.J + block.K, bhi));
        }

        found.Sort((a, b) => a.I != b.I ? a.I.CompareTo(b.I) : a.J.CompareTo(b.J));
        var merged = new List<MatchBlock>(found.Count);
        foreach (var block in found)
        {
            if (merged.Count > 0)
            {
                var last = merged[^1];
                if (last.I + last.K == block.I && last.J + last.K == block.J)
                {
                    merged[^1] = new MatchBlock(last.I, last.J, last.K + block.K);
                    continue;
                }
            }
            merged.Add(block);
        }
        return merged;
    }

    private static Dictionary<string, List<int>> IndexWords(IReadOnlyList<string> output, out HashSet<string> junk)
    {
        var index = new Dictionary<string, List<int>>(output.Count);
        for (var n = 0; n < output.Count; n++)
        {
            if (!index.TryGetValue(output[n], out var list))
            {
                list = new List<int>();
                index[output[n]] = list;
            }
            list.Add(n);
        }

        junk = new HashSet<string>(StringComparer.Ordinal);
        if (output.Count >= 200)
        {
            var popular = output.Count / 100 + 1;
            foreach (var pair in index)
            {
                if (pair.Value.Count > popular)
                    junk.Add(pair.Key);
            }
            foreach (var word in junk)
                index.Remove(word);
        }
        return index;
    }

    private static MatchBlock FindLongestMatch(
        IReadOnlyList<string> source,
        IReadOnlyList<string> output,
        Dictionary<string, List<int>> index,
        int alo,
        int ahi,
        int blo,
        int bhi)
    {
        var bestI = alo;
        var bestJ = blo;
        var best = 0;
        for (var i = alo; i < ahi; i++)
        {
            if (!index.TryGetValue(source[i], out var hits))
                continue;
            var start = LowerBound(hits, blo);
            for (var h = start; h < hits.Count; h++)
            {
                var j = hits[h];
                if (j >= bhi)
                    break;
                // Already measured from the first non-junk word of this run.
                if (i > alo && j > blo && source[i - 1] == output[j - 1] && index.ContainsKey(source[i - 1]))
                    continue;

                var end = 0;
                while (i + end < ahi && j + end < bhi && source[i + end] == output[j + end])
                    end++;
                var bi = i;
                var bj = j;
                var k = end;
                while (bi > alo && bj > blo && source[bi - 1] == output[bj - 1])
                {
                    bi--;
                    bj--;
                    k++;
                }
                if (k > best)
                {
                    best = k;
                    bestI = bi;
                    bestJ = bj;
                }
            }
        }
        return new MatchBlock(bestI, bestJ, best);
    }

    private static void Seal(Report report)
    {
        if (report.BodyDeletions > 0 || report.BodyReorders > 0)
            report.StructureOk = false;
        if (report.Similarity < 0.99)
        {
            report.StructureOk = false;
            if (!report.StructureNotes.Any(n => n.Contains("below 99%", StringComparison.Ordinal)))
                report.StructureNotes.Add($"Text similarity {report.Similarity:P2} is below 99%.");
        }

        report.Passed = report.Deletions == 0 && report.Reorders == 0
                        && report.Similarity >= PassSimilarity
                        && report.ImagesMatch
                        && report.StructureOk;
    }

    /// <summary>
    /// A missing space on one side ("commodity"+"business" vs "commoditybusiness",
    /// or "bysa" vs "by"+"sa") is the same words, not a deletion.
    /// </summary>
    private static void AbsorbGlue(
        Report report,
        IReadOnlyList<string> source,
        IReadOnlyList<string> output,
        IReadOnlyList<int>? outputPages,
        ref int srcAt,
        int srcEnd,
        ref int outAt,
        int outEnd,
        ref int matched,
        int hitLimit)
    {
        var srcCount = srcEnd - srcAt;
        var outCount = outEnd - outAt;
        if (srcCount > 0 && outCount > 0)
        {
            var srcUsed = new bool[srcCount];
            var outUsed = new bool[outCount];
            for (var j = outAt; j < outEnd; j++)
            {
                var bestI = -1;
                var bestParts = 1;
                for (var i = srcAt; i < srcEnd; i++)
                {
                    if (srcUsed[i - srcAt] || Math.Abs((i - srcAt) - (j - outAt)) > 40)
                        continue;
                    if (!TryConcat(source, i, srcEnd, output[j], out var parts) || parts <= bestParts)
                        continue;
                    var free = true;
                    for (var k = 0; k < parts; k++)
                    {
                        if (i + k >= srcEnd || srcUsed[i - srcAt + k])
                            free = false;
                    }
                    if (!free)
                        continue;
                    bestI = i;
                    bestParts = parts;
                }

                if (bestI < 0)
                    continue;
                for (var k = 0; k < bestParts; k++)
                    srcUsed[bestI - srcAt + k] = true;
                outUsed[j - outAt] = true;
                matched += bestParts;
            }

            for (var i = srcAt; i < srcEnd; i++)
            {
                if (srcUsed[i - srcAt])
                    continue;
                var bestJ = -1;
                var bestParts = 1;
                for (var j = outAt; j < outEnd; j++)
                {
                    if (outUsed[j - outAt] || Math.Abs((i - srcAt) - (j - outAt)) > 40)
                        continue;
                    if (!TryConcat(output, j, outEnd, source[i], out var parts) || parts <= bestParts)
                        continue;
                    var free = true;
                    for (var k = 0; k < parts; k++)
                    {
                        if (j + k >= outEnd || outUsed[j - outAt + k])
                            free = false;
                    }
                    if (!free)
                        continue;
                    bestJ = j;
                    bestParts = parts;
                }

                if (bestJ < 0)
                    continue;
                srcUsed[i - srcAt] = true;
                for (var k = 0; k < bestParts; k++)
                    outUsed[bestJ - outAt + k] = true;
                matched += 1;
            }

            var delFrom = -1;
            for (var i = srcAt; i < srcEnd; i++)
            {
                if (srcUsed[i - srcAt])
                    continue;
                report.Deletions++;
                if (delFrom < 0)
                    delFrom = i;
            }
            if (delFrom >= 0)
                AddHit(report, "deletion", source, delFrom, PageAt(outputPages, outAt), hitLimit, srcEnd);

            var inserted = new List<string>();
            var insPage = 0;
            for (var j = outAt; j < outEnd; j++)
            {
                if (outUsed[j - outAt])
                    continue;
                report.Insertions++;
                if (inserted.Count == 0)
                    insPage = PageAt(outputPages, j);
                inserted.Add(output[j]);
            }
            if (inserted.Count > 0 && report.Hits.Count < hitLimit)
            {
                report.Hits.Add(new DiffHit
                {
                    Kind = "insertion",
                    Page = insPage,
                    Context = string.Join(' ', inserted)
                });
            }
            return;
        }

        if (srcAt < srcEnd)
        {
            report.Deletions += srcEnd - srcAt;
            AddHit(report, "deletion", source, srcAt, PageAt(outputPages, outAt), hitLimit, srcEnd);
        }

        if (outAt < outEnd)
        {
            report.Insertions += outEnd - outAt;
            AddHit(report, "insertion", output, outAt, PageAt(outputPages, outAt), hitLimit, outEnd);
        }
    }

    private static bool TryConcat(IReadOnlyList<string> words, int start, int end, string target, out int used)
    {
        used = 0;
        if (target.Length < 2 || start >= end || !target.StartsWith(words[start], StringComparison.Ordinal))
            return false;
        var combined = words[start];
        var count = 1;
        for (var i = start + 1; i < end && count < 6 && combined.Length < target.Length; i++)
        {
            combined += words[i];
            count++;
            if (combined == target)
            {
                used = count;
                return true;
            }
        }

        return false;
    }

    private static void AddHit(Report report, string kind, IReadOnlyList<string> words, int at, int page, int hitLimit = 40, int end = -1)
    {
        if (report.Hits.Count >= hitLimit)
            return;
        report.Hits.Add(Hit(kind, words, at, page, end));
    }

    private static int LowerBound(List<int> values, int target)
    {
        var lo = 0;
        var hi = values.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (values[mid] < target)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private static void CountFurniture(Dictionary<string, int> counts, string line)
    {
        var key = FurnitureKey(line);
        if (key.Length == 0)
            return;
        counts.TryGetValue(key, out var n);
        counts[key] = n + 1;
    }

    private static string FurnitureKey(string? line)
    {
        var key = Regex.Replace((line ?? "").Trim().ToLowerInvariant(), @"\d+", "#");
        key = Regex.Replace(key, @"\s+", " ").Trim();
        return key.Length == 0 || key.Length > 48 ? "" : key;
    }

    private static string? ResolvePdftotext()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "pdftotext.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(local))
            return null;
        return Directory.EnumerateFiles(local, "pdftotext.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static void ApplyStructure(Report report, IReadOnlyList<ChapterDto>? chapters, string? output)
    {
        var notes = report.StructureNotes;
        var body = output ?? "";
        var titlePages = Regex.Matches(body, "class=\"title-page\\b", RegexOptions.IgnoreCase).Count;
        var copyrightPages = Regex.Matches(body, "copyright", RegexOptions.IgnoreCase).Count > 0 ? 1 : 0;
        if (chapters != null)
        {
            var numbered = chapters.Count(c =>
                !BookChapterExportHelper.IsUnnumberedSection(c.ChapterNumber, c.Title)
                && !BookHtmlNormalizer.IsTableOfContents(c)
                && !BookHtmlNormalizer.IsTitlePage(c));
            notes.Add($"Narrative chapters: {numbered}.");
            foreach (var ch in chapters)
            {
                var label = BookChapterExportHelper.GetPreviewStyleHeading(ch.Title, ch.ChapterNumber, 99);
                if (!string.IsNullOrWhiteSpace(label))
                    report.SectionLabels.Add(label);
                if (BookChapterExportHelper.IsBackMatterSectionTitle(ch.Title)
                    && label.Contains("Chapter", StringComparison.OrdinalIgnoreCase))
                    notes.Add($"Back matter labeled as a chapter: {ch.Title}");
            }
        }

        if (titlePages > 1)
            notes.Add($"Title pages: {titlePages} (expected 1).");
        report.StructureOk = notes.All(n => !n.Contains("labeled as a chapter", StringComparison.OrdinalIgnoreCase))
                             && titlePages <= 1
                             && copyrightPages <= 1;
        if (notes.Count == 0)
            notes.Add(copyrightPages == 1 ? "Copyright text present." : "No copyright text in this fragment.");
    }

    /// <summary>
    /// One title page, one contents page, copyright word order, and no leftover source contents list.
    /// </summary>
    private static void ApplyFrontMatterIntegrity(Report report, string? printHtml)
    {
        if (string.IsNullOrEmpty(printHtml))
            return;
        if (report.TitlePageCount != 1)
        {
            report.StructureOk = false;
            report.StructureNotes.Add("Expected one title page, found " + report.TitlePageCount + ".");
        }
        if (report.TocPageCount != 1)
        {
            report.StructureOk = false;
            report.StructureNotes.Add("Expected one contents page, found " + report.TocPageCount + ".");
        }
        if (printHtml.Contains(">Title Page<", StringComparison.OrdinalIgnoreCase))
        {
            report.StructureOk = false;
            report.StructureNotes.Add("The label \"Title Page\" was printed.");
        }

        var copyright = ExtractClassBlock(printHtml, "copyright-page");
        var flat = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(copyright, "<[^>]+>", " ")), @"\s+", " ");
        if (flat.Contains("randomcrown", StringComparison.OrdinalIgnoreCase)
            || !flat.Contains("trademarks of Random House", StringComparison.OrdinalIgnoreCase))
        {
            report.StructureOk = false;
            report.StructureNotes.Add("Copyright trademark sentence is out of order.");
        }
        if (flat.Contains("The Challenge of the Future", StringComparison.OrdinalIgnoreCase))
        {
            report.StructureOk = false;
            report.StructureNotes.Add("The source contents list is still on the copyright page.");
        }
    }

    private static string ExtractClassBlock(string html, string className)
    {
        var match = Regex.Match(
            html,
            "class\\s*=\\s*\"[^\"]*\\b" + Regex.Escape(className) + "\\b",
            RegexOptions.IgnoreCase);
        if (!match.Success)
            return "";
        var at = match.Index;
        var start = html.LastIndexOf('<', at);
        if (start < 0)
            return "";
        var depth = 0;
        for (var i = start; i < html.Length; i++)
        {
            if (html[i] != '<')
                continue;
            if (i + 1 < html.Length && html[i + 1] == '/')
            {
                depth--;
                if (depth == 0)
                {
                    var end = html.IndexOf('>', i);
                    return end < 0 ? html[start..] : html[start..(end + 1)];
                }
            }
            else if (i + 1 < html.Length && html[i + 1] != '!' && html[i + 1] != '?')
            {
                var close = html.IndexOf('>', i);
                var selfClosing = close > i && html[close - 1] == '/';
                if (!selfClosing)
                    depth++;
            }
        }
        return html[start..];
    }

    private static DiffHit Hit(string kind, IReadOnlyList<string> source, int at, int page = 0, int end = -1)
    {
        var from = end > at ? at : Math.Max(0, at - 4);
        var to = end > at ? Math.Min(source.Count, end) : Math.Min(source.Count, at + 6);
        return new DiffHit
        {
            Kind = kind,
            Page = page,
            Context = string.Join(' ', source.Skip(from).Take(to - from))
        };
    }

    private static int PageAt(IReadOnlyList<int>? pages, int index)
    {
        if (pages == null || pages.Count == 0)
            return 0;
        if (index < 0)
            return pages[0];
        if (index >= pages.Count)
            return pages[^1];
        return pages[index];
    }

    private static void CountPrintHtml(Report report, string? printHtml)
    {
        if (string.IsNullOrEmpty(printHtml))
            return;
        report.TitlePageCount = Regex.Matches(printHtml, "class=\"title-page\\b", RegexOptions.IgnoreCase).Count;
        report.CopyrightPageCount = Regex.Matches(printHtml, "class=\"[^\"]*\\bcopyright-page\\b", RegexOptions.IgnoreCase).Count;
        report.TocPageCount = Regex.Matches(printHtml, "class=\"[^\"]*\\btoc-page(?:\"|\\s)", RegexOptions.IgnoreCase).Count;
    }

    private static void CountPdfFrontMatter(Report report, byte[] pdf)
    {
        using var doc = PdfDocument.Open(pdf);
        var copyright = 0;
        var toc = 0;
        var title = 0;
        foreach (var page in doc.GetPages().Take(12))
        {
            var text = (page.Text ?? "").ToLowerInvariant();
            if (text.Contains("copyright") || text.Contains("all rights reserved"))
                copyright++;
            if (text.Contains("contents") && text.Contains("chapter"))
                toc++;
            if (page.Number <= 3 && Tokenize(page.Text).Length < 40)
                title++;
        }

        if (report.CopyrightPageCount == 0)
            report.CopyrightPageCount = copyright;
        if (report.TocPageCount == 0)
            report.TocPageCount = toc;
        if (report.TitlePageCount == 0)
            report.TitlePageCount = title;
    }

    private static int IndexOf(IReadOnlyList<string> words, string needle, int start, int endExclusive)
    {
        var end = Math.Min(words.Count, endExclusive);
        for (var i = start; i < end; i++)
        {
            if (words[i] == needle)
                return i;
        }
        return -1;
    }

    private static (List<string> Words, List<int> Pages) ExtractPdfWords(byte[] pdf, bool skipFrontMatter = false)
    {
        var words = new List<string>();
        var pages = new List<int>();
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
        {
            var pageWords = page.GetWords()?.ToList() ?? new List<UglyToad.PdfPig.Content.Word>();
            var lines = PdfWordLineBuilder.Build(pageWords, page.Width);
            var text = string.Join('\n', lines.Select(l => PdfImportScriptMarkup.StripMarkers(l.Text)));
            var tokens = Tokenize(text);
            if (IsGeneratedChromePage(text, tokens.Length, page.Number))
                continue;
            if (skipFrontMatter && IsGeneratedFrontMatterPage(text, tokens.Length))
                continue;
            foreach (var word in tokens)
            {
                words.Add(word);
                pages.Add(page.Number);
            }
        }

        return (words, pages);
    }

    /// <summary>Our title page and generated contents page are not part of the manuscript word sequence.</summary>
    private static bool IsGeneratedChromePage(string text, int wordCount, int pageNumber)
    {
        var lower = text.ToLowerInvariant();
        if (pageNumber <= 2 && wordCount < 45 && !lower.Contains("copyright"))
            return true;
        if (wordCount < 280 && lower.Contains("contents") && lower.Contains("chapter"))
            return true;
        return false;
    }

    /// <summary>
    /// The generated contents page is not part of the manuscript word sequence.
    /// A copyright page is kept even when a leftover contents list shares it.
    /// </summary>
    private static bool IsGeneratedFrontMatterPage(string text, int wordCount)
    {
        var lower = text.ToLowerInvariant();
        if (lower.Contains("copyright") || lower.Contains("rights reserved"))
            return false;
        if (wordCount < 900 && lower.Contains("contents")
            && (lower.Contains("preface") || lower.Contains("chapter")))
            return true;
        return false;
    }

    private static int CountPdfImages(byte[] pdf)
    {
        var n = 0;
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
        {
            try { n += page.GetImages()?.Count() ?? 0; }
            catch { /* unreadable image dictionary */ }
        }
        return n;
    }
}
