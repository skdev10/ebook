using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    public class TemporaryFeatures
    {
        [Required]
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string SessionId { get; set; } = string.Empty;
        
        [Required]
        public int FeatureId { get; set; }
        
        [Required]
        public string UserId { get; set; } = string.Empty;
        
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public Features? Feature { get; set; }
    }
}