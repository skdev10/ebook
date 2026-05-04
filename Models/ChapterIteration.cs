using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    /// <summary>
    /// One row per generation/regeneration of a chapter. All rows for the same logical chapter
    /// (same book + chapter number + user) share <see cref="ChapterSeriesGuid"/>.
    /// </summary>
    [Table("chapter_iterations")]
    public class ChapterIteration
    {
        [Key]
        public int ChapterIterationId { get; set; }

        [Required]
        [Column(TypeName = "char(36)")]
        public Guid ChapterSeriesGuid { get; set; }

        public int BookId { get; set; }
        public int UserId { get; set; }
        public int ChapterNumber { get; set; }
        public int IterationNumber { get; set; }

        /// <summary>Optional FK to <c>apirawresponse.ResponseId</c> for traceability.</summary>
        public int? ResponseId { get; set; }

        [MaxLength(500)]
        public string? Title { get; set; }

        [Column(TypeName = "longtext")]
        public string Content { get; set; } = string.Empty;

        [Column(TypeName = "date")]
        public DateTime GenerationDate { get; set; }

        [Column(TypeName = "time(6)")]
        public TimeSpan GenerationTime { get; set; }

        public bool IsFinalized { get; set; }
        public bool IsLocked { get; set; }

        [Column(TypeName = "date")]
        public DateTime? FinalizedDate { get; set; }

        [Column(TypeName = "time(6)")]
        public TimeSpan? FinalizedTime { get; set; }
    }
}
