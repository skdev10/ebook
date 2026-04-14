using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Principal;

namespace EBookDashboard.Models
{
    public class BookSelectionsUser
    {
        [Key]
        public int SelectionId { get; set; }
        public int UserId { get; set; }
        public int BookId { get; set; }
        public int DesignId { get; set; }
        public DateTime? CreatedDate { get; set; } = DateTime.Now; 
    }
}
