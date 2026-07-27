using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using EBookDashboard.Models;
using EBookDashboard.Services.Rendering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace EBookDashboard.Services.Publishing;

/// <summary>Live progress snapshot for a single export job, polled by the workspace UI.</summary>
public sealed class ExportProgress
{
    public int JobId { get; set; }
    public int PercentComplete { get; set; }
    public string Message { get; set; } = string.Empty;
    public ExportJobStatus Status { get; set; }
}

/// <summary>In-memory progress store for export jobs (per-process; acceptable for a single-node deployment).</summary>
public interface IExportProgressStore
{
    void Set(int jobId, ExportProgress progress);
    ExportProgress? Get(int jobId);
    void Remove(int jobId);
}

public sealed class ExportProgressStore : IExportProgressStore
{
    private readonly ConcurrentDictionary<int, ExportProgress> _store = new();

    public void Set(int jobId, ExportProgress progress) => _store[jobId] = progress;
    public ExportProgress? Get(int jobId) => _store.TryGetValue(jobId, out var p) ? p : null;
    public void Remove(int jobId) => _store.TryRemove(jobId, out _);
}

/// <summary>
/// Queues and runs Print PDF / EPUB / Cover PDF export jobs in the background, persisting an
/// <see cref="ExportJob"/> row and streaming progress through <see cref="IExportProgressStore"/>.
/// Export is never blocked by upstream validation — the workspace always offers "Export anyway".
/// </summary>
public sealed class ExportJobService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IExportProgressStore _progress;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ExportJobService> _logger;

    public ExportJobService(
        IServiceScopeFactory scopeFactory,
        IExportProgressStore progress,
        IWebHostEnvironment env,
        ILogger<ExportJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _progress = progress;
        _env = env;
        _logger = logger;
    }

    public async Task<ExportJob> QueueExportAsync(
        int projectId,
        ExportFormat format,
        bool includeCover,
        bool includeFrontMatter,
        bool includeBackMatter,
        CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var job = new ExportJob
        {
            ProjectId = projectId,
            Format = format,
            IncludeCover = includeCover,
            IncludeFrontMatter = includeFrontMatter,
            IncludeBackMatter = includeBackMatter,
            Status = ExportJobStatus.Queued
        };
        db.ExportJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        _progress.Set(job.Id, new ExportProgress { JobId = job.Id, PercentComplete = 0, Message = "Queued", Status = ExportJobStatus.Queued });

        _ = Task.Run(() => RunExportAsync(job.Id));
        return job;
    }

    public ExportProgress? GetProgress(int jobId) => _progress.Get(jobId);

    public async Task<ExportJob?> GetJobAsync(int jobId, int userId, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.ExportJobs
            .Include(j => j.Project)
            .FirstOrDefaultAsync(j => j.Id == jobId && j.Project != null && j.Project.UserId == userId, cancellationToken);
    }

    private async Task RunExportAsync(int jobId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var job = await db.ExportJobs.FirstOrDefaultAsync(j => j.Id == jobId);
        if (job == null) return;

        try
        {
            job.Status = ExportJobStatus.Running;
            job.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Report(jobId, 10, "Loading manuscript", ExportJobStatus.Running);

            var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == job.ProjectId)
                ?? throw new InvalidOperationException("Project not found.");
            var version = await db.ManuscriptVersions
                .Include(v => v.Sections)
                .Where(v => v.ProjectId == project.Id && v.IsActive)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("Upload a manuscript before exporting.");

            var outputDir = Path.Combine(_env.WebRootPath ?? string.Empty, "exports", project.Id.ToString());
            Directory.CreateDirectory(outputDir);

            byte[] fileBytes;
            string fileName;
            var slug = Slugify(project.Title);

            switch (job.Format)
            {
                case ExportFormat.Epub:
                {
                    Report(jobId, 35, "Building EPUB package", ExportJobStatus.Running);
                    var ebookProfile = await db.LayoutProfiles.FirstOrDefaultAsync(p => p.ProjectId == project.Id && p.IsEbookProfile)
                        ?? new LayoutProfile { ProjectId = project.Id, IsEbookProfile = true };
                    var builder = new Epub3Builder();
                    byte[]? coverBytes = null;
                    string? coverContentType = null;
                    if (job.IncludeCover)
                        (coverBytes, coverContentType) = await TryLoadCoverBytesAsync(db, project.Id);
                    fileBytes = builder.Build(project, version, ebookProfile, coverBytes, coverContentType);
                    fileName = $"{slug}.epub";
                    break;
                }
                case ExportFormat.PrintPdf:
                {
                    Report(jobId, 25, "Paginating interior (this can take a moment)", ExportJobStatus.Running);
                    var printProfile = await db.LayoutProfiles.FirstOrDefaultAsync(p => p.ProjectId == project.Id && !p.IsEbookProfile)
                        ?? new LayoutProfile { ProjectId = project.Id, IsEbookProfile = false };
                    var renderer = scope.ServiceProvider.GetRequiredService<PagedRenderer>();
                    var result = await renderer.PaginateAsync(project, version, printProfile, producePdfBytes: true);
                    fileBytes = result.PdfBytes ?? throw new InvalidOperationException("The interior PDF could not be rendered.");
                    fileName = $"{slug}-interior.pdf";
                    break;
                }
                case ExportFormat.CoverPdf:
                {
                    Report(jobId, 30, "Preparing cover PDF", ExportJobStatus.Running);
                    fileBytes = await BuildCoverPdfAsync(db, project.Id);
                    fileName = $"{slug}-cover.pdf";
                    break;
                }
                default:
                    throw new InvalidOperationException("Unknown export format.");
            }

            Report(jobId, 90, "Saving file", ExportJobStatus.Running);
            var fullPath = Path.Combine(outputDir, fileName);
            await File.WriteAllBytesAsync(fullPath, fileBytes);

            job.Status = ExportJobStatus.Succeeded;
            job.OutputFilePath = $"/exports/{project.Id}/{fileName}";
            job.FileSizeBytes = fileBytes.LongLength;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            Report(jobId, 100, "Export complete", ExportJobStatus.Succeeded);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Export job {JobId} failed", jobId);
            job.Status = ExportJobStatus.Failed;
            job.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            try { await db.SaveChangesAsync(); } catch { /* best-effort */ }
            Report(jobId, 100, ex.Message, ExportJobStatus.Failed);
        }
    }

    private async Task<byte[]> BuildCoverPdfAsync(ApplicationDbContext db, int projectId)
    {
        var cover = await db.CoverProjects
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();

        if (cover == null || string.IsNullOrWhiteSpace(cover.UploadedFilePath))
            throw new InvalidOperationException("Design a cover first from the Book Cover step, then export again.");

        var physicalPath = ResolvePhysicalPath(cover.UploadedFilePath);
        if (!File.Exists(physicalPath))
            throw new InvalidOperationException("The saved cover image could not be found. Re-save your cover design and try again.");

        if (Path.GetExtension(physicalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return await File.ReadAllBytesAsync(physicalPath);

        using var doc = new PdfDocument();
        var widthIn = cover.ComputedTotalWidthIn > 0 ? cover.ComputedTotalWidthIn : 6.0;
        var heightIn = cover.ComputedTotalHeightIn > 0 ? cover.ComputedTotalHeightIn : 9.0;
        var page = doc.AddPage();
        page.Width = XUnit.FromInch(widthIn);
        page.Height = XUnit.FromInch(heightIn);

        using var gfx = XGraphics.FromPdfPage(page);
        using var image = XImage.FromFile(physicalPath);
        gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);

        using var ms = new MemoryStream();
        doc.Save(ms, false);
        return ms.ToArray();
    }

    private async Task<(byte[]? Bytes, string? ContentType)> TryLoadCoverBytesAsync(ApplicationDbContext db, int projectId)
    {
        var cover = await db.CoverProjects
            .Where(c => c.ProjectId == projectId)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync();
        if (cover == null || string.IsNullOrWhiteSpace(cover.UploadedFilePath))
            return (null, null);

        var physicalPath = ResolvePhysicalPath(cover.UploadedFilePath);
        if (!File.Exists(physicalPath) || Path.GetExtension(physicalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return (null, null);

        try
        {
            var bytes = await File.ReadAllBytesAsync(physicalPath);
            return (bytes, null);
        }
        catch
        {
            return (null, null);
        }
    }

    private string ResolvePhysicalPath(string storedPath)
    {
        if (Path.IsPathRooted(storedPath) && File.Exists(storedPath))
            return storedPath;
        var webRoot = _env.WebRootPath ?? string.Empty;
        return Path.Combine(webRoot, storedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    }

    private void Report(int jobId, int percent, string message, ExportJobStatus status) =>
        _progress.Set(jobId, new ExportProgress { JobId = jobId, PercentComplete = percent, Message = message, Status = status });

    private static readonly Regex SlugInvalidChars = new(@"[^a-z0-9\-]+", RegexOptions.Compiled);

    private static string Slugify(string? title)
    {
        var t = (title ?? "book").Trim().ToLowerInvariant().Replace(' ', '-');
        t = SlugInvalidChars.Replace(t, string.Empty);
        t = Regex.Replace(t, "-{2,}", "-").Trim('-');
        return string.IsNullOrEmpty(t) ? "book" : (t.Length > 80 ? t[..80] : t);
    }
}
