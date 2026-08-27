using System.Collections.Concurrent;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EBookDashboard.Controllers;

public partial class CheckoutController
{
    private static readonly ConcurrentDictionary<string, byte> PricingSessionGates = new(StringComparer.Ordinal);

    /// <summary>GET /checkout/{bookId} — the single paywall moment.</summary>
    [HttpGet("/checkout/{bookId:int}")]
    public async Task<IActionResult> BookBill(int bookId, string? format = null)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return RedirectToAction("UserLogin", "Account");

        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId.Value);
        if (book == null)
            return NotFound();

        var paperback = string.Equals(format, "paperback", StringComparison.OrdinalIgnoreCase);
        var hardcover = string.Equals(format, "hardcover", StringComparison.OrdinalIgnoreCase);
        ViewData["Title"] = "Checkout";
        ViewBag.BookTitle = book.Title;
        ViewBag.Paperback = paperback;
        ViewBag.Hardcover = hardcover;
        var writingRule = await _context.PricingRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Key == PricingKeys.WritingPerPage && r.IsActive);
        ViewBag.FreeAllowance = writingRule?.FreeAllowance ?? 0;
        try
        {
            var access = HttpContext.RequestServices.GetRequiredService<IExportAccessService>();
            var eval = await access.EvaluateAsync(userId.Value, bookId, paperback, hardcover);
            return View("BookBill", eval);
        }
        catch (Exception ex)
        {
            ViewBag.CheckoutError = ex.Message;
            return View("BookBill", new ExportAccessResult { BookId = bookId, CheckoutPath = $"/checkout/{bookId}" });
        }
    }

    /// <summary>POST /checkout/session</summary>
    [HttpPost("/checkout/session")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> CreatePricingSession([FromBody] PricingCheckoutSessionRequest? req)
    {
        var userId = HttpContext.Session.GetInt32("UserId");
        if (userId == null)
            return Unauthorized(new { success = false, message = "Please sign in and try Pay again." });
        if (req == null || req.BookId <= 0)
            return BadRequest(new { success = false, message = "BookId is required. Refresh this page and try again." });

        var lockKey = userId.Value + ":" + req.BookId;
        if (!PricingSessionGates.TryAdd(lockKey, 0))
            return Json(new { success = false, message = "Checkout is already starting. Wait for Stripe — do not click Pay again." });

        try
        {
            var access = HttpContext.RequestServices.GetRequiredService<IExportAccessService>();
            var orders = HttpContext.RequestServices.GetRequiredService<IOrderService>();
            var payments = HttpContext.RequestServices.GetRequiredService<IPaymentService>();

            var eval = await access.EvaluateAsync(userId.Value, req.BookId, req.Paperback, req.Hardcover);
            if (eval.Quote.Total <= 0)
                return Json(new { success = true, free = true, downloadUrl = "/Dashboard/Publish?bookId=" + req.BookId });

            var order = await orders.CreateFromQuoteAsync(userId.Value, req.BookId, eval.Quote, eval.PageCount);
            if (!string.IsNullOrWhiteSpace(order.StripeCheckoutSessionId))
            {
                var openUrl = await payments.TryGetOpenSessionUrlAsync(order.StripeCheckoutSessionId);
                if (!string.IsNullOrEmpty(openUrl))
                    return Json(new { success = true, url = openUrl, sessionId = order.StripeCheckoutSessionId });
            }

            var origin = ResolvePublicOrigin();
            var success = $"{origin}/checkout/paid?session_id={{CHECKOUT_SESSION_ID}}";
            var cancel = $"{origin}/checkout/abandoned?bookId={req.BookId}";
            var session = await payments.CreateSessionAsync(
                order.Id,
                eval.Quote.Total,
                eval.Quote.Currency,
                $"Book export — {eval.Quote.Total:0.00} {eval.Quote.Currency}",
                success,
                cancel);
            await orders.AttachCheckoutSessionAsync(order.Id, session.SessionId);
            return Json(new { success = true, url = session.Url, sessionId = session.SessionId });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message + " Check your connection, then try Pay again. If this keeps happening, open Support." });
        }
        finally
        {
            PricingSessionGates.TryRemove(lockKey, out _);
        }
    }

    [HttpGet("/checkout/paid")]
    public async Task<IActionResult> PricingSuccess(string? session_id)
    {
        ViewData["Title"] = "Payment complete";
        Order? order = null;
        if (!string.IsNullOrWhiteSpace(session_id))
        {
            var payments = HttpContext.RequestServices.GetRequiredService<IPaymentService>();
            var confirmed = await payments.ConfirmSessionAsync(session_id);
            if (confirmed.Paid)
            {
                var orders = HttpContext.RequestServices.GetRequiredService<IOrderService>();
                order = await orders.MarkPaidAsync(session_id, confirmed.PaymentIntentId);
            }
            order ??= await _context.Orders.AsNoTracking()
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.StripeCheckoutSessionId == session_id);
        }
        return View("PricingSuccess", order);
    }

    [HttpGet("/checkout/abandoned")]
    public IActionResult PricingCancel(int bookId)
    {
        ViewData["Title"] = "Checkout cancelled";
        ViewBag.BookId = bookId;
        return View("PricingCancel");
    }
}

public sealed class PricingCheckoutSessionRequest
{
    public int BookId { get; set; }
    public bool Paperback { get; set; }
    public bool Hardcover { get; set; }
}
