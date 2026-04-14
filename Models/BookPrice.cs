using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("bookprice")]
    public class BookPrice
    {
        [Required]
        [Key]
        public int PriceId { get; set; }
        public int BookVersionId { get; set; }
        public int BookId { get; set; }
        [Required]
        public int AuthorId { get; set; }  // FK to AspNetUsers
        [Required]
        public string AuthorCode { get; set; } = null!; // FK to AspNetUsers
        [Column(TypeName = "decimal(10,2)")]
        public decimal bookPrice { get; set; } = 0;
        public string Currency { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = string.Empty;
    }

}
