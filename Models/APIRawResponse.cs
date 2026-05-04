using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("apirawresponse")]
    public class APIRawResponse
    {
        [Key]
        public int ResponseId { get; set; }

        [Required]
        public string? Endpoint { get; set; }= string.Empty;
        [Required]
        public int Chapter { get; set; } = 0;
        [Required]
        public string? Title { get; set; } = string.Empty;
        public string? ChapterNames { get; set; } = string.Empty;
        [Required]
        public string? RequestData { get; set; } = string.Empty;

        [Required]
        public string? ResponseData { get; set; } = string.Empty;

        public int? UserId { get; set; }
        public int? BookId { get; set; }

        public string? StatusCode { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = new DateTime(1980, 1, 1);


        // Optional: Store the parsed book ID for reference
        public string? ParsedBookId { get; set; } = string.Empty;
        public string? Content { get; set; } = string.Empty;
    }
}
