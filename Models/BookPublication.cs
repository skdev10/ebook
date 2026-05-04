using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("bookpublication")]
    public class BookPublication
    {
        [Key]
        public int PublicationId { get; set; }
        public int BookId { get; set; }
        public int AuthorId { get; set; }
        public int UserId { get; set; }
        public string PublisherName { get; set; }= string.Empty;
        public DateTime PublicationDate { get; set; }
        public string? ISBN { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty; // e.g., eBook, Paperback, Hardcover
        public decimal Price { get; set; }= 0.0m;
        public string Description { get; set; } = string.Empty;
    }
}
