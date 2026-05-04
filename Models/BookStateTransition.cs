using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    /// <summary>
    /// Immutable history row for book creation and every status change (admin Progress Tracker / audit).
    /// </summary>
    [Table("bookstatetransitions")]
    public class BookStateTransition
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int BookStateTransitionId { get; set; }

        [Required]
        public int BookId { get; set; }

        /// <summary>Book owner (Users.UserId).</summary>
        [Required]
        public int UserId { get; set; }

        /// <summary>User who performed the change when known (same as owner for self-service).</summary>
        public int? ActorUserId { get; set; }

        [MaxLength(100)]
        public string? FromStatus { get; set; }

        [Required]
        [MaxLength(100)]
        public string ToStatus { get; set; } = string.Empty;

        /// <summary>Created | StatusChanged</summary>
        [Required]
        [MaxLength(50)]
        public string Kind { get; set; } = string.Empty;

        [MaxLength(2000)]
        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
