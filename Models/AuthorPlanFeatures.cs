using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("authorplanfeatures")]
    public class AuthorPlanFeatures
    {
        [Key]
        [Column("AuthorFeaturesId")]
        public int AuthorFeaturesId { get; set; }

        [Column("AuthorId")]
        public int AuthorId { get; set; }

        [Column("UserId")]
        public int UserId { get; set; } = 0;

        [Column("UserEmail")]
        [StringLength(145)]
        public string? UserEmail { get; set; }= string.Empty;

        [Column("FeatureId")]
        public int? FeatureId { get; set; }

        [Column("PlanId")]
        public int? PlanId { get; set; }
        [Column("BillId")]
        public int? BillId { get; set; }

        [Column("FeatureName")]
        [StringLength(45)]
        public string? FeatureName { get; set; }= string.Empty;

        [Column("Description")]
        [StringLength(245)]
        public string? Description { get; set; }= string.Empty;

        [Column("FeatureRate", TypeName = "decimal(10,2)")]
        public decimal FeatureRate { get; set; } = 0.00m;

        [Column("Currency")]
        [StringLength(12)]
        public string? Currency { get; set; }="USD";

        [Column("TotalAmount", TypeName = "decimal(10,2)")]
        public decimal TotalAmount { get; set; } = 0.00m;

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("ExpiryDate")]
        public DateTime? ExpiryDate { get; set; } = new DateTime(1980, 1, 1);

        public string? UsageLimit { get; set; } = "";
        [Column("Status")]
        [StringLength(12)]
        public string? Status { get; set; }="Pending";

        [Column("isActive")]
        public int? IsActive { get; set; }=1;

        // ✅ Navigation Property (Many-to-One)
        [ForeignKey("FeatureId")]
        public PlanFeatures? PlanFeature { get; set; }

        [ForeignKey("BillId")] // Add this navigation property
        public AuthorBills? AuthorBill { get; set; }
    }
}
