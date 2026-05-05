using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace EBookDashboard.Infrastructure;

/// <summary>
/// Resolves MySQL DefaultConnection from config, env, or optional local fallback (see Database:UseLocalFallbackConnection).
/// Fallback honors MYSQL_HOST, MYSQL_PORT, MYSQL_DATABASE, MYSQL_USER, MYSQL_PASSWORD / MYSQL_ROOT_PASSWORD (Docker/Linux style).
/// </summary>
public static class DefaultConnectionResolver
{
    public static string Resolve(IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs))
            cs = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");

        if (string.IsNullOrWhiteSpace(cs) && configuration.GetValue("Database:UseLocalFallbackConnection", false))
            cs = BuildFallbackConnection(configuration);

        cs = cs?.Trim();
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is missing. Options: environment variable ConnectionStrings__DefaultConnection, " +
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
            configuration["Database:FallbackDatabase"]) ?? "ebook";

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
}
