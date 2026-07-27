using System.ComponentModel.DataAnnotations;
using EBookDashboard.Configuration;

namespace EBookDashboard.Models;

public class BookCoverModel : IValidatableObject
{
    [Required(ErrorMessage = "Page count is required.")]
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

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var specs = KdpSpecsAccessor.Current;
        var min = specs.Paperback.MinPages;
        var max = specs.Paperback.MaxPages;
        if (Pages < min || Pages > max)
        {
            yield return new ValidationResult(
                $"Pages must be between {min} and {max}.",
                [nameof(Pages)]);
        }
    }
}
