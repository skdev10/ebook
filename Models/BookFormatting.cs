using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("bookformatting")]
    public class BookFormatting
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BookId { get; set; }

        [Required]
        public int UserId { get; set; }

        /// <summary>Ebook / Print / Both</summary>
        [MaxLength(50)]
        public string Format { get; set; } = "Ebook";

        /// <summary>Novel, Modern, Classic, Minimalist, ElegantTrade, etc.</summary>
        [MaxLength(50)]
        public string InteriorStyle { get; set; } = "Novel";

        /// <summary>Small / Medium / Large</summary>
        [MaxLength(20)]
        public string TextSize { get; set; } = "Medium";

        /// <summary>Line height multiplier stored as string (e.g. 1.4, 1.6, 1.8, 2).</summary>
        [MaxLength(20)]
        public string LineSpacing { get; set; } = "1.6";

        /// <summary>Primary export target for PDF trim/margins (first selected platform or explicit choice).</summary>
        [MaxLength(120)]
        public string? PublishingPlatform { get; set; }

        /// <summary>Comma-separated: Amazon KDP, IngramSpark, Barnes &amp; Noble, Just print-ready file</summary>
        [Column(TypeName = "varchar(500)")]
        public string? PublishingPlatforms { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
