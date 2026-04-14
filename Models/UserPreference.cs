using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models
{
    public class UserPreference
    {
        [Key]
        public int PreferenceId { get; set; }
        
        [Required]
        public int UserId { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty; // e.g., "theme", "language", "sidebar_collapsed"
        
        [MaxLength(500)]
        public string? Value { get; set; } // e.g., "dark", "en", "true"
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation
        public Users User { get; set; } = null!;
    }
}

