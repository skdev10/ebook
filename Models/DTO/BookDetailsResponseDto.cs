namespace EBookDashboard.Models.DTO
{
    public class BookDetailsResponseDto
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        //public int ResponseId { get; set; }
        public int BookId { get; set; }
        public string BookTitle { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public string? Genre { get; set; }
        public string? AuthorName { get; set; }
        public string? CoverImagePath { get; set; }
        public int TotalChapters { get; set; }
        public List<ChapterDto> Chapters { get; set; } = new List<ChapterDto>();
        public string? BookContentHtml { get; set; }
    }
}
