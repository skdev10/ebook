namespace EBookDashboard.Models;

/// <summary>Publishing project type (interior / binding intent).</summary>
public enum ProjectType
{
    Ebook = 0,
    Paperback = 1,
    Hardcover = 2
}

/// <summary>Lifecycle status for a publishing <see cref="Project"/>.</summary>
public enum ProjectStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2
}

/// <summary>Uploaded manuscript source format.</summary>
public enum ManuscriptSourceFormat
{
    Docx = 0,
    Pdf = 1,
    Txt = 2,
    Rtf = 3,
    Odt = 4,
    Epub = 5
}

/// <summary>Front / body / back matter classification.</summary>
public enum MatterType
{
    Front = 0,
    Body = 1,
    Back = 2
}

/// <summary>Semantic section kind within a manuscript.</summary>
public enum SectionKind
{
    TitlePage = 0,
    Copyright = 1,
    Dedication = 2,
    TableOfContents = 3,
    Chapter = 4,
    Section = 5,
    Appendix = 6,
    Index = 7,
    AboutAuthor = 8,
    Blank = 9,
    Custom = 10
}

/// <summary>Per-page layout template for print / children's books.</summary>
public enum LayoutTemplate
{
    NormalText = 0,
    FullWidthImage = 1,
    FullPageImage = 2,
    FullBleedImage = 3,
    ImageWithCaption = 4,
    TwoColumn = 5
}

/// <summary>Bleed mode for interior PDF layout.</summary>
public enum BleedMode
{
    None = 0,
    AllSides = 1
}

/// <summary>Cover artifact type for a project.</summary>
public enum CoverType
{
    EbookFront = 0,
    PaperbackWrap = 1,
    HardcoverWrap = 2
}

/// <summary>Export output format.</summary>
public enum ExportFormat
{
    PrintPdf = 0,
    Epub = 1,
    CoverPdf = 2
}

/// <summary>Export job lifecycle.</summary>
public enum ExportJobStatus
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}
