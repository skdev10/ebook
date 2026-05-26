using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models;

public class BookCoverModel
{
    [Required(ErrorMessage = "Page count is required.")]
    [Range(24, 828, ErrorMessage = "Pages must be between 24 and 828.")]
    public int Pages { get; set; } = 150;

    [Required(ErrorMessage = "Paper type is required.")]
    public string PaperType { get; set; } = "White paper";

    [Required(ErrorMessage = "Book title is required.")]
    [MaxLength(80, ErrorMessage = "Title cannot exceed 80 characters.")]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Author name is required.")]
    [MaxLength(60, ErrorMessage = "Author name cannot exceed 60 characters.")]
    public string Author { get; set; } = "";

    [MaxLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string? Description { get; set; }
}
