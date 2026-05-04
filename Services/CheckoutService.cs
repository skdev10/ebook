// Services/ICheckoutService.cs
using EBookDashboard.Interfaces;
using Microsoft.AspNetCore.Http;
using Stripe;
using Stripe.Checkout;

namespace EBookDashboard.Services
{
    public class CheckoutService : ICheckoutService
    {
        private readonly StripeClient _client;
        private readonly string _webhookSecret;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CheckoutService(IConfiguration config, IHttpContextAccessor httpContextAccessor)
        {
            var secretKey = config["Stripe:SecretKey"] ?? string.Empty;
            _webhookSecret = config["Stripe:WebhookSecret"] ?? string.Empty;
            _client = new StripeClient(secretKey);
            _configuration = config;
            _httpContextAccessor = httpContextAccessor;
        }

        private string ResolvePublicOrigin()
        {
            var req = _httpContextAccessor.HttpContext?.Request;
            if (req != null && !string.IsNullOrEmpty(req.Host.Value))
                return $"{req.Scheme}://{req.Host}";

            var appUrl = _configuration["App:PublicBaseUrl"] ?? _configuration["PaymentSettings:Stripe:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(appUrl))
                return appUrl.TrimEnd('/');

            return "http://localhost:5000";
        }

        public async Task<Session> CreateCheckoutSessionAsync(string productName, long amount, string currency)
        {
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
                SuccessUrl = $"{origin}/Checkout/OrderConfirmation",
                CancelUrl = $"{origin}/Checkout/Index"
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
