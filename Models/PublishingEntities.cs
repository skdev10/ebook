using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models;

/// <summary>KDP-oriented publishing project (extends the existing Books model; does not replace it).</summary>
[Table("projects")]
public class Project
{
    [Key]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required, MaxLength(250)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Subtitle { get; set; }

    [MaxLength(250)]
    public string? AuthorName { get; set; }

    public ProjectType ProjectType { get; set; } = ProjectType.Ebook;

    public double TrimWidthIn { get; set; } = 6.0;

    public double TrimHeightIn { get; set; } = 9.0;

    public bool IsCustomTrim { get; set; }

    public PaperType PaperType { get; set; } = PaperType.White;

    public bool HasBleed { get; set; }

    /// <summary>Last resolved page count from pagination (0 until rendered).</summary>
    public int PageCount { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.Draft;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ManuscriptVersion> ManuscriptVersions { get; set; } = new List<ManuscriptVersion>();
    public ICollection<LayoutProfile> LayoutProfiles { get; set; } = new List<LayoutProfile>();
    public ICollection<CoverProject> CoverProjects { get; set; } = new List<CoverProject>();
    public ICollection<ExportJob> ExportJobs { get; set; } = new List<ExportJob>();
}

/// <summary>Immutable uploaded manuscript snapshot for a project.</summary>
[Table("manuscript_versions")]
public class ManuscriptVersion
{
    [Key]
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public int VersionNumber { get; set; }

    [MaxLength(500)]
    public string? OriginalFileName { get; set; }

    [MaxLength(1000)]
    public string? StoredFilePath { get; set; }

    public ManuscriptSourceFormat SourceFormat { get; set; }

    [Column(TypeName = "longtext")]
    public string? ParsedStructureJson { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; }

    public Project? Project { get; set; }
    public ICollection<BookSection> Sections { get; set; } = new List<BookSection>();
}

/// <summary>Structured section within a manuscript version (supports nested sub-sections).</summary>
[Table("book_sections")]
public class BookSection
{
    [Key]
    public int Id { get; set; }

    public int ManuscriptVersionId { get; set; }

    public int? ParentSectionId { get; set; }

    public MatterType MatterType { get; set; } = MatterType.Body;

    public SectionKind SectionKind { get; set; } = SectionKind.Chapter;

    [MaxLength(500)]
    public string? Title { get; set; }

    public int OrderIndex { get; set; }

    [Column(TypeName = "longtext")]
    public string? ContentHtml { get; set; }

    public bool StartsOnRecto { get; set; }

    public bool IsBlank { get; set; }

    /// <summary>Computed after layout/render; null until pagination runs.</summary>
    public int? StartPageNumber { get; set; }

    public LayoutTemplate LayoutTemplate { get; set; } = LayoutTemplate.NormalText;

    public ManuscriptVersion? ManuscriptVersion { get; set; }
    public BookSection? ParentSection { get; set; }
    public ICollection<BookSection> ChildSections { get; set; } = new List<BookSection>();
}

/// <summary>Per-project interior typography and margin profile.</summary>
[Table("layout_profiles")]
public class LayoutProfile
{
    [Key]
    public int Id { get; set; }

    public int ProjectId { get; set; }

    /// <summary>When true, this profile is the ebook style set (independent of print).</summary>
    public bool IsEbookProfile { get; set; }

    public double MarginTopIn { get; set; } = 0.25;
    public double MarginBottomIn { get; set; } = 0.25;
    public double MarginOutsideIn { get; set; } = 0.25;
    public double MarginInsideIn { get; set; } = 0.375;

    public bool UseRecommendedMargins { get; set; } = true;
    public bool MarginsLocked { get; set; }

    public BleedMode BleedMode { get; set; } = BleedMode.None;

    [MaxLength(100)]
    public string BodyFontFamily { get; set; } = "Georgia";

    public double BodyFontSizePt { get; set; } = 11;

    public double LineSpacing { get; set; } = 1.15;

    public double ParagraphSpacingBeforePt { get; set; }
    public double ParagraphSpacingAfterPt { get; set; }

    [MaxLength(50)]
    public string TextAlignment { get; set; } = "Justify";

    public double FirstLineIndentIn { get; set; } = 0.25;

    public bool NoIndentOnFirstPara { get; set; } = true;

    [MaxLength(100)]
    public string H1FontFamily { get; set; } = "Georgia";

    public double H1FontSizePt { get; set; } = 18;

    [MaxLength(100)]
    public string H2FontFamily { get; set; } = "Georgia";

    public double H2FontSizePt { get; set; } = 14;

    public double TocFontSizePt { get; set; } = 11;
    public double HeaderFooterFontSizePt { get; set; } = 9;

    [MaxLength(50)]
    public string PageNumberPosition { get; set; } = "BottomCenter";

    public bool ShowPageNumbers { get; set; } = true;
    public bool ChaptersStartOnRecto { get; set; } = true;

    public Project? Project { get; set; }
}

/// <summary>Cover design artifact linked to a project.</summary>
[Table("cover_projects")]
public class CoverProject
{
    [Key]
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public CoverType CoverType { get; set; } = CoverType.EbookFront;

    public double ComputedSpineWidthIn { get; set; }
    public double ComputedTotalWidthIn { get; set; }
    public double ComputedTotalHeightIn { get; set; }

    public int PageCountUsedForSpine { get; set; }

    [Column(TypeName = "longtext")]
    public string? DesignJson { get; set; }

    [MaxLength(1000)]
    public string? UploadedFilePath { get; set; }

    [MaxLength(1000)]
    public string? ThumbnailPath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Project? Project { get; set; }
}

/// <summary>Async export job for print PDF, EPUB, or cover PDF.</summary>
[Table("export_jobs")]
public class ExportJob
{
    [Key]
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public ExportFormat Format { get; set; }

    public bool IncludeCover { get; set; }
    public bool IncludeFrontMatter { get; set; } = true;
    public bool IncludeBackMatter { get; set; } = true;

    public ExportJobStatus Status { get; set; } = ExportJobStatus.Queued;

    [MaxLength(1000)]
    public string? OutputFilePath { get; set; }

    public long? FileSizeBytes { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Project? Project { get; set; }
}
