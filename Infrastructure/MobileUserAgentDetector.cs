namespace EBookDashboard.Infrastructure;

/// <summary>Detects mobile and tablet browsers from request headers.</summary>
public static class MobileUserAgentDetector
{
    private static readonly string[] MobileSubstrings =
    [
        "android", "iphone", "ipod", "ipad", "windows phone", "iemobile",
        "mobile", "blackberry", "bb10", "opera mini", "webos", "kindle", "silk",
        "tablet", "playbook", "fennec"
    ];

    /// <summary>Returns true when the request likely originates from a phone or tablet browser.</summary>
    public static bool IsMobileOrTablet(string? userAgent, string? clientHintMobile)
    {
        if (string.Equals(clientHintMobile?.Trim(), "?1", StringComparison.Ordinal))
            return true;

        if (string.IsNullOrWhiteSpace(userAgent))
            return false;

        var ua = userAgent.AsSpan();

        foreach (var token in MobileSubstrings)
        {
            if (ua.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // iPadOS 13+ may report as Macintosh; the "Mobile/" token distinguishes iPad from Mac Safari.
        if (ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)
            && ua.Contains("Mobile/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
