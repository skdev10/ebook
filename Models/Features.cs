using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("features")]
    public class Features
    {
        [Required]
        [Key]
        public int Id { get; set; }
        
        [Required]
        [StringLength(50)]
        public string Key { get; set; } = string.Empty;
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;
        
        [Required]
        [Column(TypeName = "decimal(10,2)")]
        public decimal Price { get; set; }
        
        [Required]
        [StringLength(20)]
        public string Type { get; set; } = "Basic"; // Basic, Premium, Marketing
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation properties
        public ICollection<UserFeatures> UserFeatures { get; set; } = new List<UserFeatures>();
    }
}