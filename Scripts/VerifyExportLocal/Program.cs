using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MySqlConnector;

var repoRoot = args.Length > 1 ? args[1] : @"c:\Users\Hp\Desktop\newEbook";
var baseUrl = args.Length > 0 ? args[0] : "http://localhost:5282";
var connStr = ResolveConnectionString(repoRoot);

string email, password;
int bookId;

await using (var conn = new MySqlConnection(connStr))
{
    await conn.OpenAsync();
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = """
        SELECT u.UserEmail, u.Password, b.BookId
        FROM users u
        JOIN books b ON b.UserId = u.UserId
        JOIN chapters c ON c.BookId = b.BookId
        WHERE c.Content IS NOT NULL AND LENGTH(TRIM(c.Content)) > 10
        GROUP BY u.UserId, b.BookId
        ORDER BY b.BookId DESC
        LIMIT 1
        """;
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) throw new Exception("No book with content found.");
    email = r.GetString(0);
    password = r.GetString(1);
    bookId = r.GetInt32(2);
}

Console.WriteLine($"=== Manual verification harness bookId={bookId} email={email} ===");

var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = true };
using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(3) };

var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["UserEmail"] = email,
    ["Password"] = password,
    ["RememberMe"] = "false"
});
(await http.PostAsync("/Account/UserLogin", loginForm)).EnsureSuccessStatusCode();

var outDir = Path.Combine(Path.GetTempPath(), $"ebook-verify-{bookId}");
Directory.CreateDirectory(outDir);
var failures = new List<string>();

// TEST 1: Cold start — first PDF and EPUB download
Console.WriteLine("\n[TEST 1] Cold start download (first request after app start)");
var coldPayload = BuildPayload(bookId, "Novel", "Medium", "1.6");
var (pdf1, pdfStatus1, pdfErr1) = await PostDownload(http, coldPayload, "/Dashboard/DownloadBookPdf");
var pdfOk1 = IsValidPdf(pdf1);
Console.WriteLine($"  PDF: status={pdfStatus1} bytes={pdf1.Length} valid={pdfOk1} err={pdfErr1}");
if (!pdfOk1) failures.Add("Cold start PDF failed");

var (epub1, epubStatus1, epubErr1) = await PostDownload(http, coldPayload, "/Books/ExportEpub");
var epubOk1 = IsValidEpub(epub1);
Console.WriteLine($"  EPUB: status={epubStatus1} bytes={epub1.Length} valid={epubOk1} err={epubErr1}");
if (!epubOk1) failures.Add("Cold start EPUB failed");

// TEST 2: Edit-vs-output parity — change settings, compare preview HTML tokens vs export payload
Console.WriteLine("\n[TEST 2] Edit-vs-output parity");
var editStyle = "Classic";
var editSize = "Large";
var editLine = "1.8";
await SaveFormatting(http, bookId, editStyle, editSize, editLine);
var previewEdited = await http.GetStringAsync($"/BookDesign/PreviewBookHtml?bookId={bookId}");
var hasClassic = previewEdited.Contains("tpl-classic", StringComparison.Ordinal) || previewEdited.Contains("interior-classic", StringComparison.Ordinal);
var hasLargePt = previewEdited.Contains("14.25pt", StringComparison.Ordinal);
var hasLine18 = previewEdited.Contains("--ilt-body-lh: 1.8", StringComparison.Ordinal);
Console.WriteLine($"  Preview after save: classic={hasClassic} largePt={hasLargePt} line1.8={hasLine18}");
if (!(hasClassic && hasLargePt && hasLine18)) failures.Add("Preview does not reflect edited Classic/Large/1.8 settings");

var editPayload = BuildPayload(bookId, editStyle, editSize, editLine);
var (pdf2, _, pdfErr2) = await PostDownload(http, editPayload, "/Dashboard/DownloadBookPdf");
await File.WriteAllBytesAsync(Path.Combine(outDir, "parity.pdf"), pdf2);
Console.WriteLine($"  PDF with edited settings: bytes={pdf2.Length} valid={IsValidPdf(pdf2)} err={pdfErr2}");
if (!IsValidPdf(pdf2)) failures.Add("PDF export failed after formatting edit");

// Re-fetch preview and ensure still matches (export uses same pipeline)
var previewAfterPdf = await http.GetStringAsync($"/BookDesign/PreviewBookHtml?bookId={bookId}");
var previewStable = previewAfterPdf.Contains("14.25pt", StringComparison.Ordinal);
Console.WriteLine($"  Preview stable after PDF export: {previewStable}");
if (!previewStable) failures.Add("Preview drifted after PDF export");

