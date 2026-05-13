using System.IO;

namespace EBookDashboard.Services.BookApi;

public static class BookApiInputValidation
{
    public static bool IsAllowedAudioExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return BookApiConstants.ValidAudioExtensions.Contains(ext, StringComparer.Ordinal);
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
