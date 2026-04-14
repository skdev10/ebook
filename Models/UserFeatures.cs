using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models
{
    public class UserFeatures
    {
        [Required]
        [Key]
        public int Id { get; set; }
        
        [Required]
        public string UserId { get; set; } = string.Empty;
        
        [Required]
        public int FeatureId { get; set; }
        
        public int? AuthorPlanId { get; set; }
        
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public Features? Feature { get; set; }
        public AuthorPlans? AuthorPlan { get; set; }
    }
}