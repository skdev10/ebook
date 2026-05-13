using System.Net.Mime;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EBookDashboard.Health;

public static class HealthCheckResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static Task WriteAsync(HttpContext httpContext, HealthReport report)
    {
        httpContext.Response.ContentType = MediaTypeNames.Application.Json;
        // App Platform / load balancers: return 200 when the host is up; surface upstream in payload.
        httpContext.Response.StatusCode = StatusCodes.Status200OK;

        var upstream = report.Entries.TryGetValue("upstream_book_api", out var u)
            ? new { status = u.Status.ToString(), description = u.Description }
            : null;

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            upstream
        };
        return httpContext.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
    }
}
