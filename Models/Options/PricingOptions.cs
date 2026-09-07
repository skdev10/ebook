namespace EBookDashboard.Models.Options;

/// <summary>Billing page-count configuration. Layout pages from the book preview take priority when saved.</summary>
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    /// <summary>Word-count proxy for a billable page. Default 275.</summary>
    public int WordsPerPage { get; set; } = 275;
}
