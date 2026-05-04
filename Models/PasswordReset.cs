using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models
{
    public class PasswordReset
    {
        [Key]
        public int ResetId { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string OTP { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime ExpiresAt { get; set; }

        public bool IsUsed { get; set; } = false;

        public string? Token { get; set; }

    }
}
