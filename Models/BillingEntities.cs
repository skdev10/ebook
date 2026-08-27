using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    public enum OrderStatus
    {
        Draft = 0,
        PendingPayment = 1,
        Paid = 2,
        Failed = 3,
        Cancelled = 4,
        Refunded = 5
    }

    public class Order
    {
        public int Id { get; set; }

        /// <summary>Matches <see cref="Users.UserId"/> (int in this repo, not a string).</summary>
        public int UserId { get; set; }

        public int? BookId { get; set; }

        public OrderStatus Status { get; set; } = OrderStatus.Draft;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Discount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "USD";

        [MaxLength(128)]
        public string? StripeCheckoutSessionId { get; set; }

        [MaxLength(128)]
        public string? StripePaymentIntentId { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? PaidAtUtc { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }

        [MaxLength(64)]
        public string PricingRuleKey { get; set; } = string.Empty;

        [MaxLength(512)]
        public string Description { get; set; } = string.Empty;

        public int Quantity { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitAmount { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal LineTotal { get; set; }

        [MaxLength(2000)]
        public string MetadataJson { get; set; } = "{}";

        public Order? Order { get; set; }
    }

    public enum EntitlementType
    {
        WritingPages = 0,
        CustomCover = 1,
        PremiumFormat = 2,
        PaperbackExport = 3,
        HardcoverExport = 4
    }

    public class Entitlement
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int? BookId { get; set; }
        public EntitlementType Type { get; set; }

        [MaxLength(64)]
        public string ScopeKey { get; set; } = string.Empty;

        public int Quantity { get; set; }

        /// <summary>Null for grandfathered entitlements that have no Stripe order.</summary>
        public int? OrderId { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public class BookBillingState
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        public int BilledPageCount { get; set; }
        public DateTime? LastBilledAtUtc { get; set; }
    }

    public class FormattingTemplate
    {
        public int Id { get; set; }

        [MaxLength(50)]
        public string StyleKey { get; set; } = string.Empty;

        [MaxLength(128)]
        public string DisplayName { get; set; } = string.Empty;

        public bool IsPremium { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? PremiumPrice { get; set; }
    }
}
