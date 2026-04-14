using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("parsedbookcontent")]
    public class ParsedBookContent
    {
        [Key]
        public int ParsedContentId { get; set; }

        [Required]
        public int ResponseId { get; set; } // FK to APIRawResponse

        public int? BookId { get; set; }
        public int? UserId { get; set; }

        // Book Content
        public string? ChapterTitle { get; set; }
        
        // Store parsed sections as JSON
        [Column(TypeName = "TEXT")]
        public string? BookSectionsJson { get; set; }

        // Highlights
        public string? HighlightChapterName { get; set; }
        
        // Store parsed highlights as JSON
        [Column(TypeName = "TEXT")]
        public string? HighlightsJson { get; set; }

        // Store full parsed result as JSON for reference
        [Column(TypeName = "TEXT")]
        public string? FullParsedDataJson { get; set; }

        // New fields from table structure
        [MaxLength(245)]
        [Column("chapter_name")]
        public string? ChapterName { get; set; }

        [Column("user_input", TypeName = "longtext")]
        public string? UserInput { get; set; }

        [Column("content", TypeName = "longtext")]
        public string? Content { get; set; }

        [Column("highlight_of_previous_chapter", TypeName = "longtext")]
        public string? HighlightOfPreviousChapter { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
