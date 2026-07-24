namespace EBookDashboard.Models.DTO
{
    /// <summary>Request body for persisting print back-cover summary text.</summary>
    public class SaveBackCoverSummaryRequest
    {
        public int BookId { get; set; }
        /// <summary>Back-cover blurb (preferred).</summary>
        public string? BackSummary { get; set; }
        /// <summary>Alias accepted for clients that still send Description.</summary>
        public string? Description { get; set; }
    }
}