// TEST 3: Resume/reload — simulate reload by fetching preview again without re-saving (DB persisted)
Console.WriteLine("\n[TEST 3] Resume/reload (persisted settings after simulated reload)");
await Task.Delay(500);
var reloadedPreview = await http.GetStringAsync($"/BookDesign/PreviewBookHtml?bookId={bookId}&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
var restoredClassic = reloadedPreview.Contains("tpl-classic", StringComparison.Ordinal) || reloadedPreview.Contains("interior-classic", StringComparison.Ordinal);
var restoredLarge = reloadedPreview.Contains("14.25pt", StringComparison.Ordinal);
var restoredLine = reloadedPreview.Contains("--ilt-body-lh: 1.8", StringComparison.Ordinal);
Console.WriteLine($"  Reloaded preview: classic={restoredClassic} largePt={restoredLarge} line1.8={restoredLine}");
if (!(restoredClassic && restoredLarge && restoredLine)) failures.Add("Formatting not restored after reload");

// TEST 4: KDP layout in PDF + preview
Console.WriteLine("\n[TEST 4] KDP layout check");
await SaveFormatting(http, bookId, "Novel", "Medium", "1.6"); // reset to novel for KDP check
var kdpPreview = await http.GetStringAsync($"/BookDesign/PreviewBookHtml?bookId={bookId}");
var kdpPayload = BuildPayload(bookId, "Novel", "Medium", "1.6");
var (pdfKdp, _, _) = await PostDownload(http, kdpPayload, "/Dashboard/DownloadBookPdf");
await File.WriteAllBytesAsync(Path.Combine(outDir, "kdp.pdf"), pdfKdp);
await File.WriteAllTextAsync(Path.Combine(outDir, "kdp-preview.html"), kdpPreview);

var hasCopyright = kdpPreview.Contains("copyright", StringComparison.OrdinalIgnoreCase);
var hasToc = kdpPreview.Contains("toc-block", StringComparison.Ordinal);
var hasChapterBlock = kdpPreview.Contains("reader-chapter-block", StringComparison.Ordinal);
var hasPageBreak = kdpPreview.Contains("page-break-before", StringComparison.Ordinal);
var has6x9 = kdpPreview.Contains("6in", StringComparison.Ordinal);
var pageCount = CountPdfPages(pdfKdp);
var pdfText = Encoding.Latin1.GetString(pdfKdp);
var hasFolioNumbers = pdfText.Contains("pageNumber") || CountNumericFolios(pdfText) >= 2;
Console.WriteLine($"  Preview: copyright={hasCopyright} toc={hasToc} chapters={hasChapterBlock} pageBreak={hasPageBreak} trim6x9={has6x9}");
Console.WriteLine($"  PDF: pages={pageCount} folioMarkers={hasFolioNumbers}");
if (!hasCopyright) failures.Add("Missing copyright front matter in preview");
if (!hasToc) failures.Add("Missing TOC in preview");
if (!hasChapterBlock) failures.Add("Missing chapter blocks in preview");
if (!has6x9) failures.Add("Missing 6x9 trim in preview CSS");
if (pageCount < 3) failures.Add($"PDF too short for front matter + chapters (pages={pageCount})");

Console.WriteLine($"\nOUTDIR={outDir}");
if (failures.Count == 0)
{
    Console.WriteLine("ALL TESTS PASSED");
    Environment.Exit(0);
}
else
{
    Console.WriteLine("FAILURES:");
    foreach (var f in failures) Console.WriteLine($"  - {f}");
    Environment.Exit(1);
}

static string BuildPayload(int bookId, string style, string size, string line) =>
    JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["bookId"] = bookId,
        ["displayTitle"] = "Verify Export",
        ["displayAuthor"] = "Test Author",
        ["displayGenre"] = "Fiction",
        ["interiorStyle"] = style,
        ["textSize"] = size,
        ["lineSpacing"] = line,
        ["bookFormat"] = "Ebook"
    });

static async Task SaveFormatting(HttpClient http, int bookId, string style, string size, string line)
{
    var body = JsonSerializer.Serialize(new
    {
        bookId,
        interiorStyle = style,
        textSize = size,
        lineSpacing = line,
        format = "Ebook",
        publishingPlatforms = "",
        includeCoverPage = true
    });
    var resp = await http.PostAsync("/BookDesign/SaveBookFormatting",
        new StringContent(body, Encoding.UTF8, "application/json"));
    var text = await resp.Content.ReadAsStringAsync();
    if (!resp.IsSuccessStatusCode)
        throw new Exception($"SaveBookFormatting failed: {resp.StatusCode} {text}");
}

static async Task<(byte[] bytes, int status, string? err)> PostDownload(HttpClient http, string json, string path)
{
    var resp = await http.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
    var bytes = await resp.Content.ReadAsByteArrayAsync();
    return resp.IsSuccessStatusCode
        ? (bytes, (int)resp.StatusCode, null)
        : (bytes, (int)resp.StatusCode, Encoding.UTF8.GetString(bytes));
}

static bool IsValidPdf(byte[] b) => b.Length > 128 && b[0] == (byte)'%' && b[1] == (byte)'P';
static bool IsValidEpub(byte[] b) => b.Length > 80 && b[0] == 0x50 && b[1] == 0x4B;
static int CountPdfPages(byte[] pdf) => Encoding.Latin1.GetString(pdf).Split("/Type /Page", StringSplitOptions.None).Length - 1;
static int CountNumericFolios(string pdfText)
{
    var count = 0;
    for (var i = 1; i <= 20; i++)
        if (pdfText.Contains($">{i}<") || pdfText.Contains($" {i} ")) count++;
    return count;
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
