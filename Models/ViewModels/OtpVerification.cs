using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models.ViewModels
{
    public class OtpVerification
    {
        public int Id { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(6)]
        public string OtpCode { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; }

        [StringLength(50)]
        public string Purpose { get; set; } = string.Empty; // "Login", "Registration", etc.
    }
}
