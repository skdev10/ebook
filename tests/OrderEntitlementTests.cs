using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EBookDashboard.Tests;

public class OrderEntitlementTests
{
    private static ApplicationDbContext Ctx(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var ctx = new ApplicationDbContext(options);
        var updated = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);
        ctx.PricingRules.Add(new PricingRule
        {
            Id = 1, Key = PricingKeys.WritingPerPage, DisplayName = "AI writing",
            Unit = PricingUnit.PerPage, UnitAmount = 0.50m, FreeAllowance = 20,
            Currency = "USD", IsActive = true, UpdatedAtUtc = updated
        });
        ctx.SaveChanges();
        return ctx;
    }

    [Fact]
    public async Task MarkPaid_and_grant_are_idempotent()
    {
        await using var ctx = Ctx(nameof(MarkPaid_and_grant_are_idempotent));
        var entitlements = new EntitlementService(ctx);
        var orders = new OrderService(ctx, entitlements);

        var quote = new PriceQuote
        {
            Currency = "USD",
            Subtotal = 10m,
            Total = 10m,
            Lines =
            {
                new QuoteLine { Key = PricingKeys.CoverCustomImage, Description = "Cover", Quantity = 1, UnitAmount = 10m, LineTotal = 10m }
            }
        };
        var order = await orders.CreateFromQuoteAsync(7, 3, quote, pageCount: 40);
        await orders.AttachCheckoutSessionAsync(order.Id, "cs_test_1");

        var first = await orders.MarkPaidAsync("cs_test_1", "pi_1");
        var second = await orders.MarkPaidAsync("cs_test_1", "pi_1");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(OrderStatus.Paid, second!.Status);
        Assert.Equal(1, await ctx.Entitlements.CountAsync());
    }
}
