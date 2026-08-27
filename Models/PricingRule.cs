using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    public enum PricingUnit
    {
        PerPage = 0,
        PerItem = 1,
        Flat = 2
    }

    public class PricingRule
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(64)]
        public string Key { get; set; } = string.Empty;

        [Required]
        [MaxLength(128)]
        public string DisplayName { get; set; } = string.Empty;

        [MaxLength(512)]
        public string? Description { get; set; }

        [Required]
        public PricingUnit Unit { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitAmount { get; set; }

        [Required]
        public int FreeAllowance { get; set; } = 0;

        [Required]
        [MaxLength(3)]
        public string Currency { get; set; } = "USD";

        [Required]
        public bool IsActive { get; set; } = true;

        [Required]
        public DateTime UpdatedAtUtc { get; set; }
    }
}
