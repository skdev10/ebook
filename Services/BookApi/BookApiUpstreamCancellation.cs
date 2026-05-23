using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services.BookApi;

/// <summary>
/// Server-side cancellation for long upstream AI calls.
/// Does not use <see cref="Microsoft.AspNetCore.Http.HttpContext.RequestAborted"/> so a proxy/browser
/// disconnect (504) does not abort an in-flight chapter/cover request before the HttpClient budget expires.
/// </summary>
public static class BookApiUpstreamCancellation
{
    public static CancellationTokenSource CreateLongRunning(IConfiguration configuration)
    {
        var mins = ResolveTimeoutMinutes(configuration);
        return new CancellationTokenSource(TimeSpan.FromMinutes(mins));
    }

    public static int ResolveTimeoutMinutes(IConfiguration configuration)
    {
        var mins = int.TryParse(configuration["ChapterGeneration:HttpTimeoutMinutes"], out var m) ? m : 30;
        return Math.Clamp(mins, 5, 120);
    }
}
