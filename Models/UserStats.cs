using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    /// <summary>
    /// Per-user reading and authoring statistics. One row per user.
    /// Books Published and Books Generated are derived from Books table; these store user-editable stats.
    /// </summary>
    [Table("userstats")]
    public class UserStats
    {
        [Key]
        public int UserId { get; set; }

        public int BookId { get; set; }
        public int HoursRead { get; set; }
        public int PagesRead { get; set; }
        public int DayStreak { get; set; }
        public DateTime? UpdatedAt { get; set; }

        [ForeignKey(nameof(UserId))]
        public Users User { get; set; } = null!;
    }
}
