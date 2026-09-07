using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Controllers;

[Authorize]
public class OrdersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IExportAccessService _access;
    private readonly ICurrentUserAccessor _currentUser;

    public OrdersController(
        ApplicationDbContext context,
        IExportAccessService access,
        ICurrentUserAccessor currentUser)
    {
        _context = context;
        _access = access;
        _currentUser = currentUser;
    }

    /// <summary>Lists open book bills and paid export receipts for the signed-in user.</summary>
    [HttpGet("/orders")]
    public async Task<IActionResult> Index()
    {
        var userId = _currentUser.GetUserId();
        if (userId == null || userId.Value <= 0)
            return RedirectToAction("UserLogin", "Account");

        var receipts = await _context.Orders.AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.UserId == userId.Value)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(100)
            .ToListAsync();

        var books = await _context.Books.AsNoTracking()
            .Where(b => (b.UserId == userId.Value || b.AuthorId == userId.Value) && b.Status != "Archived")
            .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
            .Select(b => new { b.BookId, b.Title, b.UpdatedAt, b.CreatedAt })
            .Take(40)
            .ToListAsync();

        var seen = new HashSet<int>();
        var openBills = new List<OpenBookBillRow>();
        foreach (var book in books)
        {
            if (!seen.Add(book.BookId))
                continue;
            var title = string.IsNullOrWhiteSpace(book.Title) ? "Untitled book" : book.Title.Trim();
            var updated = book.UpdatedAt.HasValue && book.UpdatedAt.Value.Year > 1990
                ? book.UpdatedAt.Value
                : book.CreatedAt;
            try
            {
                var eval = await _access.EvaluateAsync(userId.Value, book.BookId, paperback: false, hardcover: false);
                var quote = eval.Quote ?? new PriceQuote();
                openBills.Add(new OpenBookBillRow
                {
                    BookId = book.BookId,
                    Title = title,
                    PageCount = quote.PageCount > 0 ? quote.PageCount : eval.PageCount,
                    PaidPages = quote.PaidPages,
                    FreeAllowance = quote.FreeAllowance,
                    AmountDue = quote.Total,
                    Currency = string.IsNullOrWhiteSpace(quote.Currency) ? "USD" : quote.Currency,
                    UpdatedAt = updated
                });
            }
            catch
            {
                openBills.Add(new OpenBookBillRow
                {
                    BookId = book.BookId,
                    Title = title,
                    UpdatedAt = updated
                });
            }
        }

        ViewData["Title"] = "Orders";
        return View(new OrdersPageViewModel
        {
            OpenBills = openBills,
            Receipts = receipts
        });
    }
}
