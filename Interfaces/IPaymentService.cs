namespace EBookDashboard.Interfaces;

public sealed class PaymentSession
{
    public string SessionId { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class PaymentConfirmation
{
    public bool Paid { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public string PaymentIntentId { get; set; } = string.Empty;
}

/// <summary>Provider-agnostic checkout. Stripe types stay in <c>StripePaymentService</c>.</summary>
public interface IPaymentService
{
    Task<PaymentSession> CreateSessionAsync(int orderId, decimal amount, string currency, string productName, string successUrl, string cancelUrl, CancellationToken cancellationToken = default);

    /// <summary>Asks the provider whether this checkout session is paid. Used on the success return URL.</summary>
    Task<PaymentConfirmation> ConfirmSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <param name="ignored">True when the signature is valid but the event is not a paid checkout (do not retry).</param>
    bool TryReadPaidCheckout(string json, string? signature, out string sessionId, out string paymentIntentId, out bool ignored);
}
