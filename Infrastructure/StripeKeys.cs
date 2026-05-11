using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Infrastructure;

/// <summary>
/// Resolves Stripe keys from either top-level <c>Stripe:*</c> or nested <c>PaymentSettings:Stripe:*</c>.
/// </summary>
public static class StripeKeys
{
    public static string? Publishable(IConfiguration c) =>
        NullIfEmpty(c["Stripe:PublishableKey"] ?? c["PaymentSettings:Stripe:PublishableKey"]);

    public static string? Secret(IConfiguration c) =>
        NullIfEmpty(c["Stripe:SecretKey"] ?? c["PaymentSettings:Stripe:SecretKey"]);

    public static string? WebhookSecret(IConfiguration c) =>
        NullIfEmpty(c["Stripe:WebhookSecret"] ?? c["PaymentSettings:Stripe:WebhookSecret"]);

    private static string? NullIfEmpty(string? s)
    {
        var t = s?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
