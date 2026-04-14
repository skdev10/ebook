using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    public class Plans
    {
        [Required]
        [Key]
        public int PlanId { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public string PlanDescription { get; set; } = string.Empty;
        [Required]
        [Column(TypeName = "decimal(10,2)")]
        public decimal PlanRate { get; set; } = 0;
        [Required]
        public int PlanDays { get; set; } = 0; // DurationInDays
        public int PlanHours { get; set; } = 0;

        public int MaxEBooks { get; set; } = 0;
        public bool AllowDownloads { get; set; } = false;
        public bool AllowFullDashboard { get; set; } = false;
        public bool AllowAnalytics { get; set; } = false;
        public bool AllowPublishing { get; set; } = false;
        public int IsActive { get; set; } = 1;

        public DateTime CreateddAt { get; set; } = DateTime.UtcNow;
        public int MaxPages { get; set; } = 0;
        public int MaxChapters { get; set; } = 0;

        public string Currency { get; set; } = "usd";
        // Navigation
        public ICollection<AuthorPlans> AuthorPlans { get; set; } = new List<AuthorPlans>();


    }

}
