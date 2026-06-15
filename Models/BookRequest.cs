namespace EBookDashboard.Models;

/// <summary>User book-generation settings for whole-manuscript HTML output.</summary>
public class BookRequest
{
    public string BookTitle { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Language { get; set; } = "English";
    public int ChaptersCount { get; set; } = 5;
    public int MinWordsPerChapter { get; set; } = 800;
    public string ToneDescription { get; set; } = "descriptive";
}
