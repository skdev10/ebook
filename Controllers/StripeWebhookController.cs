using EBookDashboard.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class StripeWebhookController : Controller
{
    private readonly IPaymentService _payments;
    private readonly IOrderService _orders;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(IPaymentService payments, IOrderService orders, ILogger<StripeWebhookController> logger)
    {
        _payments = payments;
        _orders = orders;
        _logger = logger;
    }

    [HttpPost("/webhooks/stripe")]
    public async Task<IActionResult> Receive()
    {
        string json;
        using (var reader = new StreamReader(Request.Body))
            json = await reader.ReadToEndAsync();

        var signature = Request.Headers["Stripe-Signature"].ToString();
        if (!_payments.TryReadPaidCheckout(json, signature, out var sessionId, out var paymentIntentId, out var ignored))
        {
            if (ignored)
                return Ok();
            _logger.LogWarning("Stripe webhook signature rejected.");
            return BadRequest();
        }

        await _orders.MarkPaidAsync(sessionId, paymentIntentId);
        return Ok();
    }
}
