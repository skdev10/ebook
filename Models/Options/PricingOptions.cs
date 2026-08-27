namespace EBookDashboard.Models.Options;

/// <summary>Billing page-count configuration. PDF rendered pages are never used for pricing.</summary>
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    /// <summary>Word-count proxy for a billable page. Default 275.</summary>
    public int WordsPerPage { get; set; } = 275;
}
