using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers;

/// <summary>Anonymous live calculator used by the pricing page. Totals are server-side only.</summary>
[AllowAnonymous]
public sealed class PricingApiController : Controller
{
    private readonly PricingCalculatorService _calculator;

    public PricingApiController(PricingCalculatorService calculator)
    {
        _calculator = calculator;
    }

    [HttpPost("/api/pricing/calculate")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Calculate([FromBody] PricingInput? input)
    {
        var breakdown = await _calculator.GetFullBreakdown(input ?? new PricingInput());
        return Json(new
        {
            success = true,
            writingLabel = breakdown.WritingLabel,
            writingCost = breakdown.WritingCost,
            coverLabel = breakdown.CoverLabel,
            coverCost = breakdown.CoverCost,
            formattingLabel = breakdown.FormattingLabel,
            formattingCost = breakdown.FormattingCost,
            exportLabel = breakdown.ExportLabel,
            exportCost = breakdown.ExportCost,
            total = breakdown.Total,
            requiresPayment = breakdown.RequiresPayment,
            pageCount = breakdown.PageCount,
            freePages = breakdown.FreePages,
            paidPages = breakdown.PaidPages,
            currency = breakdown.Currency
        });
    }
}
