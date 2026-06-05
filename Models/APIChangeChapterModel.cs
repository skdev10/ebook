namespace EBookDashboard.Models
{
    public class APIChangeChapterModel
    {
        public string? UserId { get; set; } = string.Empty;       // The user making the change
        public string? BookId { get; set; } = string.Empty;      // The book to which the chapter belongs
        public string? Chapter { get; set; } = string.Empty;     // Chapter number or ID (as string for flexibility)
        public string? NewContent { get; set; } = string.Empty;     // The updated text/content entered by the user
        /// <summary>Optional display title for this chapter when recording a version.</summary>
        public string? ChapterTitle { get; set; }
        /// <summary>Topic / prompt text stored alongside the version for the writer UI.</summary>
        public string? Topic { get; set; }
        /// <summary>When true (default), append a new row to version history. Set false only for silent/bulk ops.</summary>
        public bool RecordVersion { get; set; } = true;
        /// <summary>When true, only update the chapter title (allowed on finalized/read-only chapters).</summary>
        public bool TitleOnly { get; set; }
        /// <summary>Optional book-level title from AI Writer (synced when DB title is still a placeholder).</summary>
        public string? BookTitle { get; set; }
    }
}
