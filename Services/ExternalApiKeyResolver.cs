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
        var raw = (configuration["ExternalApi:ApiKey"] ?? "").Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            raw = raw[1..^1].Trim();
        if (raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'')
            raw = raw[1..^1].Trim();
        return raw;
    }

    public static string MissingKeyUserMessage { get; } =
        "Server configuration error: no API key for the book service. On production set environment variable " +
        "ExternalApi__ApiKey; locally use appsettings.Local.json or dotnet user-secrets. " +
        "Keeping ApiKey empty in appsettings.json in git is intentional — do not commit secrets.";
}
