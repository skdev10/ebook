using Stripe.Checkout;
namespace EBookDashboard.Interfaces
{
    // Services/ICheckoutService.cs
    public interface ICheckoutService
    {
        Task<Session> CreateCheckoutSessionAsync(string productName, long amount, string currency);
        Task HandleWebhookAsync(string json, string stripeSignature);
    }
}

