using EBookDashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EBookDashboard.Controllers;

[AllowAnonymous]
public class PricingController : Controller
{
    private readonly ApplicationDbContext _context;

    public PricingController(ApplicationDbContext context)
    {
        _context = context;
    }

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

    [HttpGet("/pricing/rates")]
    public async Task<IActionResult> Rates()
    {
        var rules = await _context.PricingRules.AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => new { r.Key, r.DisplayName, r.UnitAmount, r.FreeAllowance, r.Currency })
            .ToListAsync();
        var words = HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<EBookDashboard.Models.Options.PricingOptions>>().Value.WordsPerPage;
        return Json(new { wordsPerPage = words > 0 ? words : 275, rules });
    }
}
