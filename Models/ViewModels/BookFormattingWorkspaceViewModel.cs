namespace EBookDashboard.Models.ViewModels;

/// <summary>Workspace model for GET /Books/Formatting/{bookId}.</summary>
public sealed class BookFormattingWorkspaceViewModel
{
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "Untitled Book";
    public string Format { get; set; } = "Ebook";
    /// <summary>Normalized binding for preview: Ebook | Paperback | Hardcover.</summary>
    public string BindingType { get; set; } = "Ebook";
    /// <summary>print = two-page spread; ebook = Kindle-style reflow.</summary>
    public string PreviewMode { get; set; } = "ebook";
    public bool IsPrintPreview => PreviewMode.Equals("print", StringComparison.OrdinalIgnoreCase);
    public string InteriorStyle { get; set; } = "Novel";
    public string TextSize { get; set; } = "Medium";
    public string LineSpacing { get; set; } = "1.6";
    public string TrimSizeLabel { get; set; } = "6 x 9 in";
    public decimal MarginTopIn { get; set; } = 0.625m;
    public decimal MarginBottomIn { get; set; } = 0.875m;
    public decimal MarginInsideIn { get; set; } = 0.8125m;
    public decimal MarginOutsideIn { get; set; } = 0.625m;
    public string? ManuscriptPath { get; set; }
    public DateTime? LastManuscriptUploadedAtUtc { get; set; }
    public string? LastManuscriptFileName { get; set; }
    public IReadOnlyList<FormattingTocItem> Toc { get; set; } = Array.Empty<FormattingTocItem>();
    public IReadOnlyList<FormattingPageItem> Pages { get; set; } = Array.Empty<FormattingPageItem>();
    public IReadOnlyList<FormattingChapterItem> Chapters { get; set; } = Array.Empty<FormattingChapterItem>();
    public IReadOnlyList<ManuscriptVersionItem> ManuscriptVersions { get; set; } = Array.Empty<ManuscriptVersionItem>();
    public bool HasContent => Chapters.Count > 0;
}

public sealed class FormattingChapterItem
{
    public int ChapterNo { get; set; }
    public string Title { get; set; } = "";
    public string Matter { get; set; } = "body"; // front | body | back
    public string ContentHtml { get; set; } = "";
    public int WordCount { get; set; }
    public int StartPage { get; set; }
    public int PageCount { get; set; }
}

public sealed class FormattingTocItem
{
    public int ChapterNo { get; set; }
    public string Title { get; set; } = "";
    public int StartPage { get; set; }
    public string Matter { get; set; } = "body";
}

public sealed class FormattingPageItem
{
    public int PageNumber { get; set; }
    public int ChapterNo { get; set; }
    public string Label { get; set; } = "";
}

public sealed class ManuscriptVersionItem
{
    public string Path { get; set; } = "";
    public string FileName { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; }
    public bool Active { get; set; }
}
