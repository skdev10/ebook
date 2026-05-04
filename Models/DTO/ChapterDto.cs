namespace EBookDashboard.Models.DTO
{
    public class ChapterDto
    {
        public int ResponseId { get; set; }
        public int ChapterNumber { get; set; }
        public string? RequestData { get; set; } = string.Empty;
        public string? Title { get; set; } = string.Empty;
        public string? Content { get; set; } = string.Empty;
        public string? StatusCode { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; }=DateTime.Now;

        /// <summary>Optional HTML block shown in PDF export (e.g. generation / finalization timestamps).</summary>
        public string? ExportMetaHtml { get; set; }
    }
}
