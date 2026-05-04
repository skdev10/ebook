using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models
{
    public class Settings
    {
        [Key]
        public int SettingId { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Key { get; set; } = string.Empty;

        /// <summary>Stored as MySQL LONGTEXT — run ALTER TABLE if the column is still VARCHAR.</summary>
        public string? Value { get; set; }
        
        [MaxLength(50)]
        public string Category { get; set; } = "General"; // General, Payment, Email, Security
        
        [MaxLength(200)]
        public string? Description { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Legacy short settings (non-LONGTEXT keys).</summary>
        public const int MaxShortValueLength = 1000;

        /// <summary>
        /// MySQL <c>Settings.Value</c> is often still VARCHAR(1000). All writes are clamped to this until you run:
        /// <c>ALTER TABLE `Settings` MODIFY COLUMN `Value` LONGTEXT NULL;</c>
        /// </summary>
        public const int DbCompatMaxValueLength = 1000;

        /// <summary>AI cover direction / prompt stored in Settings (generous limit).</summary>
        public const int MaxPromptLength = 16000;

        /// <summary>Safety cap for LONGTEXT payloads (~4M chars).</summary>
        public const int MaxLongTextChars = 4_000_000;

        /// <summary>Clamp text to a maximum length (avoids oversized payloads).</summary>
        public static string? ClampValueLength(string? value, int maxLen = MaxShortValueLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLen ? value : value.Substring(0, maxLen);
        }
    }
}

