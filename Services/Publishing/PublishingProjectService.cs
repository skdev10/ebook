using EBookDashboard.Configuration;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services.Publishing;

public sealed class PublishingProjectService
{
    private readonly Models.ApplicationDbContext _db;
    private readonly KdpSpecs _specs;
    private readonly KdpCalculationService _calc;
    private readonly ILogger<PublishingProjectService> _logger;

    public PublishingProjectService(
        Models.ApplicationDbContext db,
        IOptions<KdpSpecs> specs,
        KdpCalculationService calc,
        ILogger<PublishingProjectService> logger)
    {
        _db = db;
        _specs = specs.Value;
        _calc = calc;
        _logger = logger;
    }

    public async Task<Project> CreateProjectAsync(int userId, string title, string? authorName, CancellationToken ct = default)
    {
        var defaultTrim = _specs.TrimPresets.FirstOrDefault(t => t.IsDefault)
                          ?? _specs.TrimPresets.First(t => t.Key == "6x9");
        var margins = _calc.GetRecommendedMargins(200, false);

        var project = new Project
        {
            UserId = userId,
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled manuscript" : title.Trim(),
            AuthorName = authorName,
            ProjectType = ProjectType.Paperback,
            TrimWidthIn = defaultTrim.WidthIn,
            TrimHeightIn = defaultTrim.HeightIn,
            PaperType = PaperType.White,
            Status = ProjectStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        project.LayoutProfiles.Add(new LayoutProfile
        {
            IsEbookProfile = false,
            MarginTopIn = margins.TopIn,
            MarginBottomIn = margins.BottomIn,
            MarginOutsideIn = margins.OutsideIn,
            MarginInsideIn = margins.InsideIn,
            UseRecommendedMargins = true,
            MarginsLocked = true
        });
        project.LayoutProfiles.Add(new LayoutProfile
        {
            IsEbookProfile = true,
            UseRecommendedMargins = false,
            ShowPageNumbers = false,
            BodyFontSizePt = 16,
            LineSpacing = 1.5
        });

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);
        return project;
    }

    public Task<Project?> GetForUserAsync(int projectId, int userId, CancellationToken ct = default) =>
        _db.Projects
            .Include(p => p.LayoutProfiles)
            .Include(p => p.CoverProjects)
            .Include(p => p.ExportJobs)
            .Include(p => p.ManuscriptVersions)
            .ThenInclude(v => v.Sections)
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

