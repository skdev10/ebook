using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services;

/// <summary>
/// Resolves the API key sent to the Python external API (X-API-Key). Prefer <c>ExternalApi:ApiKey</c>;
/// if unset, falls back to <c>OpenAI:ApiKey</c> so production can set a single <c>OpenAI__ApiKey</c> env var.
/// </summary>
public static class ExternalApiKeyResolver
{
    public static string Resolve(IConfiguration? configuration)
    {
        if (configuration == null) return "";
        var k = (configuration["ExternalApi:ApiKey"] ?? "").Trim();
        if (k.Length > 0) return k;
        k = (configuration["OpenAI:ApiKey"] ?? "").Trim();
        return k;
    }

    public static string MissingKeyUserMessage { get; } =
        "Server configuration error: no API key for the external book API. Set ExternalApi:ApiKey or OpenAI:ApiKey " +
        "(on the host use environment variables ExternalApi__ApiKey or OpenAI__ApiKey).";
}
