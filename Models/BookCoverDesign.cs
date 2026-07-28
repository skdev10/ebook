using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models;

/// <summary>
/// One cover project linked to an interior book (Spec 11.1 / 11.4).
/// A book may have Ebook, Paperback, and Hardcover cover rows; soft-delete supported.
/// </summary>
[Table("book_cover_designs")]
public class BookCoverDesign
{
    [Key]
    [Column("CoverId")]
    public int CoverId { get; set; }

    public int BookId { get; set; }

    public int UserId { get; set; }

    /// <summary>Ebook | Paperback | Hardcover</summary>
    [MaxLength(40)]
    public string CoverType { get; set; } = "Ebook";

    [MaxLength(120)]
    public string DisplayName { get; set; } = string.Empty;

    public double? TrimWidthIn { get; set; }
    public double? TrimHeightIn { get; set; }
    public double? SpineWidthIn { get; set; }
    public double? BleedIn { get; set; }
    public double? FullWidthIn { get; set; }
    public double? FullHeightIn { get; set; }

    /// <summary>Draft | Ready | Generating | Failed</summary>
    [MaxLength(40)]
    public string Status { get; set; } = "Draft";

    [MaxLength(1000)]
    public string? FrontImagePath { get; set; }

    [MaxLength(1000)]
    public string? BackImagePath { get; set; }

    [MaxLength(1000)]
    public string? SpineImagePath { get; set; }

    [MaxLength(1000)]
    public string? WrapImagePath { get; set; }

    /// <summary>Currently selected cover project for the Cover Design workspace.</summary>
    public bool IsActive { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
