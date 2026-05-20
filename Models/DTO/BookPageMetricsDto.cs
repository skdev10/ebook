namespace EBookDashboard.Models.DTO;

/// <summary>
/// Shared manuscript pagination metrics used by PDF export, print-ready cover sizing,
/// and Stripe price calculation.
/// </summary>
public sealed class BookPageMetricsDto
{
    public int PageCount { get; set; }
    public int WordCount { get; set; }
    public int ChapterCount { get; set; }
    public int ImageCount { get; set; }
    public string Basis { get; set; } = "estimated_from_saved_manuscript";
}
