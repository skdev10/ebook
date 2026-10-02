using System.Collections.Concurrent;
using System.Text.Json;
using EBookDashboard.Interfaces;
using Microsoft.AspNetCore.Hosting;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EBookDashboard.Services.PdfExport;

public sealed class BookPrintJobSnapshot
{
    public string JobId { get; set; } = "";
    public int UserId { get; set; }
    public int Percent { get; set; }
    public string Message { get; set; } = "";
    /// <summary>queued, running, blocked, ready, or error.</summary>
    public string Status { get; set; } = "queued";
    public string? FileName { get; set; }
    public string? Error { get; set; }
    public BookIntegrityChecker.Report? Integrity { get; set; }
    public string? HeldPath { get; set; }
}

/// <summary>
/// Renders large print PDFs off the HTTP request and holds a finished file until the
/// integrity check passes or the reader chooses Download anyway.
/// </summary>
public interface IBookPrintExportQueue
{
    /// <summary>Queue a full-book render. Progress is available from <see cref="Get"/>.</summary>
    string Start(int userId, ExportBookPdfRequest request, string? publisherLabel);

    BookPrintJobSnapshot? Get(string jobId, int userId);

    /// <summary>Save a PDF that failed integrity so it is not sent until the reader confirms.</summary>
    string Hold(int userId, byte[] pdf, string fileName, BookIntegrityChecker.Report report);

    byte[]? ReadPdf(string jobId, int userId, bool downloadAnyway);
}

public sealed class BookPrintExportQueue : IBookPrintExportQueue
{
    /// <summary>Chapter HTML longer than this leaves the HTTP request and runs in the background.</summary>
    public const int BackgroundHtmlChars = 900_000;

    private readonly ConcurrentDictionary<string, BookPrintJobSnapshot> _jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<BookPrintExportQueue> _logger;

    public BookPrintExportQueue(
        IServiceScopeFactory scopeFactory,
        IWebHostEnvironment env,
        ILogger<BookPrintExportQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _env = env;
        _logger = logger;
    }

    public string Start(int userId, ExportBookPdfRequest request, string? publisherLabel)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var snap = new BookPrintJobSnapshot
        {
            JobId = jobId,
            UserId = userId,
            Status = "queued",
            Message = "Queued",
            Percent = 0
        };
        _jobs[jobId] = snap;
        var copy = JsonSerializer.Deserialize<ExportBookPdfRequest>(JsonSerializer.Serialize(request))
                   ?? request;
        _ = Task.Run(() => RunAsync(snap, copy, publisherLabel));
        return jobId;
    }

    public BookPrintJobSnapshot? Get(string jobId, int userId)
    {
        if (string.IsNullOrWhiteSpace(jobId) || !_jobs.TryGetValue(jobId, out var snap) || snap.UserId != userId)
            return null;
        return snap;
    }

    public string Hold(int userId, byte[] pdf, string fileName, BookIntegrityChecker.Report report)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var path = WritePdf(jobId, pdf);
        _jobs[jobId] = new BookPrintJobSnapshot
        {
            JobId = jobId,
            UserId = userId,
            Status = "blocked",
            Percent = 100,
            Message = "Text check failed",
            FileName = fileName,
            Integrity = report,
            HeldPath = path
        };
        return jobId;
    }

    public byte[]? ReadPdf(string jobId, int userId, bool downloadAnyway)
    {
        var snap = Get(jobId, userId);
        if (snap == null || string.IsNullOrEmpty(snap.HeldPath) || !File.Exists(snap.HeldPath))
            return null;
        if (snap.Status == "blocked" && !downloadAnyway)
            return null;
        if (snap.Status is not ("ready" or "blocked"))
            return null;
        return File.ReadAllBytes(snap.HeldPath);
    }

    private async Task RunAsync(BookPrintJobSnapshot snap, ExportBookPdfRequest request, string? publisherLabel)
    {
        try
        {
            snap.Status = "running";
            snap.Message = "Loading manuscript";
            snap.Percent = 2;
            BookPrintProgress.Bind((percent, message) =>
            {
                snap.Percent = percent;
                snap.Message = message;
                snap.Status = "running";
            });

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var books = scope.ServiceProvider.GetRequiredService<IBookService>();
            var pdfs = scope.ServiceProvider.GetRequiredService<IBookPdfService>();

            var details = await books.GetBookDetailsForPreviewAsync(snap.UserId, request.BookId);
            if (details == null || !details.Success)
                throw new InvalidOperationException(details?.Message ?? "Could not load book.");

            var draft = await db.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == $"book:{request.BookId}:formattingDraft");
            var fmt = await db.BookFormatting.AsNoTracking()
                .FirstOrDefaultAsync(f => f.BookId == request.BookId && f.UserId == snap.UserId);
            var exportOpt = BookPdfExportOptions.LoadFromPersistence(fmt, draft?.Value);
            exportOpt.ApplyRequestOverrides(request);

            BookPrintProgress.Report(8, "Measuring contents");
            var pdf = await pdfs.RenderFullBookPdfAsync(
                details,
                request.CoverImageDataUrl,
                request.DisplayTitle,
                request.DisplayAuthor,
                request.DisplayGenre,
                exportOpt,
                publisherLabel);

            if (pdf == null || pdf.Length < 128)
                throw new InvalidOperationException("PDF generation produced an empty file.");

            var sourceHtml = string.Concat((details.Chapters ?? new List<ChapterDto>()).Select(c => c.Content));
            var integrity = BookIntegrityChecker.CheckAgainstPdf(sourceHtml, pdf, details.Chapters);
            var reportDir = Path.Combine(_env.ContentRootPath, "App_Data", "integrity");
            BookIntegrityChecker.Save(reportDir, request.BookId.ToString(), integrity);

            var rawName = (request.DisplayTitle ?? details.BookTitle ?? "book").Trim();
            var safe = string.IsNullOrEmpty(rawName) ? "book" : rawName;
            snap.FileName = safe + "-" + request.BookId + ".pdf";
            snap.HeldPath = WritePdf(snap.JobId, pdf);
            snap.Integrity = integrity;
            snap.Percent = 100;
            snap.Status = integrity.Passed ? "ready" : "blocked";
            snap.Message = integrity.Passed
                ? "Ready"
                : "Text check failed. Review the pages below, then download anyway if you still want the file.";
            _logger.LogInformation("Print job {JobId} {Status} for book {BookId}", snap.JobId, snap.Status, request.BookId);
        }
        catch (Exception ex)
        {
            snap.Status = "error";
            snap.Error = ex.Message;
            snap.Message = ex.Message;
            _logger.LogError(ex, "Print job {JobId} failed", snap.JobId);
        }
        finally
        {
            BookPrintProgress.Bind(null);
        }
    }

    private string WritePdf(string jobId, byte[] pdf)
    {
        var dir = Path.Combine(_env.ContentRootPath, "App_Data", "print-jobs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, jobId + ".pdf");
        File.WriteAllBytes(path, pdf);
        return path;
    }
}
