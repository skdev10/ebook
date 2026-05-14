using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Infrastructure;

/// <summary>
/// Resolves Google / Facebook OAuth credentials from every place teams commonly put them
/// (nested Authentication section, top-level Google/Facebook, env vars, alternate key names).
/// </summary>
public static class OAuthCredentialResolver
{
    private static bool LooksLikePlaceholder(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        var t = s.Trim();
        if (t.Length < 4) return true;
        if (t.Contains("PASTE", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Contains("REPLACE", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("changeme", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string? FirstReal(IConfiguration cfg, params string[] keys)
    {
        foreach (var key in keys)
        {
            var v = cfg[key]?.Trim();
            if (!LooksLikePlaceholder(v))
                return v;
        }
        return null;
    }

    public static string? GoogleClientId(IConfiguration c) => FirstReal(c,
        "Authentication:Google:ClientId",
        "Google:ClientId",
        "OAuth:Google:ClientId",
        "Social:Google:ClientId",
        "GOOGLE_CLIENT_ID");

    public static string? GoogleClientSecret(IConfiguration c) => FirstReal(c,
        "Authentication:Google:ClientSecret",
        "Google:ClientSecret",
        "OAuth:Google:ClientSecret",
        "GOOGLE_CLIENT_SECRET");

    public static string? FacebookAppId(IConfiguration c) => FirstReal(c,
        "Authentication:Facebook:AppId",
        "Facebook:AppId",
        "Authentication:Facebook:ClientId",
        "Facebook:ClientId",
        "FACEBOOK_APP_ID");

    public static string? FacebookAppSecret(IConfiguration c) => FirstReal(c,
        "Authentication:Facebook:AppSecret",
        "Facebook:AppSecret",
        "Authentication:Facebook:ClientSecret",
        "Facebook:ClientSecret",
        "FACEBOOK_APP_SECRET");
}
