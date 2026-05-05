using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Infrastructure;

/// <summary>
/// Resolves MySQL DefaultConnection from config, env, or optional dev/local fallback (see Database:UseLocalFallbackConnection).
/// </summary>
public static class DefaultConnectionResolver
{
    private const string DefaultLocalFallback =
        "server=127.0.0.1;port=3306;database=ebook;user=root;password=;AllowPublicKeyRetrieval=true;";

    public static string Resolve(IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs) && configuration.GetValue("Database:UseLocalFallbackConnection", false))
        {
            cs = configuration["Database:LocalFallbackConnection"];
            if (string.IsNullOrWhiteSpace(cs))
                cs = DefaultLocalFallback;
        }

        cs = cs?.Trim();
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is missing. Options: set environment variable ConnectionStrings__DefaultConnection, " +
                "add ConnectionStrings:DefaultConnection to appsettings.Local.json (see appsettings.Local.json.example), " +
                "or set Database:UseLocalFallbackConnection to true with a Local MySQL matching Database:LocalFallbackConnection.");
        }

        return cs;
    }
}
