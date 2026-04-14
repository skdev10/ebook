using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("useraudio")]
    public class UserAudio
    {
        public int AudioId { get; set; }
        public int UserId { get; set; }
        public int BookId { get; set; }
        public int ChapterId { get; set; }
        public string AudioFilePath { get; set; } = string.Empty;
        public string TranscribedText { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Status { get; set; } = string.Empty;
    }
}
