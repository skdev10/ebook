using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

public sealed class ExportAccessService : IExportAccessService
{
    private readonly ApplicationDbContext _context;
    private readonly IPageCountService _pages;
    private readonly IPricingService _pricing;
    private readonly IEntitlementService _entitlements;

    public ExportAccessService(
        ApplicationDbContext context,
        IPageCountService pages,
        IPricingService pricing,
        IEntitlementService entitlements)
    {
        _context = context;
        _pages = pages;
        _pricing = pricing;
        _entitlements = entitlements;
    }

    /// <inheritdoc />
    public async Task<PriceQuote> QuoteBookAsync(int userId, int bookId, bool paperback, bool hardcover, CancellationToken cancellationToken = default)
    {
        var pageCount = await _pages.GetBillablePageCountAsync(bookId);
        var billed = await _context.BookBillingStates.AsNoTracking()
            .Where(s => s.BookId == bookId)
            .Select(s => s.BilledPageCount)
            .FirstOrDefaultAsync(cancellationToken);
        var owned = await _entitlements.GetOwnedKeysAsync(userId, bookId, cancellationToken);

        var cover = await _context.BookCoverDesigns.AsNoTracking()
            .Where(c => c.BookId == bookId && !c.IsDeleted)
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var customCover = cover?.CoverSource == CoverSource.UserUpload;

        int? premiumId = null;
        var fmt = await _context.BookFormatting.AsNoTracking()
            .FirstOrDefaultAsync(f => f.BookId == bookId, cancellationToken);
        if (fmt != null)
        {
            var style = InteriorExportTheme.NormalizeInteriorStyle(fmt.InteriorStyle);
            var template = await _context.FormattingTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.StyleKey == style, cancellationToken);
            if (template != null && template.IsPremium)
                premiumId = template.Id;
        }

        return await _pricing.BuildQuoteAsync(new PriceQuoteRequest
        {
            PageCount = pageCount,
            AlreadyBilledPageCount = billed,
            CustomCover = customCover,
            PremiumTemplateId = premiumId,
            Paperback = paperback,
            Hardcover = hardcover,
            OwnedKeys = owned
        });
    }

    /// <inheritdoc />
    public async Task<ExportAccessResult> EvaluateAsync(int userId, int bookId, bool paperback, bool hardcover, CancellationToken cancellationToken = default)
    {
        var pageCount = await _pages.GetBillablePageCountAsync(bookId);
        var quote = await QuoteBookAsync(userId, bookId, paperback, hardcover, cancellationToken);
        var checkout = $"/checkout/{bookId}";
        if (paperback) checkout += "?format=paperback";
        else if (hardcover) checkout += "?format=hardcover";

        return new ExportAccessResult
        {
            Allowed = quote.Total <= 0m,
            Quote = quote,
            PageCount = pageCount,
            BookId = bookId,
            CheckoutPath = checkout
        };
    }

    /// <inheritdoc />
    public async Task<byte[]> PreviewCoverBytesAsync(int userId, int bookId, byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (bytes == null || bytes.Length == 0)
            return bytes ?? Array.Empty<byte>();

        var cover = await _context.BookCoverDesigns.AsNoTracking()
            .Where(c => c.BookId == bookId && !c.IsDeleted)
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (cover == null || cover.CoverSource != CoverSource.UserUpload)
            return bytes;

        if (await _entitlements.HasAsync(userId, bookId, EntitlementType.CustomCover, cancellationToken: cancellationToken))
            return bytes;

        return CoverPreviewWatermark.Apply(bytes);
    }
}
