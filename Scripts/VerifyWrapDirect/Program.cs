using EBookDashboard.Application.Kdp.Interfaces;
using EBookDashboard.Application.Kdp.Services;
using EBookDashboard.Infrastructure;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.Options;
using EBookDashboard.Services;
using EBookDashboard.Services.BookApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var repoRoot = args.Length > 0 ? args[0] : @"c:\Users\Hp\Desktop\newEbook";
var bookIdArg = args.Length > 1 && int.TryParse(args[1], out var bid) ? bid : 0;

Environment.CurrentDirectory = repoRoot;
var host = Host.CreateDefaultBuilder()
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
        services.Configure<ExternalApiOptions>(ctx.Configuration.GetSection("ExternalApi"));
        services.AddSingleton<IWebHostEnvironment>(new SimpleWebHostEnvironment(repoRoot));
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddHttpClient();
        services.AddBookUpstreamHttpClients(ctx.Configuration);
        services.AddSingleton<IKdpCoverDimensionService, KdpCoverDimensionService>();
        services.AddScoped<IBookPageMetricsService, BookPageMetricsService>();
        services.AddScoped<IChapterIterationService, ChapterIterationService>();
        services.AddScoped<IBookService, BookService>();
        services.AddScoped<IPrintWrapGenerationService, PrintWrapGenerationService>();

        var cs = ctx.Configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection missing.");
        services.AddDbContext<ApplicationDbContext>(o => o.UseMySql(cs, ServerVersion.AutoDetect(cs)));
    })
    .Build();

using var scope = host.Services.CreateScope();
var cfg = scope.ServiceProvider.GetRequiredService<IConfiguration>();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var wrapSvc = scope.ServiceProvider.GetRequiredService<IPrintWrapGenerationService>();

var hasKey = !string.IsNullOrWhiteSpace(ExternalApiKeyResolver.Resolve(cfg));
Console.WriteLine($"API key configured: {hasKey}");
if (!hasKey)
{
    Console.WriteLine("FAIL: Set ExternalApi__ApiKey env var or appsettings.Local.json before testing wrap.");
    return 1;
}

var candidates = await db.Books.AsNoTracking()
    .OrderByDescending(b => b.BookId)
    .Take(30)
    .Select(b => new { b.BookId, b.UserId, b.Title, b.CoverImagePath })
    .ToListAsync();

Console.WriteLine("Books with cover assets:");
foreach (var b in candidates)
{
    var keys = new[]
    {
        $"book:{b.BookId}:printReadyCoverFront",
        $"book:{b.BookId}:aiCoverLastPreview",
        $"book:{b.BookId}:printReadyCoverWrap"
    };
    var rows = await db.Settings.AsNoTracking()
        .Where(s => keys.Contains(s.Key))
        .ToDictionaryAsync(s => s.Key, s => s.Value ?? "");
    var front = rows.GetValueOrDefault($"book:{b.BookId}:printReadyCoverFront", "");
    var ai = rows.GetValueOrDefault($"book:{b.BookId}:aiCoverLastPreview", "");
    var wrap = rows.GetValueOrDefault($"book:{b.BookId}:printReadyCoverWrap", "");
    if (string.IsNullOrWhiteSpace(front) && string.IsNullOrWhiteSpace(ai) && string.IsNullOrWhiteSpace(b.CoverImagePath))
        continue;

    Console.WriteLine($"  book={b.BookId} user={b.UserId} title={b.Title}");
    Console.WriteLine($"    front={(front.Length > 0 ? front[..Math.Min(70, front.Length)] : "(none)")}");
    Console.WriteLine($"    aiPreview={(ai.Length > 0 ? ai[..Math.Min(70, ai.Length)] : "(none)")}");
    Console.WriteLine($"    wrap={(wrap.Length > 0 ? wrap[..Math.Min(70, wrap.Length)] : "(none)")}");
}

var target = bookIdArg > 0
    ? await db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.BookId == bookIdArg)
    : await db.Books.AsNoTracking()
        .OrderByDescending(b => b.BookId)
        .FirstOrDefaultAsync(b => db.Settings.Any(s =>
            s.Key == $"book:{b.BookId}:printReadyCoverFront" || s.Key == $"book:{b.BookId}:aiCoverLastPreview"));

if (target == null)
{
    Console.WriteLine("FAIL: No book with saved front cover found.");
    return 1;
}

Console.WriteLine($"\nTesting wrap generation for book {target.BookId} (user {target.UserId})…");
var frontBefore = await db.Settings.AsNoTracking()
    .Where(s => s.Key == $"book:{target.BookId}:printReadyCoverFront" || s.Key == $"book:{target.BookId}:aiCoverLastPreview")
    .Select(s => s.Value)
    .FirstOrDefaultAsync();
var wrapBefore = await db.Settings.AsNoTracking()
    .Where(s => s.Key == $"book:{target.BookId}:printReadyCoverWrap")
    .Select(s => s.Value)
    .FirstOrDefaultAsync();

Console.WriteLine($"Front before: {(frontBefore ?? "").Trim()[..Math.Min(80, (frontBefore ?? "").Trim().Length)]}");
Console.WriteLine($"Wrap before: {(string.IsNullOrWhiteSpace(wrapBefore) ? "(empty)" : wrapBefore.Trim()[..Math.Min(80, wrapBefore.Trim().Length)])}");

var ok = await wrapSvc.TryGenerateFromSavedFrontAsync(target.UserId, target.BookId);
Console.WriteLine($"TryGenerateFromSavedFrontAsync => {ok}");

var wrapAfter = await db.Settings.AsNoTracking()
    .Where(s => s.Key == $"book:{target.BookId}:printReadyCoverWrap" || s.Key == $"book:{target.BookId}:printReadyCoverWrapApi")
    .Select(s => s.Value)
    .FirstOrDefaultAsync();
var frontAfter = await db.Settings.AsNoTracking()
    .Where(s => s.Key == $"book:{target.BookId}:printReadyCoverFront")
    .Select(s => s.Value)
    .FirstOrDefaultAsync();

Console.WriteLine($"Wrap after: {(string.IsNullOrWhiteSpace(wrapAfter) ? "(empty)" : wrapAfter.Trim()[..Math.Min(80, wrapAfter.Trim().Length)])}");
Console.WriteLine($"Front after: {(frontAfter ?? "").Trim()[..Math.Min(80, (frontAfter ?? "").Trim().Length)]}");

if (string.IsNullOrWhiteSpace(wrapAfter))
{
    Console.WriteLine("FAIL: Wrap was not saved.");
    return 2;
}

Console.WriteLine("PASS: Full wrap saved in Settings.");
return 0;

sealed class SimpleWebHostEnvironment : IWebHostEnvironment
{
    public SimpleWebHostEnvironment(string contentRoot)
    {
        ContentRootPath = contentRoot;
        WebRootPath = Path.Combine(contentRoot, "wwwroot");
    }

    public string ApplicationName { get; set; } = "VerifyWrapDirect";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Development";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; }
}
