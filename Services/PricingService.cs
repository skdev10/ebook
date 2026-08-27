using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

public sealed class PricingService : IPricingService
{
    private readonly ApplicationDbContext _context;

    public PricingService(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<PriceQuote> BuildQuoteAsync(PriceQuoteRequest request)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var rules = await _context.PricingRules.AsNoTracking()
            .Where(r => r.IsActive)
            .ToListAsync();
        var byKey = rules.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        var owned = new HashSet<string>(request.OwnedKeys ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        var writing = Require(byKey, PricingKeys.WritingPerPage);
        var quote = new PriceQuote { Currency = string.IsNullOrWhiteSpace(writing.Currency) ? "USD" : writing.Currency };

        var billedFloor = Math.Max(writing.FreeAllowance, Math.Max(0, request.AlreadyBilledPageCount));
        var billablePages = Math.Max(0, request.PageCount - billedFloor);
        quote.Lines.Add(BuildLine(
            writing,
            billablePages,
            owned,
            $"{writing.DisplayName} ({request.PageCount} pages − {billedFloor} free/billed = {billablePages} billable × {writing.UnitAmount:0.00})"));

        if (request.CustomCover)
        {
            var cover = Require(byKey, PricingKeys.CoverCustomImage);
            quote.Lines.Add(BuildLine(cover, 1, owned, cover.DisplayName));
        }

        if (request.PremiumTemplateId.HasValue)
        {
            var formatting = Require(byKey, PricingKeys.FormattingPremium);
            var unit = formatting.UnitAmount;
            var label = formatting.DisplayName;
            var template = await _context.FormattingTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == request.PremiumTemplateId.Value);
            if (template != null)
            {
                if (template.PremiumPrice.HasValue)
                    unit = template.PremiumPrice.Value;
                if (!string.IsNullOrWhiteSpace(template.DisplayName))
                    label = template.DisplayName;
            }
            var line = BuildLine(formatting, 1, owned, label);
            line.UnitAmount = RoundMoney(unit);
            line.LineTotal = line.AlreadyOwned ? 0m : RoundMoney(line.UnitAmount * line.Quantity);
            quote.Lines.Add(line);
        }

        if (request.Paperback)
        {
            var pb = Require(byKey, PricingKeys.ExportPaperback);
            quote.Lines.Add(BuildLine(pb, 1, owned, pb.DisplayName));
        }

        if (request.Hardcover)
        {
            var hc = Require(byKey, PricingKeys.ExportHardcover);
            quote.Lines.Add(BuildLine(hc, 1, owned, hc.DisplayName));
        }

        quote.Lines.Add(new QuoteLine
        {
            Key = PricingKeys.ExportEbook,
            Description = "eBook (EPUB/PDF)",
            Quantity = 1,
            UnitAmount = 0m,
            LineTotal = 0m,
            AlreadyOwned = owned.Contains(PricingKeys.ExportEbook)
        });

        quote.Subtotal = RoundMoney(quote.Lines.Sum(l => l.LineTotal));
        quote.Total = quote.Subtotal;
        return quote;
    }

    private static PricingRule Require(IReadOnlyDictionary<string, PricingRule> byKey, string key)
    {
        if (!byKey.TryGetValue(key, out var rule) || rule == null || !rule.IsActive)
            throw new InvalidOperationException($"Pricing rule '{key}' is missing or inactive. Restore it in Admin before quoting.");
        return rule;
    }

    private static QuoteLine BuildLine(PricingRule rule, int quantity, HashSet<string> owned, string description)
    {
        var alreadyOwned = owned.Contains(rule.Key);
        var unit = RoundMoney(rule.UnitAmount);
        var total = alreadyOwned ? 0m : RoundMoney(unit * quantity);
        return new QuoteLine
        {
            Key = rule.Key,
            Description = description,
            Quantity = quantity,
            UnitAmount = unit,
            LineTotal = total,
            AlreadyOwned = alreadyOwned
        };
    }

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
