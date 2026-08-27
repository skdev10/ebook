using EBookDashboard.Models;
using EBookDashboard.Models.Options;
using EBookDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace EBookDashboard.Tests;

public class PricingEngineTests
{
    private static ApplicationDbContext CreateContext(string name)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        var ctx = new ApplicationDbContext(options);
        var updated = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);
        ctx.PricingRules.AddRange(
            new PricingRule { Id = 1, Key = PricingKeys.WritingPerPage, DisplayName = "AI writing", Unit = PricingUnit.PerPage, UnitAmount = 0.50m, FreeAllowance = 20, Currency = "USD", IsActive = true, UpdatedAtUtc = updated },
            new PricingRule { Id = 2, Key = PricingKeys.CoverCustomImage, DisplayName = "Custom image cover", Unit = PricingUnit.PerItem, UnitAmount = 10.00m, FreeAllowance = 0, Currency = "USD", IsActive = true, UpdatedAtUtc = updated },
            new PricingRule { Id = 3, Key = PricingKeys.FormattingPremium, DisplayName = "Premium formatting", Unit = PricingUnit.PerItem, UnitAmount = 3.00m, FreeAllowance = 0, Currency = "USD", IsActive = true, UpdatedAtUtc = updated },
            new PricingRule { Id = 4, Key = PricingKeys.ExportPaperback, DisplayName = "Paperback package", Unit = PricingUnit.Flat, UnitAmount = 20.00m, FreeAllowance = 0, Currency = "USD", IsActive = true, UpdatedAtUtc = updated },
            new PricingRule { Id = 5, Key = PricingKeys.ExportHardcover, DisplayName = "Hardcover package", Unit = PricingUnit.Flat, UnitAmount = 30.00m, FreeAllowance = 0, Currency = "USD", IsActive = true, UpdatedAtUtc = updated });
        ctx.SaveChanges();
        return ctx;
    }

    private static PricingService Service(ApplicationDbContext ctx) => new(ctx);

    [Fact]
    public async Task Quote_200_pages_custom_cover_premium_paperback_is_123()
    {
        await using var ctx = CreateContext(nameof(Quote_200_pages_custom_cover_premium_paperback_is_123));
        var quote = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest
        {
            PageCount = 200,
            CustomCover = true,
            PremiumTemplateId = 1,
            Paperback = true
        });
        Assert.Equal(123.00m, quote.Total);
        Assert.Equal("USD", quote.Currency);
        Assert.Contains(quote.Lines, l => l.Key == PricingKeys.ExportEbook && l.LineTotal == 0m);
    }

    [Fact]
    public async Task Quote_15_pages_free_cover_free_template_ebook_is_zero()
    {
        await using var ctx = CreateContext(nameof(Quote_15_pages_free_cover_free_template_ebook_is_zero));
        var quote = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest
        {
            PageCount = 15
        });
        Assert.Equal(0.00m, quote.Total);
        Assert.All(quote.Lines, l => Assert.Equal(0m, l.LineTotal));
    }

    [Fact]
    public async Task Quote_already_billed_200_grown_to_240_is_20()
    {
        await using var ctx = CreateContext(nameof(Quote_already_billed_200_grown_to_240_is_20));
        var quote = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest
        {
            PageCount = 240,
            AlreadyBilledPageCount = 200
        });
        Assert.Equal(20.00m, quote.Total);
        var writing = Assert.Single(quote.Lines, l => l.Key == PricingKeys.WritingPerPage);
        Assert.Equal(40, writing.Quantity);
        Assert.Equal(20.00m, writing.LineTotal);
    }

    [Fact]
    public async Task Quote_already_billed_200_shrunk_to_180_is_zero()
    {
        await using var ctx = CreateContext(nameof(Quote_already_billed_200_shrunk_to_180_is_zero));
        var quote = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest
        {
            PageCount = 180,
            AlreadyBilledPageCount = 200
        });
        Assert.Equal(0.00m, quote.Total);
        var writing = Assert.Single(quote.Lines, l => l.Key == PricingKeys.WritingPerPage);
        Assert.Equal(0, writing.Quantity);
    }

    [Fact]
    public async Task PageCount_54930_words_is_200_pages()
    {
        await using var ctx = CreateContext(nameof(PageCount_54930_words_is_200_pages));
        ctx.Books.Add(new Books { BookId = 10, UserId = 1, Title = "Long", WordCount = 54930, Status = "Draft" });
        await ctx.SaveChangesAsync();
        var pages = new PageCountService(ctx, Options.Create(new PricingOptions { WordsPerPage = 275 }));
        Assert.Equal(200, await pages.GetBillablePageCountAsync(10));
        Assert.Equal(54930, await pages.GetWordCountAsync(10));
    }

    [Fact]
    public async Task Owned_custom_cover_line_present_with_zero_total()
    {
        await using var ctx = CreateContext(nameof(Owned_custom_cover_line_present_with_zero_total));
        var quote = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest
        {
            PageCount = 20,
            CustomCover = true,
            OwnedKeys = new[] { PricingKeys.CoverCustomImage }
        });
        var cover = Assert.Single(quote.Lines, l => l.Key == PricingKeys.CoverCustomImage);
        Assert.True(cover.AlreadyOwned);
        Assert.Equal(0m, cover.LineTotal);
        Assert.Equal(10.00m, cover.UnitAmount);
    }

    [Fact]
    public async Task Missing_rule_throws()
    {
        await using var ctx = CreateContext(nameof(Missing_rule_throws));
        var rule = await ctx.PricingRules.FirstAsync(r => r.Key == PricingKeys.WritingPerPage);
        ctx.PricingRules.Remove(rule);
        await ctx.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = 50 }));
    }
}
