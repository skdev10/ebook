namespace EBookDashboard.Models;

/// <summary>User book-generation settings for whole-manuscript HTML output.</summary>
public class BookRequest
{
    public string BookTitle { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public string Language { get; set; } = "English";
    public int ChaptersCount { get; set; } = 12;
    public int MinWordsPerChapter { get; set; } = 1800;
    public string ToneDescription { get; set; } = "published trade nonfiction — clear, argument-driven, 5.5×8.5 hardcover/paperback";
}
