namespace EBookDashboard.Models;

/// <summary>Live estimate input for the public pricing calculator.</summary>
public sealed class PricingInput
{
    public int PageCount { get; set; }
    public bool IsCustomCover { get; set; }
    public int TemplateId { get; set; }
    public string? ExportType { get; set; }
}
