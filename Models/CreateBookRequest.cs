using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace EBookDashboard.Models
{
    [Table("createbookrequest")]
    public class CreateBookRequest
    {
        public int BookId { get; set; }
        public int AuthorId { get; set; }
        public int UserId { get; set; }
        public int CategoryId { get; set; }

        [MaxLength(250)]
        public string Title { get; set; } = string.Empty;

        public string? Subtitle { get; set; } = string.Empty;
        public string AuthorCode { get; set; } = string.Empty;
        public string BookCode { get; set; } = string.Empty;
        public int LanguageId { get; set; }
        public string Dedication { get; set; } = string.Empty;
        public string Ghostwriting { get; set; } = string.Empty;
        public string Epigraph { get; set; } = string.Empty;

        public string? CoverImagePath { get; set; } = null!; // FK to AspNetUsers

        public string? ManuscriptPath { get; set; } = null!; // FK to AspNetUsers

        public string Genre { get; set; } = string.Empty;
        [StringLength(500)]
        public string? Description { get; set; } = string.Empty;
        public int WordCount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; } = new DateTime(1980, 1, 1);
        public string Status { get; set; } = string.Empty;

       
    }
}
