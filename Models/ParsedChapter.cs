namespace EBookDashboard.Models
{
    public class ParsedChapter
    {
        // This is just a TEMPORARY helper class - NOT a database table!
        public string? Title { get; set; }= string.Empty;
        public string? Content { get; set; } = string.Empty;
        public int ChapterNumber { get; set; }
    }
}
