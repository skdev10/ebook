using EBookDashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Controllers;

[Authorize]
public class OrdersController : Controller
{
    private readonly ApplicationDbContext _context;

    public OrdersController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("/orders")]
    public async Task<IActionResult> Index()
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("UserLogin", "Account");

        var orders = await _context.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.UserId == userId.Value)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(100)
            .ToListAsync();
        ViewData["Title"] = "Orders";
        return View(orders);
    }
}
