using System.Globalization;

namespace EBookDashboard.Services;

/// <summary>
/// Centralized human-friendly date/time formatting for user-facing displays
/// (e.g. "June 18, 2026 at 4:37 PM"). Machine-readable exports (CSV, packaging
/// metadata) intentionally keep ISO formats and must not use this helper.
/// </summary>
public static class FriendlyDateFormatter
{
    private const string Pattern = "MMMM d, yyyy 'at' h:mm tt";

    /// <summary>Formats a date/time as e.g. "June 18, 2026 at 4:37 PM".</summary>
    public static string Format(DateTime value)
    {
        return value.ToString(Pattern, CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a nullable date/time, returning <paramref name="fallback"/> when null/default.</summary>
    public static string Format(DateTime? value, string fallback = "—")
    {
        if (!value.HasValue || value.Value == default)
            return fallback;
        return Format(value.Value);
    }
}
