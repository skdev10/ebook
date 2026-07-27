using EBookDashboard.Configuration;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Services;
using EBookDashboard.Services.Publishing;
using EBookDashboard.Services.Rendering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Controllers;

/// <summary>
/// Multipart upload → structured review → workspace (setup/typography/cover/preview) → export.
/// Every workspace step is always reachable and export is never gated — see individual actions.
/// Views bind directly to <see cref="Project"/> (with its navigation collections eager-loaded by
/// <see cref="PublishingProjectService.GetForUserAsync"/>) plus a handful of ViewBag computed values.
/// </summary>
[Route("Publishing")]
public sealed class PublishingController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly ManuscriptImportService _importService;
    private readonly PublishingProjectService _projectService;
    private readonly BookHtmlBuilder _htmlBuilder;
    private readonly PagedRenderer _pagedRenderer;
    private readonly ExportJobService _exportJobs;
    private readonly KdpCalculationService _calc;
    private readonly IWebHostEnvironment _env;
    private readonly KdpSpecs _specs;
    private readonly ILogger<PublishingController> _logger;

    public PublishingController(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        ManuscriptImportService importService,
        PublishingProjectService projectService,
        BookHtmlBuilder htmlBuilder,
        PagedRenderer pagedRenderer,
        ExportJobService exportJobs,
        KdpCalculationService calc,
        IWebHostEnvironment env,
        IOptions<KdpSpecs> specs,
        ILogger<PublishingController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _importService = importService;
        _projectService = projectService;
        _htmlBuilder = htmlBuilder;
        _pagedRenderer = pagedRenderer;
        _exportJobs = exportJobs;
        _calc = calc;
        _env = env;
        _specs = specs.Value;
        _logger = logger;
    }

    private int? UserId => _currentUser.GetUserId();

    // ───────────────────────────── Upload ─────────────────────────────

    [HttpGet("Upload")]
    public IActionResult Upload()
    {
        if (UserId == null) return RedirectToAction("UserLogin", "Account");
        // Primary Formatting UI is the classic CoverDesignCalculatorFixing studio — not the long wizard.
        return Redirect("/BookDesign/CoverDesignCalculatorFixing?bookId=0");
    }

    [HttpPost("UploadManuscript")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(80_000_000)]
    public async Task<IActionResult> UploadManuscript(IFormFile? file, int projectId = 0, string? title = null, CancellationToken cancellationToken = default)
    {
        var userId = UserId;
        if (userId == null)
            return Json(new { success = false, message = "Please sign in to upload a manuscript." });

        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "Choose a Word (.docx) or PDF file to upload." });

        var maxBytes = (long)_specs.MaxUploadSizeMb * 1024 * 1024;
        if (file.Length > maxBytes)
            return Json(new { success = false, message = $"That file is larger than the {_specs.MaxUploadSizeMb} MB upload limit. Try compressing images or splitting the manuscript." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".docx" or ".pdf"))
            return Json(new { success = false, message = "Please upload a Word (.docx) or PDF manuscript. Other formats aren't supported yet." });

        Project? project;
        if (projectId > 0)
        {
            project = await _projectService.GetForUserAsync(projectId, userId.Value, cancellationToken);
            if (project == null)
                return Json(new { success = false, message = "That project could not be found." });
        }
        else
        {
            var suggestedTitle = string.IsNullOrWhiteSpace(title)
                ? Path.GetFileNameWithoutExtension(file.FileName)
                : title.Trim();
            project = await _projectService.CreateProjectAsync(userId.Value, suggestedTitle, authorName: null, cancellationToken);
        }

        try
        {
            var storageRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            await using var stream = file.OpenReadStream();

            var importResult = ext == ".docx"
                ? await _importService.ImportDocxAsync(stream, file.FileName, project.Id, storageRoot, cancellationToken)
                : await _importService.ImportPdfAsync(stream, file.FileName, project.Id, storageRoot, cancellationToken);

            var storedRelativePath = await SaveOriginalFileAsync(file, project.Id, cancellationToken);

            var version = await _projectService.SaveManuscriptVersionAsync(
                project, file.FileName, storedRelativePath, importResult.SourceFormat, importResult, cancellationToken);

            return Json(new
            {
                success = true,
                projectId = project.Id,
                versionId = version.Id,
                stats = new
                {
                    chapterCount = importResult.Stats.ChapterCount,
                    wordCount = importResult.Stats.WordCount,
                    imageCount = importResult.Stats.ImageCount
                },
                reviewUrl = Url.Action(nameof(Review), new { projectId = project.Id })
            });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadManuscript failed for project {ProjectId}", project.Id);
            return Json(new { success = false, message = "Something went wrong reading that file. Please try again or use a different export of your manuscript." });
        }
    }

    private async Task<string> SaveOriginalFileAsync(IFormFile file, int projectId, CancellationToken cancellationToken)
    {
        var storageRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
        var dir = Path.Combine(storageRoot, "uploads", "projects", projectId.ToString(), "manuscripts");
        Directory.CreateDirectory(dir);
        var safeExt = Path.GetExtension(file.FileName);
        var fileName = $"manuscript_{DateTime.UtcNow:yyyyMMddHHmmss}{safeExt}";
        var fullPath = Path.Combine(dir, fileName);
        await using var outStream = System.IO.File.Create(fullPath);
        await file.CopyToAsync(outStream, cancellationToken);
        return $"/uploads/projects/{projectId}/manuscripts/{fileName}";
    }

    // ───────────────────────────── Review ─────────────────────────────

    [HttpGet("Review/{projectId:int}")]
    public async Task<IActionResult> Review(int projectId, CancellationToken cancellationToken = default)
    {
        var userId = UserId;
        if (userId == null) return RedirectToAction("UserLogin", "Account");

        var project = await _projectService.GetForUserAsync(projectId, userId.Value, cancellationToken);
        if (project == null)
        {
            TempData["InfoMessage"] = "That project could not be found.";
            return RedirectToAction(nameof(Upload));
        }

        var version = project.ManuscriptVersions.FirstOrDefault(v => v.IsActive)
            ?? project.ManuscriptVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

        ViewBag.Step = "upload";
        ViewBag.Version = version;
        return View(project);
    }

    // ───────────────────────────── Workspace ─────────────────────────────

    [HttpGet("Workspace/{projectId:int}")]
    public async Task<IActionResult> Workspace(int projectId, string step = "type", CancellationToken cancellationToken = default)
    {
        var userId = UserId;
        if (userId == null) return RedirectToAction("UserLogin", "Account");

        var project = await _projectService.GetForUserAsync(projectId, userId.Value, cancellationToken);
        if (project == null)
        {
            TempData["InfoMessage"] = "That project could not be found. Start a new upload.";
            return RedirectToAction(nameof(Upload));
        }

        var printProfile = project.LayoutProfiles.FirstOrDefault(p => !p.IsEbookProfile);
        if (printProfile == null)
        {
            await _projectService.UpsertLayoutProfileAsync(project, new LayoutProfile { UseRecommendedMargins = true }, ebook: false, cancellationToken);
            printProfile = project.LayoutProfiles.First(p => !p.IsEbookProfile);
        }
        var ebookProfile = project.LayoutProfiles.FirstOrDefault(p => p.IsEbookProfile);
        if (ebookProfile == null)
        {
            await _projectService.UpsertLayoutProfileAsync(project, new LayoutProfile { UseRecommendedMargins = false }, ebook: true, cancellationToken);
            ebookProfile = project.LayoutProfiles.First(p => p.IsEbookProfile);
        }

        var version = project.ManuscriptVersions.FirstOrDefault(v => v.IsActive)
            ?? project.ManuscriptVersions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

        var validSteps = new[] { "upload", "type", "trim", "typography", "cover", "preview", "pages", "export" };
        var normalizedStep = validSteps.Contains(step, StringComparer.OrdinalIgnoreCase) ? step.ToLowerInvariant() : "type";

        ViewBag.Step = normalizedStep;
        ViewBag.Version = version;
        ViewBag.PrintProfile = printProfile;
        ViewBag.EbookProfile = ebookProfile;
        ViewBag.TrimPresets = _specs.TrimPresets;
        ViewBag.PageWarning = _calc.ValidatePageCount(project.PageCount, project.ProjectType, project.PaperType);
        ViewBag.GutterBand = _calc.GetActiveGutterBandLabel(Math.Max(project.PageCount, 1));
        ViewBag.SpineWidth = _calc.CalculateSpineWidthIn(project.PageCount, project.PaperType, project.ProjectType);

        return View(project);
    }

    // ───────────────────────────── Save endpoints ─────────────────────────────

    public sealed class SaveSetupRequest
    {
        public int ProjectId { get; set; }
        public int ProjectType { get; set; }
        public double TrimWidthIn { get; set; }
        public double TrimHeightIn { get; set; }
        public bool IsCustomTrim { get; set; }
        public int PaperType { get; set; }
        public bool HasBleed { get; set; }
    }

    [HttpPost("SaveSetup")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveSetup([FromBody] SaveSetupRequest request, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return Json(new { success = false, message = "Please sign in." });

        var project = await _projectService.GetForUserAsync(request.ProjectId, userId.Value, cancellationToken);
        if (project == null) return Json(new { success = false, message = "Project not found." });

        var projectType = Enum.IsDefined(typeof(ProjectType), request.ProjectType) ? (ProjectType)request.ProjectType : ProjectType.Paperback;
        var paperType = Enum.IsDefined(typeof(PaperType), request.PaperType) ? (PaperType)request.PaperType : PaperType.White;

        await _projectService.UpdateProjectSetupAsync(
            project, projectType, request.TrimWidthIn, request.TrimHeightIn, request.IsCustomTrim, paperType, request.HasBleed, cancellationToken);

        var printProfile = project.LayoutProfiles.FirstOrDefault(p => !p.IsEbookProfile);
        return Json(new
        {
            success = true,
            gutterBand = _calc.GetActiveGutterBandLabel(Math.Max(project.PageCount, 1)),
            pageWarning = _calc.ValidatePageCount(project.PageCount, project.ProjectType, project.PaperType),
            spineWidthIn = _calc.CalculateSpineWidthIn(project.PageCount, project.PaperType, project.ProjectType),
            margins = printProfile == null ? null : new
            {
                marginTopIn = printProfile.MarginTopIn,
                marginBottomIn = printProfile.MarginBottomIn,
                marginOutsideIn = printProfile.MarginOutsideIn,
                marginInsideIn = printProfile.MarginInsideIn
            }
        });
    }

    public sealed class TypographyProfileDto
    {
        public string? BodyFontFamily { get; set; }
        public double? BodyFontSizePt { get; set; }
        public double? LineSpacing { get; set; }
        public string? TextAlignment { get; set; }
        public double? H1FontSizePt { get; set; }
        public double? FirstLineIndentIn { get; set; }
        public bool? NoIndentOnFirstPara { get; set; }
        public bool? UseRecommendedMargins { get; set; }
    }

    public sealed class SaveTypographyRequest
    {
        public int ProjectId { get; set; }
        public bool IsEbookProfile { get; set; }
        public TypographyProfileDto Profile { get; set; } = new();
    }

    [HttpPost("SaveTypography")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveTypography([FromBody] SaveTypographyRequest request, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return Json(new { success = false, message = "Please sign in." });

        var project = await _projectService.GetForUserAsync(request.ProjectId, userId.Value, cancellationToken);
        if (project == null) return Json(new { success = false, message = "Project not found." });

        var existing = project.LayoutProfiles.FirstOrDefault(p => p.IsEbookProfile == request.IsEbookProfile)
            ?? new LayoutProfile { IsEbookProfile = request.IsEbookProfile };
        var p = request.Profile;

        var incoming = new LayoutProfile
        {
            MarginTopIn = existing.MarginTopIn,
            MarginBottomIn = existing.MarginBottomIn,
            MarginOutsideIn = existing.MarginOutsideIn,
            MarginInsideIn = existing.MarginInsideIn,
            UseRecommendedMargins = p.UseRecommendedMargins ?? existing.UseRecommendedMargins,
            MarginsLocked = existing.MarginsLocked,
            BleedMode = existing.BleedMode,
            BodyFontFamily = string.IsNullOrWhiteSpace(p.BodyFontFamily) ? existing.BodyFontFamily : p.BodyFontFamily,
            BodyFontSizePt = p.BodyFontSizePt ?? existing.BodyFontSizePt,
            LineSpacing = p.LineSpacing ?? existing.LineSpacing,
            ParagraphSpacingBeforePt = existing.ParagraphSpacingBeforePt,
            ParagraphSpacingAfterPt = existing.ParagraphSpacingAfterPt,
            TextAlignment = string.IsNullOrWhiteSpace(p.TextAlignment) ? existing.TextAlignment : p.TextAlignment,
            FirstLineIndentIn = p.FirstLineIndentIn ?? existing.FirstLineIndentIn,
            NoIndentOnFirstPara = p.NoIndentOnFirstPara ?? existing.NoIndentOnFirstPara,
            H1FontFamily = existing.H1FontFamily,
            H1FontSizePt = p.H1FontSizePt ?? existing.H1FontSizePt,
            H2FontFamily = existing.H2FontFamily,
            H2FontSizePt = existing.H2FontSizePt,
            TocFontSizePt = existing.TocFontSizePt,
            HeaderFooterFontSizePt = existing.HeaderFooterFontSizePt,
            PageNumberPosition = existing.PageNumberPosition,
            ShowPageNumbers = existing.ShowPageNumbers,
            ChaptersStartOnRecto = existing.ChaptersStartOnRecto
        };

        await _projectService.UpsertLayoutProfileAsync(project, incoming, request.IsEbookProfile, cancellationToken);

        return Json(new { success = true });
    }

    public sealed class SectionEditDto
    {
        public int Id { get; set; }
        public int OrderIndex { get; set; }
        public string? Title { get; set; }
        public string? MatterType { get; set; }
    }

    public sealed class SaveSectionsRequest
    {
        public int ProjectId { get; set; }
        public List<SectionEditDto> Sections { get; set; } = new();
    }

    [HttpPost("SaveSections")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SaveSections([FromBody] SaveSectionsRequest request, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return Json(new { success = false, message = "Please sign in." });

        var project = await _projectService.GetForUserAsync(request.ProjectId, userId.Value, cancellationToken);
        if (project == null) return Json(new { success = false, message = "Project not found." });

        var version = project.ManuscriptVersions.FirstOrDefault(v => v.IsActive) ?? project.ManuscriptVersions.FirstOrDefault();
        if (version == null)
            return Json(new { success = false, message = "No active manuscript version to update." });

        var existingById = version.Sections.ToDictionary(s => s.Id);
        var updates = new List<BookSection>();
        foreach (var edit in request.Sections)
        {
            if (!existingById.TryGetValue(edit.Id, out var existing))
                continue;

            var matter = existing.MatterType;
            if (!string.IsNullOrWhiteSpace(edit.MatterType))
                Enum.TryParse(edit.MatterType, true, out matter);

            updates.Add(new BookSection
            {
                Id = existing.Id,
                ManuscriptVersionId = existing.ManuscriptVersionId,
                ParentSectionId = existing.ParentSectionId,
                MatterType = matter,
                SectionKind = existing.SectionKind,
                Title = string.IsNullOrWhiteSpace(edit.Title) ? existing.Title : edit.Title.Trim(),
                OrderIndex = edit.OrderIndex,
                ContentHtml = existing.ContentHtml,
                StartsOnRecto = existing.StartsOnRecto,
                IsBlank = existing.IsBlank,
                LayoutTemplate = existing.LayoutTemplate
            });
        }

        await _projectService.SaveSectionsAsync(version.Id, updates, cancellationToken);
        return Json(new { success = true });
    }

    public sealed class AutosaveRequest
    {
        public int ProjectId { get; set; }
        public string Field { get; set; } = string.Empty;
        public string? Value { get; set; }
    }

    [HttpPost("Autosave")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Autosave([FromBody] AutosaveRequest request, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return Json(new { success = false });

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId.Value, cancellationToken);
        if (project == null) return Json(new { success = false });

        switch ((request.Field ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "title":
                if (!string.IsNullOrWhiteSpace(request.Value)) project.Title = request.Value.Trim();
                break;
            case "subtitle":
                project.Subtitle = string.IsNullOrWhiteSpace(request.Value) ? null : request.Value.Trim();
                break;
            case "authorname":
                project.AuthorName = string.IsNullOrWhiteSpace(request.Value) ? null : request.Value.Trim();
                break;
            default:
                return Json(new { success = false, message = "Unknown field." });
        }

        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return Json(new { success = true, savedAt = DateTime.UtcNow });
    }

    public sealed class RestoreVersionRequest
    {
        public int ProjectId { get; set; }
        public int VersionId { get; set; }
    }

    [HttpPost("RestoreVersion")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> RestoreVersion([FromBody] RestoreVersionRequest request, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return Json(new { success = false, message = "Please sign in." });

        var project = await _projectService.GetForUserAsync(request.ProjectId, userId.Value, cancellationToken);
        if (project == null) return Json(new { success = false, message = "Project not found." });

        try
        {
            await _projectService.RestoreVersionAsync(project, request.VersionId, cancellationToken);
            return Json(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ───────────────────────────── Pagination (live page count / gutter band) ─────────────────────────────

    [HttpPost("Paginate/{projectId:int}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Paginate(int projectId, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return Json(new { success = false, message = "Please sign in." });

        var project = await _projectService.GetForUserAsync(projectId, userId.Value, cancellationToken);
        if (project == null) return Json(new { success = false, message = "Project not found." });

        var version = project.ManuscriptVersions.FirstOrDefault(v => v.IsActive) ?? project.ManuscriptVersions.FirstOrDefault();
        if (version == null)
            return Json(new { success = false, message = "Upload a manuscript first, then refresh the page count." });

        var printProfile = project.LayoutProfiles.FirstOrDefault(p => !p.IsEbookProfile) ?? new LayoutProfile { ProjectId = project.Id };

        try
        {
            var result = await _pagedRenderer.PaginateAsync(project, version, printProfile, producePdfBytes: false, cancellationToken);
            var startPages = result.SectionStartPages.ToDictionary(kv => kv.Key, kv => kv.Value.DisplayPageNumber);
            await _projectService.UpdatePageNumbersAsync(project, version, result.TotalPages, startPages, cancellationToken);

            return Json(new
            {
                success = true,
                pageCount = result.TotalPages,
                gutterBand = _calc.GetActiveGutterBandLabel(Math.Max(result.TotalPages, 1)),
                pageWarning = _calc.ValidatePageCount(result.TotalPages, project.ProjectType, project.PaperType)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pagination failed for project {ProjectId}", projectId);
            return Json(new { success = false, message = "Pagination preview failed — you can still export; export renders pages independently." });
        }
    }

    // ───────────────────────────── Preview ─────────────────────────────

    [HttpGet("PreviewHtml/{projectId:int}")]
    public async Task<IActionResult> PreviewHtml(int projectId, bool spread = false, bool ebook = false, CancellationToken cancellationToken = default)
    {
        var userId = UserId;
        if (userId == null) return Unauthorized();

        var project = await _projectService.GetForUserAsync(projectId, userId.Value, cancellationToken);
        if (project == null) return NotFound();

        var version = project.ManuscriptVersions.FirstOrDefault(v => v.IsActive) ?? project.ManuscriptVersions.FirstOrDefault();
        if (version == null || version.Sections.Count == 0)
            return Content("<html><body style=\"font-family:sans-serif;padding:2rem;color:#6b7280;\">Upload a manuscript to see the preview here.</body></html>", "text/html");

        var profile = project.LayoutProfiles.FirstOrDefault(p => p.IsEbookProfile == ebook)
            ?? project.LayoutProfiles.FirstOrDefault()
            ?? new LayoutProfile { ProjectId = project.Id, IsEbookProfile = ebook };

        var html = _htmlBuilder.BuildHtml(project, version, profile, new BookHtmlBuildOptions
        {
            PreviewMode = true,
            SpreadPreview = spread,
            IncludeSectionMarkers = false,
            Scope = SectionScope.All
        });

        return Content(html, "text/html");
    }

    // ───────────────────────────── Export ─────────────────────────────

    public sealed class ExportRequest
    {
        public int ProjectId { get; set; }
        public int Format { get; set; }
        public bool IncludeCover { get; set; }
        public bool IncludeFrontMatter { get; set; } = true;
        public bool IncludeBackMatter { get; set; } = true;
    }

    [HttpPost("Export")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Export([FromBody] ExportRequest request, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return Json(new { success = false, message = "Please sign in." });
        if (!await OwnsProjectAsync(userId.Value, request.ProjectId, cancellationToken))
            return Json(new { success = false, message = "Project not found." });

        var format = Enum.IsDefined(typeof(ExportFormat), request.Format) ? (ExportFormat)request.Format : ExportFormat.PrintPdf;

        // Export is never blocked — the workspace always exposes "Export anyway" regardless of
        // setup completeness; missing pieces (e.g. no cover yet) surface as a job failure message.
        var job = await _exportJobs.QueueExportAsync(
            request.ProjectId, format, request.IncludeCover, request.IncludeFrontMatter, request.IncludeBackMatter, cancellationToken);

        return Json(new { success = true, jobId = job.Id, statusUrl = Url.Action(nameof(ExportStatus), new { jobId = job.Id }) });
    }

    [HttpGet("ExportStatus/{jobId:int}")]
    public async Task<IActionResult> ExportStatus(int jobId, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return Json(new { success = false, message = "Please sign in." });

        var job = await _exportJobs.GetJobAsync(jobId, userId.Value, cancellationToken);
        if (job == null) return Json(new { success = false, message = "Export job not found." });

        var live = _exportJobs.GetProgress(jobId);
        return Json(new
        {
            success = true,
            jobId = job.Id,
            status = job.Status.ToString(),
            percent = live?.PercentComplete ?? (job.Status == ExportJobStatus.Succeeded ? 100 : 0),
            message = live?.Message,
            error = job.ErrorMessage,
            downloadUrl = job.Status == ExportJobStatus.Succeeded ? Url.Action(nameof(Download), new { jobId = job.Id }) : null
        });
    }

    [HttpGet("Download/{jobId:int}")]
    public async Task<IActionResult> Download(int jobId, CancellationToken cancellationToken)
    {
        var userId = UserId;
        if (userId == null) return RedirectToAction("UserLogin", "Account");

        var job = await _exportJobs.GetJobAsync(jobId, userId.Value, cancellationToken);
        if (job == null || job.Status != ExportJobStatus.Succeeded || string.IsNullOrWhiteSpace(job.OutputFilePath))
            return NotFound();

        var physicalPath = Path.Combine(_env.WebRootPath ?? string.Empty, job.OutputFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(physicalPath))
            return NotFound();

        var contentType = job.Format switch
        {
            ExportFormat.Epub => "application/epub+zip",
            _ => "application/pdf"
        };
        var downloadName = Path.GetFileName(physicalPath);
        return PhysicalFile(physicalPath, contentType, downloadName);
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private async Task<bool> OwnsProjectAsync(int userId, int projectId, CancellationToken cancellationToken) =>
        await _db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId && p.UserId == userId, cancellationToken);
}
