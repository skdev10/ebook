using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace EBookDashboard.Infrastructure;

/// <summary>Returns 401 for JSON/AJAX API calls instead of redirecting to login (avoids broken fetch parsing).</summary>
internal static class AuthRedirectHelper
{
    internal static bool IsApiLikeRequest(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return true;
        if (request.Headers.TryGetValue("X-Requested-With", out var xrw)
            && string.Equals(xrw.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            return true;
        if (request.Headers.Accept.Any(a =>
                a != null && (a.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                              || a.Contains("text/json", StringComparison.OrdinalIgnoreCase))))
            return true;
        var ct = request.ContentType;
        return ct != null && ct.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    internal static Task RedirectToLoginOrUnauthorized(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (IsApiLikeRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
