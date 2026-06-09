using EBookDashboard.Infrastructure;
using EBookDashboard.Models;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Middleware;

/// <summary>Blocks mobile/tablet browsers from user-facing HTML while leaving API and static assets available.</summary>
public class MobileBlockMiddleware
{
    private readonly RequestDelegate _next;
    private readonly MobileAccessOptions _options;

    public MobileBlockMiddleware(RequestDelegate next, IOptions<MobileAccessOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.BlockMobile || !_options.UseServerBlockPage || !MobileAccessRequestClassifier.ShouldBlockMobileBrowser(context.Request))
        {
            await _next(context);
            return;
        }

        var userAgent = context.Request.Headers.UserAgent.ToString();
        context.Request.Headers.TryGetValue("Sec-CH-UA-Mobile", out var clientHintMobile);

        if (!MobileUserAgentDetector.IsMobileOrTablet(userAgent, clientHintMobile.ToString()))
        {
            await _next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store, no-cache";
        await context.Response.WriteAsync(MobileBlockPageRenderer.Render(_options));
    }
}
