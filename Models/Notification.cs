using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("notification")]
    public class Notification
    {
        [Key]
        public int NotificationId { get; set; }
        
        [Required]
        public int? UserId { get; set; } // Null for admin-wide notifications
        
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(1000)]
        public string Message { get; set; } = string.Empty;
        
        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = "Info"; // Info, Warning, Error, Success
        
        [Required]
        public bool IsRead { get; set; } = false;
        
        public string? Link { get; set; } // Optional link to related page
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? ReadAt { get; set; }
        
        // Navigation
        public Users? User { get; set; }
    }
}

