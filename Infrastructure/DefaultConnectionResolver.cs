using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace EBookDashboard.Infrastructure;

/// <summary>
/// Resolves MySQL DefaultConnection from config, env, or optional local fallback (see Database:UseLocalFallbackConnection).
/// Fallback honors MYSQL_HOST, MYSQL_PORT, MYSQL_DATABASE, MYSQL_USER, MYSQL_PASSWORD / MYSQL_ROOT_PASSWORD (Docker/Linux style).
/// Also supports DATABASE_URL / MYSQL_URL style URLs in hosted environments.
/// </summary>
public static class DefaultConnectionResolver
{
    private static readonly string[] DatabaseUrlEnvironmentKeys =
    {
        "DATABASE_URL",
        "MYSQL_URL",
        "CLEARDB_DATABASE_URL"
    };

    /// <summary>
    /// Resolve MySQL connection string from app config and common environment variable conventions.
    /// </summary>
    public static string Resolve(IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            cs = ResolveFromDatabaseUrlEnvironmentVariable();

        if (string.IsNullOrWhiteSpace(cs) && configuration.GetValue("Database:UseLocalFallbackConnection", false))
            cs = BuildFallbackConnection(configuration);

        cs = cs?.Trim();
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is missing. Options: environment variable ConnectionStrings__DefaultConnection, " +
                "DATABASE_URL / MYSQL_URL, " +
                "appsettings.Local.json (see appsettings.Local.json.example), or with UseLocalFallbackConnection: set MYSQL_PASSWORD or MYSQL_ROOT_PASSWORD " +
                "(and optionally MYSQL_HOST, MYSQL_PORT, MYSQL_DATABASE, MYSQL_USER), or Database:LocalFallbackConnection as a full connection string.");
        }

        return cs;
    }

    /// <summary>
    /// Builds a fallback connection string: Database:LocalFallbackConnection if set; else MYSQL_* / Database:Fallback*; else localhost defaults.
    /// </summary>
    public static string BuildFallbackConnection(IConfiguration configuration)
    {
        var explicitCs = configuration["Database:LocalFallbackConnection"]?.Trim();
        if (!string.IsNullOrWhiteSpace(explicitCs))
            return explicitCs;

        var host = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQL_HOST"),
            Environment.GetEnvironmentVariable("MYSQL_TCP_HOST"),
            configuration["Database:FallbackServer"]) ?? "127.0.0.1";

        var portStr = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQL_PORT"),
            configuration["Database:FallbackPort"]) ?? "3306";
        var portOk = uint.TryParse(portStr, out var port) ? port : 3306u;

        var database = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQL_DATABASE"),
            configuration["Database:FallbackDatabase"]) ?? "ebookpublications";

        var user = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQL_USER"),
            configuration["Database:FallbackUser"]) ?? "root";

        var password = FirstNonEmpty(
            Environment.GetEnvironmentVariable("MYSQL_PASSWORD"),
            Environment.GetEnvironmentVariable("MYSQL_ROOT_PASSWORD"),
            configuration["Database:FallbackPassword"]) ?? "";

        var csb = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = portOk,
            Database = database,
            UserID = user,
            Password = password,
            AllowPublicKeyRetrieval = true,
        };

        return csb.ConnectionString;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private static string? ResolveFromDatabaseUrlEnvironmentVariable()
    {
        foreach (var key in DatabaseUrlEnvironmentKeys)
        {
            var raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            if (TryBuildConnectionStringFromUrl(raw, out var cs))
                return cs;
        }

        return null;
    }

    private static bool TryBuildConnectionStringFromUrl(string url, out string? connectionString)
    {
        connectionString = null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, "mysql", StringComparison.OrdinalIgnoreCase))
            return false;

        var userInfo = uri.UserInfo.Split(':', 2, StringSplitOptions.None);
        var user = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.Trim('/'); // DATABASE_URL format: mysql://user:pass@host:3306/dbname

        var builder = new MySqlConnectionStringBuilder
        {
            Server = string.IsNullOrWhiteSpace(uri.Host) ? "127.0.0.1" : uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 3306u : (uint)uri.Port,
            Database = string.IsNullOrWhiteSpace(database) ? "ebookpublications" : Uri.UnescapeDataString(database),
            UserID = string.IsNullOrWhiteSpace(user) ? "root" : user,
            Password = password,
            AllowPublicKeyRetrieval = true
        };

        if (!string.IsNullOrWhiteSpace(uri.Query))
            ApplyQueryParameters(uri.Query, builder);

        connectionString = builder.ConnectionString;
        return true;
    }

    private static void ApplyQueryParameters(string query, MySqlConnectionStringBuilder builder)
    {
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        foreach (var segment in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = segment.Split('=', 2, StringSplitOptions.None);
            var key = Uri.UnescapeDataString(pair[0]);
            var value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;

            switch (key.ToLowerInvariant())
            {
                case "sslmode":
                    if (Enum.TryParse<MySqlSslMode>(value, true, out var sslMode))
                        builder.SslMode = sslMode;
                    break;
                case "charset":
                    if (!string.IsNullOrWhiteSpace(value))
                        builder.CharacterSet = value;
                    break;
            }
        }
    }
}
