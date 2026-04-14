using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("authors")]
    public class Authors
    {
        [Required]
        [Key]
        public int AuthorId { get; set; }
        [Required]
        public string AuthorCode { get; set; } = null!; // 
        
        [Required]
        [MaxLength(250)]
        public string FullName { get; set; } = string.Empty;
        public string Compellation { get; set; } = string.Empty;
        public string AuthorEmail { get; set; } = string.Empty;
        public string Specialization { get; set; } = string.Empty;
        public string Qualification { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string Country { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public bool Newsletter { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public ICollection<Books> Books { get; set; } = new List<Books>();


    }

}
