namespace EBookDashboard.Models;

public sealed class PriceQuoteRequest
{
    public int PageCount { get; set; }
    public int AlreadyBilledPageCount { get; set; }
    public bool CustomCover { get; set; }
    public int? PremiumTemplateId { get; set; }
    public bool Paperback { get; set; }
    public bool Hardcover { get; set; }
    public IReadOnlyCollection<string> OwnedKeys { get; set; } = Array.Empty<string>();
}

public sealed class QuoteLine
{
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitAmount { get; set; }
    public decimal LineTotal { get; set; }
    public bool AlreadyOwned { get; set; }

    /// <summary>Human-readable arithmetic shown under the line, e.g. <c>200 pages − 20 free = 180 billable × $0.50</c>.</summary>
    public string? Breakdown { get; set; }
}

public sealed class PriceQuote
{
    public List<QuoteLine> Lines { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
    public int PageCount { get; set; }
    public int FreeAllowance { get; set; }
    public int PaidPages { get; set; }
}
