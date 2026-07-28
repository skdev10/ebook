namespace EBookDashboard.Models.DTO
{
    /// <summary>Request body for persisting optional front-cover subtitle.</summary>
    public class SaveCoverSubtitleRequest
    {
        public int BookId { get; set; }
        public string? Subtitle { get; set; }
    }
}
