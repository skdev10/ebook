namespace EBookDashboard.Services;

/// <summary>
/// Loads a persisted cover reference (local path, data URL, or remote URL) as raw Base64 for upstream APIs.
/// </summary>
public static class CoverImageRefLoader
{
    /// <summary>
    /// Returns raw Base64 (no <c>data:image/...;base64,</c> prefix) or null when the reference cannot be read.
    /// </summary>
    public static async Task<string?> TryReadAsRawBase64Async(
        string? imageRef,
        string? webRootPath,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageRef)) return null;
        var refValue = imageRef.Trim();

        if (refValue.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            var comma = refValue.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0) return null;
            var b64 = refValue[(comma + 1)..].Trim();
            return string.IsNullOrWhiteSpace(b64) ? null : b64;
        }

        byte[]? bytes = null;

        if (refValue.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || refValue.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromMinutes(2);
                using var resp = await client.GetAsync(refValue, cancellationToken);
                if (!resp.IsSuccessStatusCode) return null;
                bytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch
            {
                return null;
            }
        }
        else if (refValue.StartsWith("/", StringComparison.Ordinal))
        {
            byte[]? localBytes = null;
            foreach (var root in WebRootCandidates(webRootPath))
            {
                var rel = refValue.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(root, rel);
                if (!File.Exists(fullPath)) continue;
                localBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
                break;
            }

            if (localBytes == null || localBytes.Length == 0) return null;
            bytes = localBytes;
        }
        else if (Base64CoverImageHelper.TryDecodeToImageBytes(refValue) is { Success: true, Bytes: not null } decoded)
        {
            bytes = decoded.Bytes;
        }

        if (bytes == null || bytes.Length == 0) return null;
        return Convert.ToBase64String(bytes);
    }

    /// <summary>Loads cover reference as raw bytes (local path, data URL, remote URL, or raw base64).</summary>
    public static async Task<byte[]?> TryReadAsBytesAsync(
        string? imageRef,
        string? webRootPath,
        IHttpClientFactory httpClientFactory,
        CancellationToken cancellationToken = default)
    {
        var b64 = await TryReadAsRawBase64Async(imageRef, webRootPath, httpClientFactory, cancellationToken);
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            return Convert.FromBase64String(b64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static IEnumerable<string> WebRootCandidates(string? webRootPath)
    {
        if (!string.IsNullOrWhiteSpace(webRootPath))
            yield return webRootPath.Trim();

        yield return Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        yield return Path.Combine(AppContext.BaseDirectory, "wwwroot");
    }
}
