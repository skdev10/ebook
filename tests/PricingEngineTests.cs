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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 0)]
    [InlineData(21, 0.50)]
    [InlineData(40, 10)]
    [InlineData(100, 40)]
    [InlineData(200, 90)]
    public async Task Writing_pages_follow_20_free_then_fifty_cents(int pages, decimal expected)
    {
        await using var ctx = CreateContext(nameof(Writing_pages_follow_20_free_then_fifty_cents) + pages);
        var quote = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = pages });
        Assert.Equal(expected, quote.Total);
        Assert.Equal(pages, quote.PageCount);
        Assert.Equal(20, quote.FreeAllowance);
        Assert.Equal(Math.Max(0, pages - 20), quote.PaidPages);
    }

    [Fact]
    public async Task Premium_template_defaults_are_3_4_and_5()
    {
        await using var ctx = CreateContext(nameof(Premium_template_defaults_are_3_4_and_5));
        ctx.FormattingTemplates.AddRange(
            new FormattingTemplate { Id = 10, StyleKey = "ElegantTrade", DisplayName = "Premium Formatting 1", IsPremium = true },
            new FormattingTemplate { Id = 9, StyleKey = "FineBook", DisplayName = "Premium Formatting 2", IsPremium = true },
            new FormattingTemplate { Id = 11, StyleKey = "ElegantTradePOD", DisplayName = "Premium Formatting 3", IsPremium = true });
        await ctx.SaveChangesAsync();
        var three = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = 10, PremiumTemplateId = 10 });
        var four = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = 10, PremiumTemplateId = 9 });
        var five = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = 10, PremiumTemplateId = 11 });
        Assert.Equal(3.00m, three.Total);
        Assert.Equal(4.00m, four.Total);
        Assert.Equal(5.00m, five.Total);
    }

    [Fact]
    public async Task Quote_200_custom_cover_premium5_hardcover_is_135()
    {
        await using var ctx = CreateContext(nameof(Quote_200_custom_cover_premium5_hardcover_is_135));
        ctx.FormattingTemplates.Add(new FormattingTemplate
        {
            Id = 7,
            StyleKey = "FineBook",
            DisplayName = "Premium Formatting 2",
            IsPremium = true,
            PremiumPrice = 5.00m
        });
        await ctx.SaveChangesAsync();
        var quote = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest
        {
            PageCount = 200,
            CustomCover = true,
            PremiumTemplateId = 7,
            Hardcover = true
        });
        Assert.Equal(135.00m, quote.Total);
        Assert.Equal(90.00m, quote.Lines.Single(l => l.Key == PricingKeys.WritingPerPage).LineTotal);
        Assert.Equal(10.00m, quote.Lines.Single(l => l.Key == PricingKeys.CoverCustomImage).LineTotal);
        Assert.Equal(5.00m, quote.Lines.Single(l => l.Key == PricingKeys.FormattingPremium).LineTotal);
        Assert.Equal(30.00m, quote.Lines.Single(l => l.Key == PricingKeys.ExportHardcover).LineTotal);
    }

    [Fact]
    public async Task Basic_ai_cover_and_basic_formatting_are_free()
    {
        await using var ctx = CreateContext(nameof(Basic_ai_cover_and_basic_formatting_are_free));
        var quote = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = 20 });
        Assert.Equal(0m, quote.Total);
        Assert.DoesNotContain(quote.Lines, l => l.Key == PricingKeys.CoverCustomImage);
        Assert.DoesNotContain(quote.Lines, l => l.Key == PricingKeys.FormattingPremium);
    }

    [Fact]
    public async Task Custom_cover_is_10_and_print_packages_match_catalog()
    {
        await using var ctx = CreateContext(nameof(Custom_cover_is_10_and_print_packages_match_catalog));
        var coverOnly = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = 10, CustomCover = true });
        Assert.Equal(10.00m, coverOnly.Total);
        var pb = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = 10, Paperback = true });
        Assert.Equal(20.00m, pb.Total);
        var hc = await Service(ctx).BuildQuoteAsync(new PriceQuoteRequest { PageCount = 10, Hardcover = true });
        Assert.Equal(30.00m, hc.Total);
    }

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
    public async Task PageCount_uses_saved_layout_pages_over_word_estimate()
    {
        await using var ctx = CreateContext(nameof(PageCount_uses_saved_layout_pages_over_word_estimate));
        ctx.Books.Add(new Books { BookId = 11, UserId = 1, Title = "Preview", WordCount = 200, Status = "Draft" });
        ctx.Settings.Add(new Settings
        {
            SettingId = 1,
            Key = "book:11:printReadyPageCount",
            Value = "23",
            Category = "Book"
        });
        await ctx.SaveChangesAsync();
        var pages = new PageCountService(ctx, Options.Create(new PricingOptions { WordsPerPage = 275 }));
        Assert.Equal(23, await pages.GetBillablePageCountAsync(11));
    }

    [Fact]
    public async Task PageCount_uses_merged_chapter_words_when_book_wordcount_is_stale()
    {
        await using var ctx = CreateContext(nameof(PageCount_uses_merged_chapter_words_when_book_wordcount_is_stale));
        ctx.Books.Add(new Books { BookId = 12, UserId = 1, Title = "Draft", WordCount = 1, Status = "Draft" });
        ctx.APIRawResponse.Add(new APIRawResponse
        {
            ResponseId = 1,
            Endpoint = "test",
            Chapter = 1,
            Title = "One",
            RequestData = "",
            ResponseData = "",
            BookId = 12,
            Content = string.Join(" ", Enumerable.Repeat("page", 550)),
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
        var pages = new PageCountService(ctx, Options.Create(new PricingOptions { WordsPerPage = 275 }));
        Assert.Equal(550, await pages.GetWordCountAsync(12));
        Assert.Equal(2, await pages.GetBillablePageCountAsync(12));
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

    [Fact]
    public async Task Calculator_20_pages_ai_cover_basic_ebook_is_free()
    {
        await using var ctx = CreateContext(nameof(Calculator_20_pages_ai_cover_basic_ebook_is_free));
        var calc = new PricingCalculatorService(ctx, Service(ctx));
        var bill = await calc.GetFullBreakdown(new PricingInput
        {
            PageCount = 20,
            IsCustomCover = false,
            TemplateId = 0,
            ExportType = "ebook"
        });
        Assert.Equal(0m, bill.Total);
        Assert.False(bill.RequiresPayment);
        Assert.Equal(0m, bill.WritingCost);
        Assert.Equal(0m, bill.CoverCost);
        Assert.Equal(0m, bill.FormattingCost);
        Assert.Equal(0m, bill.ExportCost);
    }

    [Fact]
    public async Task Calculator_200_custom_cover_premium5_hardcover_maps_to_paperback()
    {
        await using var ctx = CreateContext(nameof(Calculator_200_custom_cover_premium5_hardcover_maps_to_paperback));
        ctx.FormattingTemplates.Add(new FormattingTemplate
        {
            Id = 7,
            StyleKey = "ElegantTradePOD",
            DisplayName = "Premium Formatting 3",
            IsPremium = true,
            PremiumPrice = 5.00m
        });
        await ctx.SaveChangesAsync();
        var calc = new PricingCalculatorService(ctx, Service(ctx));
        var bill = await calc.GetFullBreakdown(new PricingInput
        {
            PageCount = 200,
            IsCustomCover = true,
            TemplateId = 7,
            ExportType = "hardcover"
        });
        Assert.Equal(90.00m, bill.WritingCost);
        Assert.Equal(10.00m, bill.CoverCost);
        Assert.Equal(5.00m, bill.FormattingCost);
        Assert.Equal(20.00m, bill.ExportCost);
        Assert.Equal(125.00m, bill.Total);
        Assert.True(bill.RequiresPayment);
    }
}
