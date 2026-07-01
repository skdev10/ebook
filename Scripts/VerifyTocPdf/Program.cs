using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MySqlConnector;
using UglyToad.PdfPig;

var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5282";
var repoRoot = args.Length > 1 ? args[1] : @"c:\Users\Hp\Desktop\newEbook";
var bookIdArg = args.Length > 2 ? args[2] : "218";
if (!int.TryParse(bookIdArg, out var bookId)) bookId = 218;

var connStr = ResolveConnectionString(repoRoot);
string email, password;

await using (var conn = new MySqlConnection(connStr))
{
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT u.UserEmail, u.Password
        FROM users u
        JOIN books b ON b.UserId = u.UserId
        WHERE b.BookId = @bookId
        LIMIT 1
        """;
    cmd.Parameters.AddWithValue("@bookId", bookId);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) throw new Exception($"Book {bookId} not found.");
    email = r.GetString(0);
    password = r.GetString(1);
}

Console.WriteLine($"=== TOC PDF verification bookId={bookId} ===");

var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = true };
using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };

var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["UserEmail"] = email,
    ["Password"] = password,
    ["RememberMe"] = "false"
});
(await http.PostAsync("/Account/UserLogin", loginForm)).EnsureSuccessStatusCode();

var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
{
    ["bookId"] = bookId,
    ["interiorStyle"] = "ElegantTrade",
    ["textSize"] = "Medium",
    ["lineSpacing"] = "1.6",
    ["bookFormat"] = "Ebook",
    ["includeCoverPage"] = true
});

var resp = await http.PostAsync("/Dashboard/DownloadBookPdf",
    new StringContent(payload, Encoding.UTF8, "application/json"));
var pdfBytes = await resp.Content.ReadAsByteArrayAsync();
if (!resp.IsSuccessStatusCode)
    throw new Exception($"PDF download failed: {resp.StatusCode} {Encoding.UTF8.GetString(pdfBytes)}");

var outDir = Path.Combine(Path.GetTempPath(), $"toc-verify-{bookId}");
Directory.CreateDirectory(outDir);
var pdfPath = Path.Combine(outDir, $"book-{bookId}.pdf");
await File.WriteAllBytesAsync(pdfPath, pdfBytes);
Console.WriteLine($"PDF saved: {pdfPath} ({pdfBytes.Length} bytes)");

var pageTexts = ExtractPageTexts(pdfBytes);
Console.WriteLine($"Pages: {pageTexts.Count}");

var tocEntries = ParseTocEntries(pageTexts);
Console.WriteLine($"TOC entries parsed: {tocEntries.Count}");
foreach (var e in tocEntries)
    Console.WriteLine($"  TOC: \"{e.Title}\" -> page {e.ListedPage}");

var chapterStarts = FindChapterStartPages(pageTexts, tocEntries.Select(t => t.Title).ToList());
Console.WriteLine($"Chapter start pages found: {chapterStarts.Count}");
foreach (var kv in chapterStarts)
    Console.WriteLine($"  Actual: \"{kv.Key}\" -> page {kv.Value}");

var mismatches = new List<string>();
foreach (var toc in tocEntries)
{
    if (!chapterStarts.TryGetValue(toc.Title, out var actual))
    {
        mismatches.Add($"Missing chapter in PDF: \"{toc.Title}\" (TOC says {toc.ListedPage})");
        continue;
    }

    if (actual != toc.ListedPage)
        mismatches.Add($"\"{toc.Title}\": TOC={toc.ListedPage} actual={actual}");
}

if (mismatches.Count == 0)
{
    Console.WriteLine("PASS: All TOC page numbers match chapter start pages.");
    Environment.Exit(0);
}

Console.WriteLine("FAILURES:");
foreach (var m in mismatches) Console.WriteLine($"  - {m}");
Environment.Exit(1);

static List<string> ExtractPageTexts(byte[] pdfBytes)
{
    var latin1 = Encoding.Latin1.GetString(pdfBytes);
    var chunks = latin1.Split("/Type /Page", StringSplitOptions.None);
    var pages = new List<string>();
    for (var i = 1; i < chunks.Length; i++)
        pages.Add(Normalize(chunks[i]));
    return pages;
}

static string Normalize(string s) =>
    Regex.Replace(s.Replace('\u00A0', ' '), @"\s+", " ").Trim();

static List<TocEntry> ParseTocEntries(IReadOnlyList<string> pageTexts)
{
    var entries = new List<TocEntry>();
    for (var pageIndex = 0; pageIndex < pageTexts.Count; pageIndex++)
    {
        var text = pageTexts[pageIndex];
        if (!text.Contains("Contents", StringComparison.OrdinalIgnoreCase)) continue;

        foreach (Match m in Regex.Matches(text, @"(.+?)\s+(\d{1,4})\s*$"))
        {
            var title = m.Groups[1].Value.Trim();
            if (title.Length < 3) continue;
            if (title.Contains("Contents", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(m.Groups[2].Value, out var listed)) continue;
            if (entries.Any(e => e.Title.Equals(title, StringComparison.OrdinalIgnoreCase))) continue;
            entries.Add(new TocEntry(title, listed));
        }
    }

    return entries;
}

static Dictionary<string, int> FindChapterStartPages(IReadOnlyList<string> pageTexts, IReadOnlyList<string> titles)
{
    var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var title in titles)
    {
        var needle = Normalize(title);
        for (var i = 0; i < pageTexts.Count; i++)
        {
            if (pageTexts[i].Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                result[title] = i + 1;
                break;
            }
        }
    }

    return result;
}

static string ResolveConnectionString(string root)
{
    var appsettings = Path.Combine(root, "appsettings.json");
    var dev = Path.Combine(root, "appsettings.Development.json");
    var local = Path.Combine(root, "appsettings.Local.json");
    var node = JsonNode.Parse(File.ReadAllText(appsettings))!.AsObject();
    if (File.Exists(dev)) Merge(node, JsonNode.Parse(File.ReadAllText(dev))!.AsObject());
    if (File.Exists(local)) Merge(node, JsonNode.Parse(File.ReadAllText(local))!.AsObject());
    var cs = node["ConnectionStrings"]?["DefaultConnection"]?.GetValue<string>()?.Trim();
    if (!string.IsNullOrEmpty(cs)) return cs;
    var pw = Environment.GetEnvironmentVariable("MYSQL_PASSWORD")
        ?? Environment.GetEnvironmentVariable("MYSQL_ROOT_PASSWORD")
        ?? node["Database"]?["FallbackPassword"]?.GetValue<string>() ?? "";
    return $"Server=127.0.0.1;Port=3306;Database=ebookpublications;User=root;Password={pw};AllowPublicKeyRetrieval=True;";
}

static void Merge(JsonObject target, JsonObject source)
{
    foreach (var kv in source)
    {
        if (kv.Value is JsonObject so && target[kv.Key] is JsonObject to) Merge(to, so);
        else target[kv.Key] = kv.Value?.DeepClone();
    }
}

internal sealed record TocEntry(string Title, int ListedPage);
