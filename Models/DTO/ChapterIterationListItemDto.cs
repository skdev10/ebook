namespace EBookDashboard.Models.DTO
{
    /// <summary>One list entry per iteration — API returns these as separate JSON objects (not aggregated).</summary>
    public class ChapterIterationListItemDto
    {
        public int ChapterIterationId { get; set; }
        public Guid ChapterSeriesGuid { get; set; }
        public int IterationNumber { get; set; }
        public int ResponseId { get; set; }
        public string? Title { get; set; }
        public bool IsFinalized { get; set; }
        public bool IsLocked { get; set; }
        public string GenerationDate { get; set; } = "";
        public string GenerationTime { get; set; } = "";
        public string? FinalizedDate { get; set; }
        public string? FinalizedTime { get; set; }
        /// <summary>Latest raw API status (e.g. ReadOnly) when syncing from history.</summary>
        public string? StatusCode { get; set; }
    }
}
