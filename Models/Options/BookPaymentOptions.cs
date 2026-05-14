namespace EBookDashboard.Models.Options;

/// <summary>Stripe Checkout settings for per-book publication unlock (<c>Checkout/BookPayment</c>).</summary>
public class BookPaymentOptions
{
    public const string SectionName = "BookPayment";

    /// <summary>Line item amount in smallest currency unit (e.g. cents for USD).</summary>
    public int AmountCents { get; set; } = 999;

    public string Currency { get; set; } = "usd";

    /// <summary>Optional path for success redirect (must include <c>{{CHECKOUT_SESSION_ID}}</c> placeholder handled in code).</summary>
    public string SuccessPath { get; set; } = "/Checkout/BookPaymentSuccess";

    /// <summary>Relative path for cancel; <c>{bookId}</c> is replaced when present.</summary>
    public string CancelPathTemplate { get; set; } = "/Checkout/BookPayment?bookId={bookId}";
}
