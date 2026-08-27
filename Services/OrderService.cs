using System.Text.Json;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

public sealed class OrderService : IOrderService
{
    private readonly ApplicationDbContext _context;
    private readonly IEntitlementService _entitlements;

    public OrderService(ApplicationDbContext context, IEntitlementService entitlements)
    {
        _context = context;
        _entitlements = entitlements;
    }

    /// <inheritdoc />
    public async Task<Order> CreateFromQuoteAsync(int userId, int? bookId, PriceQuote quote, int? pageCount = null, CancellationToken cancellationToken = default)
    {
        if (quote == null)
            throw new ArgumentNullException(nameof(quote));

        var order = new Order
        {
            UserId = userId,
            BookId = bookId,
            Status = quote.Total > 0 ? OrderStatus.PendingPayment : OrderStatus.Paid,
            Subtotal = quote.Subtotal,
            Discount = 0m,
            Total = quote.Total,
            Currency = string.IsNullOrWhiteSpace(quote.Currency) ? "USD" : quote.Currency,
            CreatedAtUtc = DateTime.UtcNow,
            PaidAtUtc = quote.Total > 0 ? null : DateTime.UtcNow
        };

        foreach (var line in quote.Lines)
        {
            var meta = line.Key == PricingKeys.WritingPerPage && pageCount.HasValue
                ? $"{{\"pageCount\":{pageCount.Value}}}"
                : "{}";
            order.Items.Add(new OrderItem
            {
                PricingRuleKey = line.Key,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitAmount = line.UnitAmount,
                LineTotal = line.LineTotal,
                MetadataJson = meta
            });
        }

        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        if (order.Status == OrderStatus.Paid)
            await _entitlements.GrantFromOrderAsync(order.Id, cancellationToken);

        return order;
    }

    /// <inheritdoc />
    public async Task AttachCheckoutSessionAsync(int orderId, string sessionId, CancellationToken cancellationToken = default)
    {
        var order = await _context.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order == null)
            throw new InvalidOperationException($"Order {orderId} was not found.");
        order.StripeCheckoutSessionId = sessionId;
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Order?> MarkPaidAsync(string sessionId, string paymentIntentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.StripeCheckoutSessionId == sessionId, cancellationToken);
        if (order == null)
            return null;

        if (order.Status == OrderStatus.Paid)
        {
            if (!string.IsNullOrWhiteSpace(paymentIntentId) && string.IsNullOrWhiteSpace(order.StripePaymentIntentId))
            {
                order.StripePaymentIntentId = paymentIntentId;
                await _context.SaveChangesAsync(cancellationToken);
            }
            await _entitlements.GrantFromOrderAsync(order.Id, cancellationToken);
            return order;
        }

        order.Status = OrderStatus.Paid;
        order.PaidAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(paymentIntentId))
            order.StripePaymentIntentId = paymentIntentId;
        await _context.SaveChangesAsync(cancellationToken);
        await _entitlements.GrantFromOrderAsync(order.Id, cancellationToken);
        return order;
    }
}
