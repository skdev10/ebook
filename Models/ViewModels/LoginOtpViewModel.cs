using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models.ViewModels
{
    public class LoginOtpViewModel
    {
        [Required]
        [EmailAddress]
        public string UserEmail { get; set; } = string.Empty;

        [Required]
        [StringLength(6, MinimumLength = 6)]
        public string OtpCode { get; set; } = string.Empty;
    }
}
