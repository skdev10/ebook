namespace EBookDashboard.Models;

/// <summary>Controls whether mobile/tablet browsers may access user-facing HTML pages.</summary>
public class MobileAccessOptions
{
    public const string SectionName = "MobileAccess";

    /// <summary>When true, mobile/tablet visitors see a desktop-only SweetAlert (client) and optional server block page.</summary>
    public bool BlockMobile { get; set; } = true;

    /// <summary>When true, middleware returns a standalone HTML block page. When false, pages load and SweetAlert handles the UX.</summary>
    public bool UseServerBlockPage { get; set; }

    /// <summary>Block screen heading (configurable without code changes).</summary>
    public string Title { get; set; } = "Desktop Only";

    /// <summary>Primary message shown on the block screen.</summary>
    public string Message { get; set; } =
        "This application is currently not available on mobile devices.";

    /// <summary>Secondary guidance shown below the primary message.</summary>
    public string SubMessage { get; set; } =
        "Kindly open this application on a desktop or laptop browser to continue.";

    /// <summary>Viewport max-width (px) for the client-side overlay safeguard.</summary>
    public int ClientOverlayMaxWidthPx { get; set; } = 768;
}
