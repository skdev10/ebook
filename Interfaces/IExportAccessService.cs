using EBookDashboard.Models;

namespace EBookDashboard.Interfaces;

public sealed class ExportAccessResult
{
    public bool Allowed { get; set; }
    public PriceQuote Quote { get; set; } = new();
    public int PageCount { get; set; }
    public int BookId { get; set; }
    public string CheckoutPath { get; set; } = "";
}

public interface IExportAccessService
{
    Task<ExportAccessResult> EvaluateAsync(int userId, int bookId, bool paperback, bool hardcover, CancellationToken cancellationToken = default);

    Task<PriceQuote> QuoteBookAsync(int userId, int bookId, bool paperback, bool hardcover, CancellationToken cancellationToken = default);

    /// <summary>Watermarks a custom-upload cover when the book is not entitled. AI and text covers pass through.</summary>
    Task<byte[]> PreviewCoverBytesAsync(int userId, int bookId, byte[] bytes, CancellationToken cancellationToken = default);
}
