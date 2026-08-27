using EBookDashboard.Models;

namespace EBookDashboard.Interfaces;

public interface IPricingService
{
    Task<PriceQuote> BuildQuoteAsync(PriceQuoteRequest request);
}
