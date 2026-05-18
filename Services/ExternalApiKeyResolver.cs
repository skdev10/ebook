using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services;

/// <summary>
/// Resolves the API key sent to the Python external API (X-API-Key) from <c>ExternalApi:ApiKey</c>.
/// </summary>
public static class ExternalApiKeyResolver
{
    public static string Resolve(IConfiguration? configuration)
    {
        if (configuration == null) return "";
        return (configuration["ExternalApi:ApiKey"] ?? "").Trim();
    }

    public static string MissingKeyUserMessage { get; } =
        "Server configuration error: no API key for the book service. On production set environment variable " +
        "ExternalApi__ApiKey; locally use appsettings.Local.json or dotnet user-secrets. " +
        "Keeping ApiKey empty in appsettings.json in git is intentional — do not commit secrets.";
}
