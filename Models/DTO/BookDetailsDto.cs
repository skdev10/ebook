namespace EBookDashboard.Models.DTO
{
    public class BookDetailsDto
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Genre { get; set; } = string.Empty;
        public string Dedication { get; set; } = string.Empty;
        public string Ghostwriting { get; set; } = string.Empty;
        public string Epigraph { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalChapters { get; set; }
        public List<ChapterDto> Chapters { get; set; } = new();
        public int RawResponseId { get; set; } = 0;
        public string Endpoint { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
