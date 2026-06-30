using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models;

public class DeleteWriterChapterRequest
{
    [Required]
    public int BookId { get; set; }

    [Required]
    public int ChapterNumber { get; set; }
}
