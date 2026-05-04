namespace EBookDashboard.Models.ViewModels
{
    /// <summary>
    /// View model for EBook Hub list: Books joined with Users (Title, Description, WordCount, Author FullName).
    /// </summary>
    public class EBookHubItemViewModel
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int WordCount { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? CoverImagePath { get; set; }
        public string Genre { get; set; } = string.Empty;
    }
}
