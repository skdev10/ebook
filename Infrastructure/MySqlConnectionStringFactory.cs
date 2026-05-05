using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace EBookDashboard.Infrastructure;

/// <summary>
/// Builds a MySQL connection string with optional managed-DB SSL CA (SslCa + VerifyCA).
/// Skips CA for localhost/127.0.0.1/::1 unless <c>Database:ForceSslCa</c> is true (SSH tunnel case).
/// Set <c>Database:UseSslCa</c> to false to disable CA injection entirely (e.g. local dev).
/// </summary>
public static class MySqlConnectionStringFactory
{
    private const string TempCaFileName = "ebook-do-mysql-ca.pem";

    /// <summary>
    /// Resolves CA from <paramref name="configuration"/> keys Database:SslCaPem (or env Database__SslCaPem),
    /// or from Database:SslCaPath relative to <paramref name="contentRoot"/> (default Certificates/ca-certificate.crt when the key is omitted).
    /// An explicit empty Database:SslCaPath disables file-based CA lookup.
    /// </summary>
    public static string Build(IConfiguration configuration, string contentRoot, string? baseConnectionString)
    {
        if (string.IsNullOrWhiteSpace(baseConnectionString))
            throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        var csb = new MySqlConnectionStringBuilder(baseConnectionString);

        var useSslCa = configuration.GetValue("Database:UseSslCa", true);
        if (!useSslCa)
        {
            EnsureNonSslAuthCompatibility(csb);
            return csb.ConnectionString;
        }

        // Optional PEM from config/env (e.g. container / platform secrets).
        var pem = configuration["Database:SslCaPem"];
        if (string.IsNullOrWhiteSpace(pem))
            pem = Environment.GetEnvironmentVariable("Database__SslCaPem");

        string? sslCaFullPath = null;

        if (!string.IsNullOrWhiteSpace(pem))
        {
            var normalized = pem.Replace("\\n", "\n", StringComparison.Ordinal);
            var tmp = Path.Combine(Path.GetTempPath(), TempCaFileName);
            File.WriteAllText(tmp, normalized);
            sslCaFullPath = tmp;
        }
        else
        {
            // null = default bundled path; explicit "" disables path-based CA (local dev).
            var rel = configuration["Database:SslCaPath"];
            if (rel == null)
                rel = "Certificates/ca-certificate.crt";
            else if (string.IsNullOrWhiteSpace(rel))
                rel = null;

            if (!string.IsNullOrEmpty(rel))
            {
                var full = Path.IsPathRooted(rel)
                    ? rel
                    : Path.Combine(contentRoot, rel.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(full))
                    sslCaFullPath = full;
            }
        }

        // Bundled CA is for managed cloud DBs (e.g. DigitalOcean). Local MySQL uses a different cert chain;
        // forcing VerifyCA here causes RemoteCertificateChainErrors on sign-in.
        var forceSslCa = configuration.GetValue("Database:ForceSslCa", false);
        if (sslCaFullPath != null && IsLocalMySqlHost(csb.Server) && !forceSslCa)
        {
            EnsureNonSslAuthCompatibility(csb);
            return csb.ConnectionString;
        }

        if (sslCaFullPath != null)
        {
            csb.SslMode = MySqlSslMode.VerifyCA;
            csb.SslCa = sslCaFullPath;
        }
        else
        {
            EnsureNonSslAuthCompatibility(csb);
        }

        return csb.ConnectionString;
    }

    /// <summary>
    /// For local/non-SSL connections using MySQL 8+ default auth (caching_sha2_password),
    /// allow RSA key retrieval when TLS/CA verification is not enabled.
    /// </summary>
    private static void EnsureNonSslAuthCompatibility(MySqlConnectionStringBuilder csb)
    {
        if (csb.SslMode != MySqlSslMode.VerifyCA && csb.SslMode != MySqlSslMode.VerifyFull)
            csb.AllowPublicKeyRetrieval = true;
    }

    private static bool IsLocalMySqlHost(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
            return false;

        foreach (var part in server.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(part, "127.0.0.1", StringComparison.Ordinal)
                || string.Equals(part, "::1", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
