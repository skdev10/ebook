using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("authorplans")]
    public class AuthorPlans
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AuthorPlanId { get; set; }

        // 🔹 Author and User Relation
        [Required]
        [ForeignKey("AuthorId")]
        public int AuthorId { get; set; }

        [Required]
        public int UserId { get; set; }
        [Required]
        [ForeignKey("PlanId")]
        public int PlanId { get; set; }
        [Required]
        [StringLength(255)]
        public string PlanName { get; set; } = string.Empty;
        [StringLength(500)]
        public string? PlanDescription { get; set; }= string.Empty;
        [Required]
        [Column(TypeName = "decimal(10,2)")]
        public decimal PlanRate { get; set; } = 0.00m;
        [Required]
        public int PlanDays { get; set; } = 30;

        [Required]
        public int PlanHours { get; set; } = 720;
        [Required]
        public int MaxEBooks { get; set; } = 0;
        // 🔹 Timing and Status Fields
        [Required]
        public DateTime StartDate { get; set; } = DateTime.UtcNow;

        [Required]
        public DateTime EndDate { get; set; } = new DateTime(1980, 1, 1);
        public int IsActive { get; set; } = 1;

        public bool TrialUsed { get; set; } = false;
        [StringLength(255)]
        public string? PaymentReference { get; set; }= string.Empty;

        [Column("BillId")]
        public int? BillId { get; set; }=0;
        // 🔹 Plan Relation and Snapshot Data

        public string Currency { get; set; } = "usd";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CancelledAt { get; set; } = new DateTime(1980, 1, 1);

        [StringLength(500)]
        public string? CancellationReason { get; set; }= string.Empty;
        // Navigation properties - check these!
        public Authors? Author { get; set; }   
        public Plans? Plan { get; set; }       
        // If you have a relationship to AuthorBills, make sure it's configured correctly
        public virtual ICollection<AuthorBills> AuthorBills { get; set; } = new List<AuthorBills>();
    }
}
