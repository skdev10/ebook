using EBookDashboard.Infrastructure;
using EBookDashboard.Interfaces;
using Microsoft.Extensions.Configuration;
using Stripe;
using Stripe.Checkout;

namespace EBookDashboard.Services;

public sealed class StripePaymentService : IPaymentService
{
    private readonly IConfiguration _config;

    public StripePaymentService(IConfiguration config)
    {
        _config = config;
    }

    /// <inheritdoc />
    public async Task<PaymentSession> CreateSessionAsync(int orderId, decimal amount, string currency, string productName, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
    {
        var secret = StripeKeys.Secret(_config);
        if (string.IsNullOrEmpty(secret))
            throw new InvalidOperationException("Stripe secret key is not configured.");

        StripeConfiguration.ApiKey = secret;
        var cents = (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (cents < 50)
            throw new InvalidOperationException("Stripe Checkout requires a minimum charge of $0.50.");

        var service = new SessionService();
        var session = await service.CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            ClientReferenceId = orderId.ToString(),
            Metadata = new Dictionary<string, string> { ["orderId"] = orderId.ToString() },
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = (currency ?? "usd").ToLowerInvariant(),
                        UnitAmount = cents,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = productName
                        }
                    }
                }
            }
        }, cancellationToken: cancellationToken);

        return new PaymentSession { SessionId = session.Id, Url = session.Url };
    }

    /// <inheritdoc />
    public async Task<PaymentConfirmation> ConfirmSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var result = new PaymentConfirmation { SessionId = sessionId ?? "" };
        if (string.IsNullOrWhiteSpace(sessionId))
            return result;

        var secret = StripeKeys.Secret(_config);
        if (string.IsNullOrEmpty(secret))
            return result;

        StripeConfiguration.ApiKey = secret;
        try
        {
            var session = await new SessionService().GetAsync(sessionId, cancellationToken: cancellationToken);
            var paid = string.Equals(session.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase)
                || string.Equals(session.Status, "complete", StringComparison.OrdinalIgnoreCase);
            result.Paid = paid;
            result.PaymentIntentId = session.PaymentIntentId ?? "";
        }
        catch (StripeException)
        {
            return result;
        }

        return result;
    }

    /// <inheritdoc />
    public bool TryReadPaidCheckout(string json, string? signature, out string sessionId, out string paymentIntentId, out bool ignored)
    {
        sessionId = "";
        paymentIntentId = "";
        ignored = false;
        var webhookSecret = StripeKeys.WebhookSecret(_config);
        if (string.IsNullOrEmpty(webhookSecret))
            return false;

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, signature, webhookSecret);
        }
        catch (StripeException)
        {
            return false;
        }

        if (!string.Equals(stripeEvent.Type, "checkout.session.completed", StringComparison.Ordinal))
        {
            ignored = true;
            return false;
        }

        if (stripeEvent.Data.Object is not Session session)
        {
            ignored = true;
            return false;
        }

        sessionId = session.Id ?? "";
        paymentIntentId = session.PaymentIntentId ?? "";
        return !string.IsNullOrEmpty(sessionId);
    }
}
