using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

/// <summary>
/// Server-side rate card for the public calculator. Delegates totals to <see cref="IPricingService"/>
/// so checkout and this page never diverge.
/// </summary>
public sealed class PricingCalculatorService
{
    private readonly ApplicationDbContext _context;
    private readonly IPricingService _pricing;

    public PricingCalculatorService(ApplicationDbContext context, IPricingService pricing)
    {
        _context = context;
        _pricing = pricing;
    }

    public async Task<decimal> CalculateWritingCost(int pageCount)
    {
        var quote = await QuoteAsync(pageCount, customCover: false, templateId: 0, exportType: "ebook");
        return LineTotal(quote, PricingKeys.WritingPerPage);
    }

    public async Task<decimal> CalculateCoverCost(bool isCustomUpload)
    {
        if (!isCustomUpload)
            return 0m;
        var quote = await QuoteAsync(0, customCover: true, templateId: 0, exportType: "ebook");
        return LineTotal(quote, PricingKeys.CoverCustomImage);
    }

    public async Task<decimal> CalculateFormattingCost(int templateId)
    {
        if (templateId <= 0)
            return 0m;
        var quote = await QuoteAsync(0, customCover: false, templateId: templateId, exportType: "ebook");
        return LineTotal(quote, PricingKeys.FormattingPremium);
    }

    public async Task<decimal> CalculateExportCost(string exportType)
    {
        var kind = NormalizeExport(exportType);
        if (kind == "ebook")
            return 0m;
        var quote = await QuoteAsync(0, customCover: false, templateId: 0, exportType: kind);
        return LineTotal(quote, PricingKeys.ExportPaperback);
    }

    public async Task<PricingBreakdown> GetFullBreakdown(PricingInput input)
    {
        input ??= new PricingInput();
        var pages = Math.Max(0, input.PageCount);
        var export = NormalizeExport(input.ExportType);
        var quote = await QuoteAsync(pages, input.IsCustomCover, input.TemplateId, export);

        var writing = quote.Lines.FirstOrDefault(l => l.Key == PricingKeys.WritingPerPage);
        var cover = quote.Lines.FirstOrDefault(l => l.Key == PricingKeys.CoverCustomImage);
        var formatting = quote.Lines.FirstOrDefault(l => l.Key == PricingKeys.FormattingPremium);
        var paperback = quote.Lines.FirstOrDefault(l => l.Key == PricingKeys.ExportPaperback);

        var coverLabel = input.IsCustomCover ? (cover?.Description ?? "Custom image cover") : "AI-generated cover";
        var fmtLabel = input.TemplateId > 0
            ? (formatting?.Description ?? "Premium formatting")
            : "Basic formatting";
        var exportLabel = export == "paperback"
            ? (paperback?.Description ?? "Paperback-ready file")
            : "eBook (PDF / EPUB)";

        return new PricingBreakdown
        {
            PageCount = quote.PageCount,
            FreePages = quote.FreeAllowance,
            PaidPages = quote.PaidPages,
            Currency = quote.Currency,
            WritingLabel = writing?.Breakdown ?? $"{pages} pages",
            WritingCost = writing?.LineTotal ?? 0m,
            CoverLabel = coverLabel,
            CoverCost = cover?.LineTotal ?? 0m,
            FormattingLabel = fmtLabel,
            FormattingCost = formatting?.LineTotal ?? 0m,
            ExportLabel = exportLabel,
            ExportCost = paperback?.LineTotal ?? 0m,
            Total = quote.Total
        };
    }

    private Task<PriceQuote> QuoteAsync(int pageCount, bool customCover, int templateId, string exportType)
    {
        return _pricing.BuildQuoteAsync(new PriceQuoteRequest
        {
            PageCount = pageCount,
            CustomCover = customCover,
            PremiumTemplateId = templateId > 0 ? templateId : null,
            Paperback = exportType == "paperback",
            Hardcover = false
        });
    }

    private static string NormalizeExport(string? exportType)
    {
        var t = (exportType ?? "ebook").Trim().ToLowerInvariant();
        if (t is "paperback" or "print" or "hardcover" or "hardback")
            return "paperback";
        return "ebook";
    }

    private static decimal LineTotal(PriceQuote quote, string key) =>
        quote.Lines.FirstOrDefault(l => l.Key == key)?.LineTotal ?? 0m;
}
