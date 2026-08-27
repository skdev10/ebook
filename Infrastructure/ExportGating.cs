using EBookDashboard.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Infrastructure;

public static class ExportGating
{
    public static async Task<IActionResult?> RequirePaidJsonAsync(HttpContext http, int bookId, bool paperback, bool hardcover, CancellationToken cancellationToken = default)
    {
        var userId = http.Session.GetInt32("UserId");
        if (userId == null)
            return new UnauthorizedObjectResult(new { success = false, message = "Please sign in." });

        var access = http.RequestServices.GetRequiredService<IExportAccessService>();
        var result = await access.EvaluateAsync(userId.Value, bookId, paperback, hardcover, cancellationToken);
        if (result.Allowed)
            return null;

        return new ObjectResult(new
        {
            success = false,
            message = "Payment required before this download.",
            checkoutUrl = result.CheckoutPath,
            total = result.Quote.Total,
            currency = result.Quote.Currency
        })
        { StatusCode = StatusCodes.Status402PaymentRequired };
    }

    public static async Task<IActionResult?> RequirePaidRedirectAsync(HttpContext http, int bookId, bool paperback, bool hardcover, CancellationToken cancellationToken = default)
    {
        var userId = http.Session.GetInt32("UserId");
        if (userId == null)
            return new RedirectResult("/Account/UserLogin");

        var access = http.RequestServices.GetRequiredService<IExportAccessService>();
        var result = await access.EvaluateAsync(userId.Value, bookId, paperback, hardcover, cancellationToken);
        if (result.Allowed)
            return null;

        return new RedirectResult(result.CheckoutPath);
    }
}