    public async Task<ManuscriptVersion> SaveManuscriptVersionAsync(
        Project project,
        string originalFileName,
        string storedPath,
        ManuscriptSourceFormat format,
        ManuscriptImportResult import,
        CancellationToken ct = default)
    {
        foreach (var v in project.ManuscriptVersions.Where(x => x.IsActive))
            v.IsActive = false;

        var next = project.ManuscriptVersions.Count == 0
            ? 1
            : project.ManuscriptVersions.Max(v => v.VersionNumber) + 1;

        var version = new ManuscriptVersion
        {
            ProjectId = project.Id,
            VersionNumber = next,
            OriginalFileName = originalFileName,
            StoredFilePath = storedPath,
            SourceFormat = format,
            ParsedStructureJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                import.Stats.ChapterCount,
                import.Stats.WordCount,
                import.Stats.ImageCount,
                sections = import.Sections.Select(s => new { s.Title, s.MatterType, s.SectionKind })
            }),
            UploadedAt = DateTime.UtcNow,
            IsActive = true,
            Sections = import.Sections.Select(s => new BookSection
            {
                MatterType = s.MatterType,
                SectionKind = s.SectionKind,
                Title = s.Title,
                OrderIndex = s.OrderIndex,
                ContentHtml = s.ContentHtml,
                StartsOnRecto = s.StartsOnRecto,
                LayoutTemplate = LayoutTemplate.NormalText
            }).ToList()
        };

        _db.ManuscriptVersions.Add(version);
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var retain = Math.Max(1, _specs.ManuscriptVersionRetainCount);
        var old = await _db.ManuscriptVersions
            .Where(v => v.ProjectId == project.Id)
            .OrderByDescending(v => v.VersionNumber)
            .Skip(retain)
            .ToListAsync(ct);
        if (old.Count > 0)
        {
            _db.ManuscriptVersions.RemoveRange(old);
            await _db.SaveChangesAsync(ct);
        }

        return version;
    }

    public async Task RestoreVersionAsync(Project project, int versionId, CancellationToken ct = default)
    {
        var versions = await _db.ManuscriptVersions.Where(v => v.ProjectId == project.Id).ToListAsync(ct);
        var target = versions.FirstOrDefault(v => v.Id == versionId)
            ?? throw new InvalidOperationException("Version not found.");
        foreach (var v in versions) v.IsActive = v.Id == versionId;
        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Restored manuscript version {VersionId} for project {ProjectId}", versionId, project.Id);
    }

    public async Task UpsertLayoutProfileAsync(Project project, LayoutProfile incoming, bool ebook, CancellationToken ct = default)
    {
        var profile = project.LayoutProfiles.FirstOrDefault(p => p.IsEbookProfile == ebook);
        if (profile == null)
        {
            incoming.ProjectId = project.Id;
            incoming.IsEbookProfile = ebook;
            _db.LayoutProfiles.Add(incoming);
        }
        else
        {
            profile.MarginTopIn = incoming.MarginTopIn;
            profile.MarginBottomIn = incoming.MarginBottomIn;
            profile.MarginOutsideIn = incoming.MarginOutsideIn;
            profile.MarginInsideIn = incoming.MarginInsideIn;
            profile.UseRecommendedMargins = incoming.UseRecommendedMargins;
            profile.MarginsLocked = incoming.MarginsLocked;
            profile.BleedMode = incoming.BleedMode;
            profile.BodyFontFamily = incoming.BodyFontFamily;
            profile.BodyFontSizePt = incoming.BodyFontSizePt;
            profile.LineSpacing = incoming.LineSpacing;
            profile.ParagraphSpacingBeforePt = incoming.ParagraphSpacingBeforePt;
            profile.ParagraphSpacingAfterPt = incoming.ParagraphSpacingAfterPt;
            profile.TextAlignment = incoming.TextAlignment;
            profile.FirstLineIndentIn = incoming.FirstLineIndentIn;
            profile.NoIndentOnFirstPara = incoming.NoIndentOnFirstPara;
            profile.H1FontFamily = incoming.H1FontFamily;
            profile.H1FontSizePt = incoming.H1FontSizePt;
            profile.H2FontFamily = incoming.H2FontFamily;
            profile.H2FontSizePt = incoming.H2FontSizePt;
            profile.TocFontSizePt = incoming.TocFontSizePt;
            profile.HeaderFooterFontSizePt = incoming.HeaderFooterFontSizePt;
            profile.PageNumberPosition = incoming.PageNumberPosition;
            profile.ShowPageNumbers = incoming.ShowPageNumbers;
            profile.ChaptersStartOnRecto = incoming.ChaptersStartOnRecto;
        }

        if (!ebook && incoming.UseRecommendedMargins)
        {
            var m = _calc.GetRecommendedMargins(Math.Max(1, project.PageCount), project.HasBleed);
            var p = project.LayoutProfiles.First(x => !x.IsEbookProfile);
            p.MarginTopIn = m.TopIn;
            p.MarginBottomIn = m.BottomIn;
            p.MarginOutsideIn = m.OutsideIn;
            p.MarginInsideIn = m.InsideIn;
            p.MarginsLocked = true;
            p.BleedMode = project.HasBleed ? BleedMode.AllSides : BleedMode.None;
        }

        project.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateProjectSetupAsync(
        Project project,
        ProjectType type,
        double trimW,
        double trimH,
        bool customTrim,
        PaperType paper,
        bool hasBleed,
        CancellationToken ct = default)
    {
        project.ProjectType = type;
        project.TrimWidthIn = trimW;
        project.TrimHeightIn = trimH;
        project.IsCustomTrim = customTrim;
        project.PaperType = paper;
        project.HasBleed = hasBleed;
        project.UpdatedAt = DateTime.UtcNow;

        var print = project.LayoutProfiles.FirstOrDefault(p => !p.IsEbookProfile);
        if (print is { UseRecommendedMargins: true })
        {
            var m = _calc.GetRecommendedMargins(Math.Max(1, project.PageCount), hasBleed);
            print.MarginTopIn = m.TopIn;
            print.MarginBottomIn = m.BottomIn;
            print.MarginOutsideIn = m.OutsideIn;
            print.MarginInsideIn = m.InsideIn;
            print.BleedMode = hasBleed ? BleedMode.AllSides : BleedMode.None;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveSectionsAsync(int versionId, IReadOnlyList<BookSection> updates, CancellationToken ct = default)
    {
        var existing = await _db.BookSections.Where(s => s.ManuscriptVersionId == versionId).ToListAsync(ct);
        foreach (var u in updates)
        {
            var row = existing.FirstOrDefault(e => e.Id == u.Id);
            if (row == null) continue;
            row.Title = u.Title;
            row.OrderIndex = u.OrderIndex;
            row.MatterType = u.MatterType;
            row.SectionKind = u.SectionKind;
            row.ContentHtml = u.ContentHtml;
            row.IsBlank = u.IsBlank;
            row.StartsOnRecto = u.StartsOnRecto;
            row.LayoutTemplate = u.LayoutTemplate;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdatePageNumbersAsync(
        Project project,
        ManuscriptVersion version,
        int totalPages,
        IReadOnlyDictionary<int, int> sectionStartPages,
        CancellationToken ct = default)
    {
        project.PageCount = totalPages;
        project.UpdatedAt = DateTime.UtcNow;
        foreach (var section in version.Sections)
        {
            if (sectionStartPages.TryGetValue(section.Id, out var page))
                section.StartPageNumber = page;
        }

        var print = project.LayoutProfiles.FirstOrDefault(p => !p.IsEbookProfile);
        if (print is { UseRecommendedMargins: true })
        {
            var m = _calc.GetRecommendedMargins(totalPages, project.HasBleed);
            print.MarginInsideIn = m.InsideIn;
            print.MarginTopIn = m.TopIn;
            print.MarginBottomIn = m.BottomIn;
            print.MarginOutsideIn = m.OutsideIn;
        }

        await _db.SaveChangesAsync(ct);
    }
}
