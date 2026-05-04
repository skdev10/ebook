using EBookDashboard.Interfaces;
using Microsoft.AspNetCore.Http;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace EBookDashboard.Controllers
{
    [Route("[controller]/[action]")]
    [Authorize]
    public class CheckoutController : Controller
    {
        private readonly ICheckoutService _checkoutService;
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _context;

        public CheckoutController(ICheckoutService checkoutService, IConfiguration config, ApplicationDbContext context)
        {
            _checkoutService = checkoutService;
            _config = config;
            _context = context;
        }

        // FIXED: Changed route to match what JavaScript is calling
        [HttpGet]
        public IActionResult GetPublishableKey()
        {
            var key = _config["Stripe:PublishableKey"];
            if (string.IsNullOrEmpty(key))
                return BadRequest(new { message = "Stripe publishable key not configured." });

            return Ok(new { publishableKey = key });
        }

        [HttpPost]
        public IActionResult CreateSession([FromBody] CheckoutRequest request)
        {
            try
            {
                var secretKey = _config["Stripe:SecretKey"];
                if (string.IsNullOrEmpty(secretKey))
                    return BadRequest(new { message = "Stripe secret key not configured." });

                StripeConfiguration.ApiKey = secretKey;

                if (request.Amount <= 0)
                {
                    return BadRequest(new { message = "Invalid amount." });
                }

                var domain = $"{Request.Scheme}://{Request.Host.Value}";

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
                    SuccessUrl = $"{domain}/Checkout/Success?session_id={{CHECKOUT_SESSION_ID}}",
                    CancelUrl = $"{domain}/Checkout/Cancel",
                    ClientReferenceId = request.AuthorPlanId?.ToString() // Track which plan is being purchased
                };

                var service = new SessionService();
                var session = service.Create(options);

                return Ok(new { sessionId = session.Id });
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

        /// <summary>Book payment page: user must pay here to unlock downloads (after cover design or from My Books).</summary>
        [HttpGet]
        public async Task<IActionResult> BookPayment(int bookId)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return RedirectToAction("UserLogin", "Account");
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId.Value);
            if (book == null) return NotFound("Book not found.");
            ViewBag.BookId = bookId;
            ViewBag.BookTitle = book.Title ?? "Your Book";
            var bs = book.Status ?? "";
            ViewBag.Paid = bs.Equals("Paid", StringComparison.OrdinalIgnoreCase) || bs.Equals("Published", StringComparison.OrdinalIgnoreCase);
            ViewBag.PublishableKey = _config["Stripe:PublishableKey"] ?? "";
            return View();
        }

        /// <summary>Create Stripe checkout session for a book; success URL marks book as Paid.</summary>
        [HttpPost]
        public async Task<IActionResult> CreateBookCheckoutSession([FromBody] BookCheckoutRequest request)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null) return Unauthorized();
            if (request?.BookId <= 0) return BadRequest(new { message = "Invalid book." });
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == request.BookId && b.UserId == userId.Value);
            if (book == null) return NotFound(new { message = "Book not found." });
            var st = (book.Status ?? "");
            if (st.Equals("Paid", StringComparison.OrdinalIgnoreCase) || st.Equals("Published", StringComparison.OrdinalIgnoreCase))
                return Ok(new { sessionId = (string?)null, alreadyPaid = true });

            var secretKey = _config["Stripe:SecretKey"];
            if (string.IsNullOrEmpty(secretKey)) return BadRequest(new { message = "Stripe not configured." });
            StripeConfiguration.ApiKey = secretKey;

            var amountCents = 999; // $9.99 default; can be read from config "BookPayment:AmountCents"
            var cfgAmount = _config["BookPayment:AmountCents"];
            if (!string.IsNullOrEmpty(cfgAmount) && int.TryParse(cfgAmount, out var amt) && amt > 0) amountCents = amt;

            var domain = $"{Request.Scheme}://{Request.Host.Value}";
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
                            Currency = _config["BookPayment:Currency"] ?? "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = "E-book: " + (book.Title ?? "Your Book"),
                                Description = "Unlock downloads and publishing for this book."
                            }
                        },
                        Quantity = 1
                    }
                },
                Mode = "payment",
                SuccessUrl = $"{domain}/Checkout/BookPaymentSuccess?session_id={{CHECKOUT_SESSION_ID}}",
                CancelUrl = request.PublishIntent
                    ? $"{domain}/publish?bookId={request.BookId}"
                    : $"{domain}/Checkout/BookPayment?bookId={request.BookId}",
                Metadata = new Dictionary<string, string>
                {
                    { "bookId", request.BookId.ToString() },
                    { "publishIntent", request.PublishIntent ? "1" : "0" }
                }
            };
            var service = new SessionService();
            var session = service.Create(options);
            return Ok(new { sessionId = session.Id, url = session.Url, alreadyPaid = false });
        }

        /// <summary>Stripe success callback for book payment: mark book as Paid and redirect to My Books.</summary>
        [HttpGet]
        public async Task<IActionResult> BookPaymentSuccess(string session_id)
        {
            if (string.IsNullOrEmpty(session_id)) return RedirectToAction("MyBooks", "Dashboard");
            var secretKey = _config["Stripe:SecretKey"];
            if (string.IsNullOrEmpty(secretKey)) return RedirectToAction("MyBooks", "Dashboard");
            StripeConfiguration.ApiKey = secretKey;
            try
            {
                var service = new SessionService();
                var session = service.Get(session_id);
                if (session?.Metadata != null && session.Metadata.TryGetValue("bookId", out var bookIdStr) && int.TryParse(bookIdStr, out var bookId))
                {
                    var userId = HttpContext.Session.GetInt32("UserId");
                    var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId.GetValueOrDefault());
                    if (book != null)
                    {
                        var publishIntent = session.Metadata != null && session.Metadata.TryGetValue("publishIntent", out var pi) && pi == "1";
                        book.Status = publishIntent ? "Published" : "Paid";
                        book.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                    }
                }
            }
            catch { /* best effort */ }
            return RedirectToAction("MyBooks", "Dashboard");
        }
    }

    public class BookCheckoutRequest
    {
        public int BookId { get; set; }
        /// <summary>When true, success marks the book Published (listing); otherwise Paid (downloads).</summary>
        public bool PublishIntent { get; set; }
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

