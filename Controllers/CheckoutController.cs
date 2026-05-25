using System.Security.Claims;
using EBookDashboard.Infrastructure;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.Options;
using EBookDashboard.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace EBookDashboard.Controllers
{
    [Route("[controller]/[action]")]
    [Authorize]
    public class CheckoutController : Controller
    {
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;
        private readonly IOptionsSnapshot<BookPaymentOptions> _bookPaymentOptions;
        private readonly IBookService _bookService;
        private readonly IBookPageMetricsService _bookPageMetricsService;
        private readonly ILogger<CheckoutController> _logger;

        public CheckoutController(
            IConfiguration config,
            ApplicationDbContext context,
            IOptionsSnapshot<BookPaymentOptions> bookPaymentOptions,
            IBookService bookService,
            IBookPageMetricsService bookPageMetricsService,
            ILogger<CheckoutController> logger)
        {
            _config = config;
            _context = context;
            _bookPaymentOptions = bookPaymentOptions;
            _bookService = bookService;
            _bookPageMetricsService = bookPageMetricsService;
            _logger = logger;
        }

        /// <summary>Public site origin for Stripe redirect URLs (use behind reverse proxy).</summary>
        private string ResolvePublicOrigin()
        {
            var configured = _config["App:PublicBaseUrl"]?.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(configured))
                return $"{Request.Scheme}://{Request.Host.Value}";
            if (!configured.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !configured.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                configured = "https://" + configured.TrimStart('/');
            return configured;
        }

        [HttpGet]
        public IActionResult GetPublishableKey()
        {
            var key = StripeKeys.Publishable(_config);
            if (string.IsNullOrEmpty(key))
                return BadRequest(new { message = "Stripe publishable key not configured." });

            return Ok(new { publishableKey = key });
        }

        [HttpPost]
        public IActionResult CreateSession([FromBody] CheckoutRequest request)
        {
            try
            {
                var secretKey = StripeKeys.Secret(_config);
                if (string.IsNullOrEmpty(secretKey))
                    return BadRequest(new { message = "Stripe secret key not configured." });

                StripeConfiguration.ApiKey = secretKey;

                if (request.Amount <= 0)
                    return BadRequest(new { message = "Invalid amount." });

                var origin = ResolvePublicOrigin();

                var options = new SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
                    {
                        new SessionLineItemOptions
                        {
                            PriceData = new SessionLineItemPriceDataOptions
                            {
                                UnitAmount = request.Amount,
                                Currency = request.Currency,
                                ProductData = new SessionLineItemPriceDataProductDataOptions
                                {
                                    Name = request.ProductName,
                                    Description = request.ProductDescription
                                }
                            },
                            Quantity = 1
                        }
                    },
                    Mode = "payment",
                    SuccessUrl = $"{origin}/Checkout/Success?session_id={{CHECKOUT_SESSION_ID}}",
                    CancelUrl = $"{origin}/Checkout/Cancel",
                    ClientReferenceId = request.AuthorPlanId?.ToString()
                };

                var service = new SessionService();
                var session = service.Create(options);

                return Ok(new { sessionId = session.Id, url = session.Url });
            }
            catch (StripeException ex)
            {
                return BadRequest(new { message = $"Stripe error: {ex.Message}" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"Server error: {ex.Message}" });
            }
        }

        [HttpGet]
        public IActionResult Success(string session_id)
        {
            ViewBag.SessionId = session_id;
            return View();
        }

        [HttpGet]
        public IActionResult Cancel()
        {
            return View();
        }

        /// <summary>Legacy book payment URL — redirects to export/publish (no Stripe gate).</summary>
        [HttpGet]
        public async Task<IActionResult> BookPayment(int bookId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("UserLogin", "Account");
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId.Value);
            if (book == null) return NotFound("Book not found.");
            return RedirectToAction("Publish", "Dashboard", new { bookId });
        }

        /// <summary>Create Stripe Checkout session for a book; success callback verifies payment before updating status.</summary>
        [HttpPost]
        public async Task<IActionResult> CreateBookCheckoutSession([FromBody] BookCheckoutRequest request)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();
            if (request?.BookId <= 0) return BadRequest(new { message = "Invalid book." });
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == request.BookId && b.UserId == userId.Value);
            if (book == null) return NotFound(new { message = "Book not found." });
            var st = book.Status ?? "";
            if (st.Equals("Paid", StringComparison.OrdinalIgnoreCase) || st.Equals("Published", StringComparison.OrdinalIgnoreCase))
                return Ok(new { sessionId = (string?)null, url = (string?)null, alreadyPaid = true });

            var secretKey = StripeKeys.Secret(_config);
            if (string.IsNullOrEmpty(secretKey)) return BadRequest(new { message = "Stripe not configured." });
            StripeConfiguration.ApiKey = secretKey;

            var pay = _bookPaymentOptions.Value;
            var metrics = await BuildBookPageMetricsAsync(userId.Value, request.BookId);
            // Single source-of-truth: always derive billing pages server-side from manuscript metrics.
            var pageCount = metrics.PageCount;
            var amountCents = ResolveCheckoutAmountCents(pay, pageCount);
            var currency = string.IsNullOrWhiteSpace(pay.Currency) ? "usd" : pay.Currency.Trim().ToLowerInvariant();

            var origin = ResolvePublicOrigin();
            var successPath = string.IsNullOrWhiteSpace(pay.SuccessPath) ? "/Checkout/BookPaymentSuccess" : pay.SuccessPath.Trim();
            if (!successPath.StartsWith('/')) successPath = "/" + successPath;
            var successUrl = $"{origin}{successPath}?session_id={{CHECKOUT_SESSION_ID}}";

            string cancelUrl;
            if (request.PublishIntent)
                cancelUrl = $"{origin}/publish?bookId={request.BookId}";
            else
            {
                var cancelTpl = string.IsNullOrWhiteSpace(pay.CancelPathTemplate)
                    ? "/Checkout/BookPayment?bookId={bookId}"
                    : pay.CancelPathTemplate.Trim();
                if (!cancelTpl.StartsWith('/')) cancelTpl = "/" + cancelTpl;
                cancelUrl = $"{origin}{cancelTpl.Replace("{bookId}", request.BookId.ToString(), StringComparison.OrdinalIgnoreCase)}";
            }

            var customerEmail = User.FindFirst(ClaimTypes.Email)?.Value?.Trim();
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = amountCents,
                            Currency = currency,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "E-book: " + (book.Title ?? "Your Book"),
                                Description = $"Unlock downloads and publishing for this book. {pageCount} pages."
                            }
                        },
                        Quantity = 1
                    }
                },
                Mode = "payment",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                ClientReferenceId = $"book-{request.BookId}-user-{userId.Value}",
                Metadata = new Dictionary<string, string>
                {
                    { "bookId", request.BookId.ToString() },
                    { "userId", userId.Value.ToString() },
                    { "publishIntent", request.PublishIntent ? "1" : "0" },
                    { "pageCount", pageCount.ToString() },
                    { "amountCents", amountCents.ToString() }
                }
            };
            if (!string.IsNullOrEmpty(customerEmail))
                options.CustomerEmail = customerEmail;

            try
            {
                var service = new SessionService();
                var session = service.Create(options);
                return Ok(new
                {
                    sessionId = session.Id,
                    url = session.Url,
                    alreadyPaid = false,
                    pageCount,
                    amountCents
                });
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Stripe CreateBookCheckoutSession failed for book {BookId}", request.BookId);
                return BadRequest(new { message = ex.StripeError?.Message ?? ex.Message });
            }
        }

        private int ResolveCheckoutAmountCents(BookPaymentOptions pay, int pageCount)
        {
            if (pay.UsePageBasedPricing && pageCount > 0)
            {
                var perPage = pay.PerPagePriceCents > 0 ? pay.PerPagePriceCents : 12;
                var amount = pageCount * perPage;
                var min = pay.MinimumChargeCents > 0 ? pay.MinimumChargeCents : 999;
                var max = pay.MaximumChargeCents > 0 ? pay.MaximumChargeCents : int.MaxValue;
                amount = Math.Max(amount, min);
                amount = Math.Min(amount, max);
                return amount;
            }

            return pay.AmountCents > 0 ? pay.AmountCents : 999;
        }

        private async Task<BookPageMetricsDto> BuildBookPageMetricsAsync(int userId, int bookId)
        {
            var details = await _bookService.GetBookDetailsForPreviewAsync(userId, bookId);
            if (details == null || !details.Success)
                return new BookPageMetricsDto { PageCount = 24, Basis = "fallback_no_book_details" };

            var draftRow = await _context.Settings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:formattingDraft");
            var exportOpt = BookPdfExportOptions.FromDraftJson(draftRow?.Value);
            var fmtRow = await _context.BookFormatting.AsNoTracking()
                .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == userId);
            exportOpt.MergeFromBookFormatting(fmtRow);

            return _bookPageMetricsService.Estimate(details, exportOpt);
        }

        /// <summary>Stripe return URL: verify paid session, then set book status.</summary>
        [HttpGet]
        public async Task<IActionResult> BookPaymentSuccess(string session_id)
        {
            if (string.IsNullOrEmpty(session_id))
                return RedirectToAction("MyBooks", "Dashboard", new { payment = "incomplete" });

            var secretKey = StripeKeys.Secret(_config);
            if (string.IsNullOrEmpty(secretKey))
            {
                _logger.LogWarning("BookPaymentSuccess: Stripe secret not configured.");
                return RedirectToAction("MyBooks", "Dashboard", new { payment = "config" });
            }

            StripeConfiguration.ApiKey = secretKey;
            try
            {
                var service = new SessionService();
                var session = service.Get(session_id);
                if (session == null)
                    return RedirectToAction("MyBooks", "Dashboard", new { payment = "incomplete" });

                if (!string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("BookPaymentSuccess: session {SessionId} payment status is {Status}", session_id, session.PaymentStatus);
                    return RedirectToAction("MyBooks", "Dashboard", new { payment = "pending" });
                }

                if (session.Metadata == null
                    || !session.Metadata.TryGetValue("bookId", out var bookIdStr)
                    || !int.TryParse(bookIdStr, out var bookId)
                    || !session.Metadata.TryGetValue("userId", out var ownerStr)
                    || !int.TryParse(ownerStr, out var ownerUserId))
                {
                    _logger.LogWarning("BookPaymentSuccess: session {SessionId} missing bookId/userId metadata.", session_id);
                    return RedirectToAction("MyBooks", "Dashboard", new { payment = "incomplete" });
                }

                var sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (sessionUserId != null && sessionUserId.Value != ownerUserId)
                {
                    _logger.LogWarning("BookPaymentSuccess: logged-in user does not own checkout metadata for session {SessionId}.", session_id);
                    return RedirectToAction("MyBooks", "Dashboard", new { payment = "mismatch" });
                }

                var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == ownerUserId);
                if (book == null)
                    return RedirectToAction("MyBooks", "Dashboard", new { payment = "incomplete" });

                var publishIntent = session.Metadata.TryGetValue("publishIntent", out var pi) && pi == "1";
                book.Status = publishIntent ? "Published" : "Paid";
                book.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return RedirectToAction("MyBooks", "Dashboard", new { payment = "success" });
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "BookPaymentSuccess: Stripe error for session {SessionId}", session_id);
                return RedirectToAction("MyBooks", "Dashboard", new { payment = "error" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BookPaymentSuccess: unexpected error for session {SessionId}", session_id);
                return RedirectToAction("MyBooks", "Dashboard", new { payment = "error" });
            }
        }
    }

    public class BookCheckoutRequest
    {
        public int BookId { get; set; }
        /// <summary>When true, success marks the book Published (listing); otherwise Paid (downloads).</summary>
        public bool PublishIntent { get; set; }
        public string? Format { get; set; }
        public int? PageCount { get; set; }
        public bool PrintReadyFlow { get; set; }
    }

    public class CheckoutRequest
    {
        public string ProductName { get; set; } = string.Empty;
        public string ProductDescription { get; set; } = string.Empty;
        public long Amount { get; set; }
        public string Currency { get; set; } = "usd";
        public int? AuthorPlanId { get; set; }
    }
}
