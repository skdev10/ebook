using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("forgotpassword")]
    public class ForgotPassword
    {
        [Required]
        public string UserEmail { get; set; } = string.Empty;
    }
}
