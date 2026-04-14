using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("bookversion")]
    public class BookVersion
    {
        [Required]
        [Key]
        public int BookVersionId { get; set; }
        public int BookId { get; set; }
        public int? ChapterId { get; set; }
        public string ContentSnapshot { get; set; } = null!;
        public int ChangedByUserId { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Reason { get; set; } = null!;
        public string Status { get; set; } = string.Empty;
    }

}
