namespace EBookDashboard.Models.DTO
{
    public class SaveBookFormattingRequest
    {
        public int BookId { get; set; }
        public string? Format { get; set; }      // Ebook / Print / Both
        public string? InteriorStyle { get; set; } // Novel, Modern, Classic, Minimalist, ElegantTrade, …
        public string? TextSize { get; set; }     // Small / Medium / Large
        public string? LineSpacing { get; set; }  // 1.4 / 1.6 / 1.8 / 2
        /// <summary>Primary platform for PDF layout (matches first checked platform in UI when omitted).</summary>
        public string? PublishingPlatform { get; set; }
        public string? PublishingPlatforms { get; set; } // Comma-separated
        public string? DraftStateJson { get; set; } // Full client state snapshot
        /// <summary>Preview pagination page count from Book Formatting UI (source of truth for KDP spine).</summary>
        public int? PreviewPageCount { get; set; }
    }
}
