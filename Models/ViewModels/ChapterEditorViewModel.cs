namespace EBookDashboard.Models.ViewModels;

/// <summary>Chapter row for the AI writer UI and formatting workflow.</summary>
public class ChapterEditorViewModel
{
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string GeneratedContent { get; set; } = string.Empty;
    public bool IsFinalized { get; set; }
    public string Status { get; set; } = "Draft";
    public int? ResponseId { get; set; }

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Title) || Title.Equals($"Chapter {Number}", StringComparison.OrdinalIgnoreCase)
            ? $"Chapter {Number}"
            : $"Chapter {Number}: {Title.Trim()}";
}

/// <summary>Book-level progress for the writer → formatting workflow.</summary>
public class BookWritingProgressViewModel
{
    public int BookId { get; set; }
    public int TotalChapters { get; set; }
    public int FinalizedChapters { get; set; }
    public bool AllFinalized => TotalChapters > 0 && FinalizedChapters >= TotalChapters;
    public IReadOnlyList<ChapterEditorViewModel> Chapters { get; set; } = Array.Empty<ChapterEditorViewModel>();
}
