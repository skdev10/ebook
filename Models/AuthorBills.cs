using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("authorbills")]
    public class AuthorBills
    {
        [Key]
        public int BillId { get; set; }
        [Column("AuthorId")]
        public int AuthorId { get; set; }

        [Column("UserId")]
        public int UserId { get; set; } = 0;

        [Column("UserEmail")]
        [StringLength(145)]
        public string? UserEmail { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        [Column("Currency")]
        [StringLength(12)]
        public string? Currency { get; set; } = "usd";
        [Column("Discount", TypeName = "decimal(10,2)")]
        public decimal Discount { get; set; } = 0.00m;

        [Column("TotalAmount", TypeName = "decimal(10,2)")]
        public decimal TotalAmount { get; set; } = 0.00m;

        [Column("TaxAmount", TypeName = "decimal(10,2)")]
        public decimal TaxAmount { get; set; } = 0.00m;
        [Column("PaymentReference")]
        [StringLength(245)]
        public string? PaymentReference { get; set; } = string.Empty;
        [Column("Status")]
        [StringLength(12)]
        public string? Status { get; set; }
        public DateTime ClosingDate { get; set; } = DateTime.Now;
        public DateTime CancelledAt { get; set; } = DateTime.Now;
        public string? CancellationReason { get; set; } = string.Empty;
        [Column("isActive")]
        public int IsActive { get; set; } = 1;
        // ✅ Navigation Property (One-to-Many)
        public virtual ICollection<AuthorPlanFeatures> AuthorPlanFeatures { get; set; } = new List<AuthorPlanFeatures>();

        public virtual ICollection<AuthorPlans> AuthorPlans { get; set; } = new List<AuthorPlans>();

    }
}
