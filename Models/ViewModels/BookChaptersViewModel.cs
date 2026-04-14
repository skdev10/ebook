namespace EBookDashboard.Models.ViewModels
{
    public class BookChaptersViewModel
    {
        // Book info
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Description { get; set; }

        // Chapters
        public List<APIRawResponse> Chapters { get; set; } = new();

    }
}
