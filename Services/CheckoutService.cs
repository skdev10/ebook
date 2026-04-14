// Services/ICheckoutService.cs
using EBookDashboard.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace EBookDashboard.Services
{
    public class CheckoutService : ICheckoutService
    {
        private readonly StripeClient _client;
        private readonly string _webhookSecret;

        public CheckoutService(IConfiguration config)
        {
            var secretKey = config["Stripe:SecretKey"] ?? string.Empty;
            _webhookSecret = config["Stripe:WebhookSecret"] ?? string.Empty;
            _client = new StripeClient(secretKey);
        }

        public async Task<Session> CreateCheckoutSessionAsync(string productName, long amount, string currency)
        {
            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = amount,
                            Currency = currency,
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = productName
                            }
                        },
                        Quantity = 1
                    }
                },
                Mode = "payment",
                SuccessUrl = "https://localhost:7187/Checkout/OrderConfirmation",
                CancelUrl = "https://localhost:7187/Checkout/Index"
            };

            var service = new SessionService(_client);
            return await service.CreateAsync(options);
        }

        public async Task HandleWebhookAsync(string json, string stripeSignature)
        {
            var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, _webhookSecret);

            if (stripeEvent.Type == "checkout.session.completed")
            {
                var session = stripeEvent.Data.Object as Session;
                Console.WriteLine($"✅ Payment completed. Session: {session?.Id}, Email: {session?.CustomerEmail}");
                // TODO: Save order to DB here
            }

            await Task.CompletedTask;
        }
    }

}
