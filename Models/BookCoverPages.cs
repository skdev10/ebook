using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("bookcoverpages")]
    public class BookCoverPages
    {
        [Key]
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string BookCoverPagePath { get; set; } = string.Empty;
        public string Status { get; set; }
    }
}
