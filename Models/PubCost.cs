using Microsoft.VisualBasic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    public class PubCost
    {
        [Required]
        [Key]
        public int CostId { get; set; }
        public int AuthorId { get; set; }
        [MaxLength(50)]
        public string AuthorCode { get; set; } = string.Empty;
        public int BookId { get; set; }
        public int PriceId { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal Amount { get; set; }
        public string Currency {  get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [MaxLength(12)]
        public string Status { get; set; } = string.Empty;

    }
}
