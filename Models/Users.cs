using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;

namespace EBookDashboard.Models
{
    [Table("users")]
    public class Users
    {
        [Required]
        [Key]
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;
        public string SecretQuestion {  get; set; } = string.Empty;
        public string SecretQuestionAnswer { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastLoginAt { get; set; } = new DateTime(1980, 1, 1);
        public int RoleId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ProfilePicturePath { get; set; } // Path to profile picture file
        public Roles? Role { get; set; } // navigation property
        public string AuthorCode { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }

        /// <summary>First-login onboarding. Same meaning as the prompt's HasSeenTour.</summary>
        public bool? HasCompletedTour { get; set; }

        /// <summary>Lifetime AI cover generations consumed.</summary>
        public int AICoverGenerationsUsed { get; set; }

        /// <summary>Lifetime free AI cover cap. Default 5. Admin may raise this per user.</summary>
        public int AICoverGenerationLimit { get; set; } = 5;
    }
}
