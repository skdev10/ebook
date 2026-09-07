namespace EBookDashboard.Models;

/// <summary>Receipt-style breakdown for the pricing panel. Total is always server-computed.</summary>
public sealed class PricingBreakdown
{
    public string WritingLabel { get; set; } = "";
    public decimal WritingCost { get; set; }
    public string CoverLabel { get; set; } = "";
    public decimal CoverCost { get; set; }
    public string FormattingLabel { get; set; } = "";
    public decimal FormattingCost { get; set; }
    public string ExportLabel { get; set; } = "";
    public decimal ExportCost { get; set; }
    public decimal Total { get; set; }
    public bool RequiresPayment => Total > 0;
    public int PageCount { get; set; }
    public int FreePages { get; set; }
    public int PaidPages { get; set; }
    public string Currency { get; set; } = "USD";
}
