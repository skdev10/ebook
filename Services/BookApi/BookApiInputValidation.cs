using System.IO;

namespace EBookDashboard.Services.BookApi;

public static class BookApiInputValidation
{
    /// <summary>True when the file name ends with a documented upstream audio extension.</summary>
    public static bool IsAllowedAudioExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return BookApiConstants.ValidAudioExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a safe storage extension from filename and/or Content-Type.
    /// Browsers often send <c>blob</c>, <c>.ogg</c>, <c>.weba</c>, or empty names from MediaRecorder.
    /// </summary>
    /// <returns>Lowercase extension including the leading dot, or null if unsupported.</returns>
    public static string? ResolveAllowedAudioExtension(string? fileName, string? contentType)
    {
        var fromName = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        if (IsAllowedAudioExtension("x" + fromName))
            return fromName;

        // Aliases → nearest documented upstream format.
        fromName = fromName switch
        {
            ".ogg" or ".oga" or ".opus" or ".weba" => ".webm",
            ".aac" or ".caf" => ".m4a",
            ".3gp" or ".3gpp" or ".mov" => ".mp4",
            ".flac" or ".wave" => ".wav",
            ".mpg" or ".mpe" => ".mpeg",
            _ => fromName
        };
        if (IsAllowedAudioExtension("x" + fromName))
            return fromName;

        var ct = (contentType ?? "").Trim().ToLowerInvariant();
        if (ct.Length == 0) return null;
        // Strip codecs=… suffix: audio/webm;codecs=opus
        var semi = ct.IndexOf(';');
        if (semi > 0) ct = ct[..semi].Trim();

        return ct switch
        {
            "audio/webm" or "video/webm" or "audio/ogg" or "application/ogg" => ".webm",
            "audio/mp4" or "video/mp4" or "audio/x-m4a" or "audio/m4a" or "audio/aac" => ".m4a",
            "audio/mpeg" or "audio/mp3" or "audio/mpeg3" or "audio/x-mpeg-3" or "audio/mpga" => ".mp3",
            "audio/wav" or "audio/wave" or "audio/x-wav" => ".wav",
            _ when ct.Contains("webm", StringComparison.Ordinal) => ".webm",
            _ when ct.Contains("ogg", StringComparison.Ordinal) || ct.Contains("opus", StringComparison.Ordinal) => ".webm",
            _ when ct.Contains("wav", StringComparison.Ordinal) => ".wav",
            _ when ct.Contains("mpeg", StringComparison.Ordinal) || ct.Contains("mp3", StringComparison.Ordinal) => ".mp3",
            _ when ct.Contains("mp4", StringComparison.Ordinal) || ct.Contains("m4a", StringComparison.Ordinal) || ct.Contains("aac", StringComparison.Ordinal) => ".m4a",
            _ => null
        };
    }

    public static bool IsAllowedSize(string? size) =>
        !string.IsNullOrWhiteSpace(size) && BookApiConstants.ValidSizes.Contains(size.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsAllowedQuality(string? quality) =>
        !string.IsNullOrWhiteSpace(quality) && BookApiConstants.ValidQualities.Contains(quality.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a valid size, or the default when <paramref name="candidate"/> is null/invalid.</summary>
    public static string NormalizeSize(string? candidate, string fallback)
    {
        var f = string.IsNullOrWhiteSpace(fallback) ? "1024x1536" : fallback.Trim();
        return IsAllowedSize(candidate) ? candidate!.Trim() : f;
    }

    public static string NormalizeQuality(string? candidate, string fallback)
    {
        var f = string.IsNullOrWhiteSpace(fallback) ? "medium" : fallback.Trim();
        return IsAllowedQuality(candidate) ? candidate!.Trim() : f;
    }
}
