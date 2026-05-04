using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("finalizechapterrequest")]
    public class FinalizeChapterRequest
    {
        [Key]
        public int Id { get; set; }
        public int ResponseId { get; set; }
        public int UserId { get; set; }
        public int BookId { get; set; }
        public int Chapter { get; set; }
        public string? StatusCode { get; set; } = string.Empty;
        public DateTime? CreatedAt { get; set; } = DateTime.Now;
    }
}
