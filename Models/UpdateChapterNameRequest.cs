using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models
{
    public class UpdateChapterNameRequest
    {
        [Required]
        public int BookId { get; set; }

        [Required]
        public int ChapterNumber { get; set; }

        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;
    }
}
