using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EBookDashboard.Controllers;

[Authorize]
public class PricingController : Controller
{
    private readonly ApplicationDbContext _context;

    public PricingController(ApplicationDbContext context)
    {
        _context = context;
    }

    [AllowAnonymous]
    [HttpGet("/pricing")]
    public async Task<IActionResult> Index()
    {
        var rules = await _context.PricingRules.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Id)
            .ToListAsync();
        ViewData["Title"] = "Pricing";
        return View(rules);
    }

    [AllowAnonymous]
    [HttpGet("/pricing/rates")]
    public async Task<IActionResult> Rates()
    {
        var rules = await _context.PricingRules.AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => new { r.Key, r.DisplayName, r.UnitAmount, r.FreeAllowance, r.Currency })
            .ToListAsync();
        var words = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<EBookDashboard.Models.Options.PricingOptions>>().Value.WordsPerPage;
        if (words <= 0)
            return StatusCode(500, new { message = "Pricing:WordsPerPage must be a positive integer in configuration." });
        var premiumFallback = rules.FirstOrDefault(r => r.Key == PricingKeys.FormattingPremium)?.UnitAmount ?? 3.00m;
        var templates = await _context.FormattingTemplates.AsNoTracking()
            .OrderBy(t => t.Id)
            .ToListAsync();
        return Json(new
        {
            wordsPerPage = words,
            rules,
            templates = templates.Select(t => new
            {
                t.Id,
                t.StyleKey,
                t.DisplayName,
                t.IsPremium,
                unitAmount = t.IsPremium ? PricingService.ResolvePremiumTemplatePrice(t, premiumFallback) : 0m
            })
        });
    }

    /// <summary>Authoritative live bill for a book. Frontend may estimate; this is the source of truth.</summary>
    [Authorize]
    [HttpGet("/pricing/quote/{bookId:int}")]
    public async Task<IActionResult> Quote(int bookId, bool paperback = false, bool hardcover = false)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return Unauthorized(new { success = false, message = "Please sign in." });
        if (bookId <= 0)
            return BadRequest(new { success = false, message = "BookId is required." });

        var owns = await _context.Books.AsNoTracking()
            .AnyAsync(b => b.BookId == bookId && b.UserId == userId.Value);
        if (!owns)
            return NotFound(new { success = false, message = "Book not found." });

        var access = HttpContext.RequestServices.GetRequiredService<IExportAccessService>();
        var eval = await access.EvaluateAsync(userId.Value, bookId, paperback, hardcover);
        var writing = eval.Quote.Lines.FirstOrDefault(l => l.Key == PricingKeys.WritingPerPage);
        return Json(new
        {
            success = true,
            bookId,
            pageCount = eval.Quote.PageCount,
            freePages = eval.Quote.FreeAllowance,
            paidPages = eval.Quote.PaidPages,
            writingCost = writing?.LineTotal ?? 0m,
            writingUnit = writing?.UnitAmount ?? 0m,
            total = eval.Quote.Total,
            currency = eval.Quote.Currency,
            allowed = eval.Allowed,
            checkoutPath = eval.CheckoutPath,
            lines = eval.Quote.Lines.Select(l => new
            {
                l.Key,
                l.Description,
                l.Quantity,
                l.UnitAmount,
                l.LineTotal,
                l.AlreadyOwned,
                l.Breakdown
            })
        });
    }
}
