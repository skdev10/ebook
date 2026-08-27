using EBookDashboard.Models;

namespace EBookDashboard.Interfaces;

public interface IOrderService
{
    Task<Order> CreateFromQuoteAsync(int userId, int? bookId, PriceQuote quote, int? pageCount = null, CancellationToken cancellationToken = default);

    /// <summary>Idempotent. Safe under Stripe webhook retries.</summary>
    Task<Order?> MarkPaidAsync(string sessionId, string paymentIntentId, CancellationToken cancellationToken = default);

    Task AttachCheckoutSessionAsync(int orderId, string sessionId, CancellationToken cancellationToken = default);
}

public interface IEntitlementService
{
    Task<bool> HasAsync(int userId, int? bookId, EntitlementType type, string? scopeKey = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetOwnedKeysAsync(int userId, int? bookId, CancellationToken cancellationToken = default);

    /// <summary>Idempotent. Safe to call twice for the same paid order.</summary>
    Task GrantFromOrderAsync(int orderId, CancellationToken cancellationToken = default);
}
