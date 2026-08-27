using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

public sealed class EntitlementService : IEntitlementService
{
    private readonly ApplicationDbContext _context;

    public EntitlementService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<bool> HasAsync(int userId, int? bookId, EntitlementType type, string? scopeKey = null, CancellationToken cancellationToken = default)
    {
        var q = _context.Entitlements.AsNoTracking()
            .Where(e => e.UserId == userId && e.Type == type);
        q = bookId.HasValue ? q.Where(e => e.BookId == bookId) : q.Where(e => e.BookId == null);
        if (!string.IsNullOrWhiteSpace(scopeKey))
            q = q.Where(e => e.ScopeKey == scopeKey);
        return await q.AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetOwnedKeysAsync(int userId, int? bookId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.Entitlements.AsNoTracking()
            .Where(e => e.UserId == userId && (bookId == null || e.BookId == bookId || e.BookId == null))
            .ToListAsync(cancellationToken);

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in rows)
        {
            switch (e.Type)
            {
                case EntitlementType.CustomCover:
                    keys.Add(PricingKeys.CoverCustomImage);
                    break;
                case EntitlementType.PremiumFormat:
                    keys.Add(PricingKeys.FormattingPremium);
                    break;
                case EntitlementType.PaperbackExport:
                    keys.Add(PricingKeys.ExportPaperback);
                    break;
                case EntitlementType.HardcoverExport:
                    keys.Add(PricingKeys.ExportHardcover);
                    break;
            }
        }
        return keys.ToList();
    }

    /// <inheritdoc />
    public async Task GrantFromOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var already = await _context.Entitlements.AnyAsync(e => e.OrderId == orderId, cancellationToken);
        if (already)
            return;

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order == null || order.Status != OrderStatus.Paid)
            return;

        var now = DateTime.UtcNow;
        foreach (var item in order.Items)
        {
            if (item.LineTotal <= 0 && item.PricingRuleKey != PricingKeys.WritingPerPage)
                continue;

            var type = MapType(item.PricingRuleKey);
            if (type == null)
                continue;

            if (type == EntitlementType.WritingPages && item.Quantity <= 0)
                continue;

            _context.Entitlements.Add(new Entitlement
            {
                UserId = order.UserId,
                BookId = order.BookId,
                Type = type.Value,
                ScopeKey = item.PricingRuleKey,
                Quantity = Math.Max(1, item.Quantity),
                OrderId = order.Id,
                CreatedAtUtc = now
            });

            if (type == EntitlementType.WritingPages && order.BookId.HasValue)
                await CoverBilledPagesAsync(order.BookId.Value, item, now, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task CoverBilledPagesAsync(int bookId, OrderItem item, DateTime now, CancellationToken cancellationToken)
    {
        var covered = item.Quantity;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(item.MetadataJson) ? "{}" : item.MetadataJson);
            if (doc.RootElement.TryGetProperty("pageCount", out var pc) && pc.TryGetInt32(out var n) && n > covered)
                covered = n;
        }
        catch (System.Text.Json.JsonException)
        {
        }

        var state = await _context.BookBillingStates.FirstOrDefaultAsync(s => s.BookId == bookId, cancellationToken);
        if (state == null)
        {
            state = new BookBillingState { BookId = bookId, BilledPageCount = 0 };
            _context.BookBillingStates.Add(state);
        }
        if (covered > state.BilledPageCount)
            state.BilledPageCount = covered;
        state.LastBilledAtUtc = now;
    }

    private static EntitlementType? MapType(string key) => key switch
    {
        PricingKeys.WritingPerPage => EntitlementType.WritingPages,
        PricingKeys.CoverCustomImage => EntitlementType.CustomCover,
        PricingKeys.FormattingPremium => EntitlementType.PremiumFormat,
        PricingKeys.ExportPaperback => EntitlementType.PaperbackExport,
        PricingKeys.ExportHardcover => EntitlementType.HardcoverExport,
        _ => null
    };
}
