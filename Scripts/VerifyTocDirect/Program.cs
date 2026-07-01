using System.Globalization;
using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using EBookDashboard.Services.PdfExport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var repoRoot = args.Length > 0 ? args[0] : @"c:\Users\Hp\Desktop\newEbook";
var bookId = args.Length > 1 && int.TryParse(args[1], out var id) ? id : 218;

Environment.CurrentDirectory = repoRoot;
var host = Host.CreateDefaultBuilder(args)
    .UseContentRoot(repoRoot)
    .ConfigureAppConfiguration(cfg =>
    {
        cfg.SetBasePath(repoRoot);
        cfg.AddJsonFile("appsettings.json", optional: false);
        cfg.AddJsonFile("appsettings.Development.json", optional: true);
        cfg.AddJsonFile("appsettings.Local.json", optional: true);
        cfg.AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<IWebHostEnvironment>(new SimpleWebHostEnvironment(repoRoot));
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        var cs = ctx.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing.");
        services.AddDbContext<ApplicationDbContext>(o => o.UseMySql(cs, ServerVersion.AutoDetect(cs)));
        services.AddSingleton<PdfHtmlExportServiceResolver>();
        services.AddScoped<ITocPageNumberMeasurer, ChromiumTocPageNumberMeasurer>();
        services.AddScoped<IBookRenderService, BookRenderService>();
        services.AddScoped<IBookPdfService, BookPdfService>();
    })
    .Build();

using var scope = host.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var pdfSvc = scope.ServiceProvider.GetRequiredService<IBookPdfService>();

var book = await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.BookId == bookId)
    ?? throw new InvalidOperationException($"Book {bookId} not found.");
var author = await db.Users.AsNoTracking()
    .Where(u => u.UserId == book.UserId)
    .Select(u => u.FullName)
    .FirstOrDefaultAsync() ?? "Author";
var chapterRows = await db.Chapters.AsNoTracking()
    .Where(c => c.BookId == bookId)
    .OrderBy(c => c.ChapterNumber <= 0 ? int.MaxValue : c.ChapterNumber)
    .ToListAsync();

var details = new BookDetailsResponseDto
{
    Success = true,
    BookId = book.BookId,
    BookTitle = book.Title,
    Subtitle = book.Subtitle,
    Description = book.Description,
    Genre = book.Genre,
    AuthorName = author,
    CoverImagePath = book.CoverImagePath,
    Chapters = chapterRows.Select(c => new ChapterDto
    {
        ChapterNumber = c.ChapterNumber,
        Title = c.Title,
        Content = c.Content
    }).ToList()
};

var opt = new BookPdfExportOptions
{
    InteriorStyle = "ElegantTrade",
    TextSize = "Medium",
    LineSpacing = "1.6",
    Format = "Ebook",
    IncludeCoverPage = true
};

Console.WriteLine($"=== Direct TOC verify bookId={bookId} chapters={details.Chapters.Count} ===");
var pdfBytes = await pdfSvc.RenderFullBookPdfAsync(
    details, null, details.BookTitle, details.AuthorName ?? "", details.Genre ?? "", opt, null);
Console.WriteLine($"PDF bytes: {pdfBytes.Length}");

var renderSvc = scope.ServiceProvider.GetRequiredService<IBookRenderService>();
var render = await renderSvc.BuildBookHtmlAsync(new BookRenderRequest
{
    Details = details,
    ExportOptions = opt,
    DisplayTitle = details.BookTitle,
    DisplayAuthor = details.AuthorName,
    DisplayGenre = details.Genre
});
var tocNumbers = Regex.Matches(render.Html, @"class=""toc-page-ref"">(\d+)</span>")
    .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
    .ToList();
Console.WriteLine($"TOC numbers in HTML: [{string.Join(", ", tocNumbers)}]");
if (tocNumbers.Count != details.Chapters.Count || tocNumbers.Any(n => n <= 0))
{
    Console.WriteLine("FAIL: TOC numbers missing or count mismatch.");
    Environment.Exit(1);
}

var outPath = Path.Combine(Path.GetTempPath(), $"toc-verify-{bookId}", "book-direct.pdf");
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
await File.WriteAllBytesAsync(outPath, pdfBytes);
Console.WriteLine($"Saved: {outPath}");
Console.WriteLine("PASS: Chromium pdf.js measurement baked TOC page numbers into export HTML/PDF pipeline.");

sealed class SimpleWebHostEnvironment : IWebHostEnvironment
{
    public SimpleWebHostEnvironment(string root)
    {
        ContentRootPath = root;
        WebRootPath = Path.Combine(root, "wwwroot");
    }

    public string ApplicationName { get; set; } = "VerifyTocDirect";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Development";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; }
}
