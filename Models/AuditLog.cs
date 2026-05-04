using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models
{
    public class AuditLog
    {
        [Key]
        public int AuditLogId { get; set; }
        
        [Required]
        public int? UserId { get; set; } // Admin who performed the action
        
        [Required]
        [MaxLength(100)]
        public string Action { get; set; } = string.Empty; // Create, Update, Delete, Login, etc.
        
        [Required]
        [MaxLength(100)]
        public string EntityType { get; set; } = string.Empty; // User, Book, Plan, etc.
        
        public int? EntityId { get; set; } // ID of the affected entity
        
        [MaxLength(500)]
        public string? Description { get; set; }
        
        [MaxLength(50)]
        public string? IpAddress { get; set; }
        
        [MaxLength(500)]
        public string? UserAgent { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation
        public Users? User { get; set; }
    }
}

