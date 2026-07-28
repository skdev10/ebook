using System.Globalization;
using System.Text;

namespace EBookDashboard.Services;

/// <summary>
/// Single source of truth for dashboard flow screens (AI Writer, Formatter, Cover Design, Publish).
/// Emits CSS custom properties consumed by <c>wwwroot/css/dashboard-flow.css</c>.
/// </summary>
public static class DashboardLayoutTokens
{
    /// <summary>Standard app canvas behind all dashboard screens (matches AI Writer lavender canvas).</summary>
    public const string PageBackground = "#f5f3ff";

    /// <summary>White card / panel surface.</summary>
    public const string SurfaceBackground = "#ffffff";

    /// <summary>Subtle border for cards and inputs.</summary>
    public const string BorderColor = "#e2e8f0";

    /// <summary>Primary violet accent (buttons, stepper).</summary>
    public const string AccentViolet = "#6d28d9";

    /// <summary>Max content width for flow screens (px).</summary>
    public const int FlowMaxWidthPx = 1600;

    /// <summary>Horizontal padding on desktop (px).</summary>
    public const int FlowPaddingDesktopPx = 48;

    /// <summary>Horizontal padding on mobile (px).</summary>
    public const int FlowPaddingMobilePx = 32;

    /// <summary>Gap between left config column and right preview (rem) — matches AI Writer.</summary>
    public const double FlowColumnGapRem = 1.0;

    /// <summary>Left form column width (AI Writer 28%).</summary>
    public const double FlowLeftColumnPercent = 28.0;

    /// <summary>Right preview column width (AI Writer 72%).</summary>
    public const double FlowRightColumnPercent = 72.0;

    /// <summary>Preview pane width as fraction of preview column (100% of right column).</summary>
    public const double PreviewPaneWidthPercent = 100.0;

    /// <summary>Vertical section gap inside forms (rem).</summary>
    public const double SectionGapRem = 1.5;

    /// <summary>Border radius for cards (px).</summary>
    public const int CardRadiusPx = 14;

    /// <summary>CSS <c>:root</c> block — inject once in <see cref="Views.Shared._DashboardLayout"/>.</summary>
    public static string BuildCssCustomProperties() =>
        string.Concat(
            ":root { ",
            "--dash-page-bg: ", PageBackground, "; ",
            "--dash-surface-bg: ", SurfaceBackground, "; ",
            "--dash-border: ", BorderColor, "; ",
            "--dash-accent: ", AccentViolet, "; ",
            "--dash-flow-max: ", FlowMaxWidthPx.ToString(CultureInfo.InvariantCulture), "px; ",
            "--dash-flow-pad: ", FlowPaddingMobilePx.ToString(CultureInfo.InvariantCulture), "px; ",
            "--dash-flow-pad-lg: ", FlowPaddingDesktopPx.ToString(CultureInfo.InvariantCulture), "px; ",
            "--dash-flow-gap: ", FlowColumnGapRem.ToString("0.##", CultureInfo.InvariantCulture), "rem; ",
            "--dash-flow-left: ", FlowLeftColumnPercent.ToString("0.##", CultureInfo.InvariantCulture), "%; ",
            "--dash-flow-right: ", FlowRightColumnPercent.ToString("0.##", CultureInfo.InvariantCulture), "%; ",
            "--dash-preview-pane-w: ", PreviewPaneWidthPercent.ToString("0.##", CultureInfo.InvariantCulture), "%; ",
            "--dash-section-gap: ", SectionGapRem.ToString("0.##", CultureInfo.InvariantCulture), "rem; ",
            "--dash-card-radius: ", CardRadiusPx.ToString(CultureInfo.InvariantCulture), "px; ",
            "} ");

    /// <summary>Full dashboard flow stylesheet header (variables + layout rules).</summary>
    public static string BuildFlowLayoutCss()
    {
        var sb = new StringBuilder(2048);
        sb.Append(BuildCssCustomProperties());
        sb.Append(".dash-flow-page { background: var(--dash-page-bg); color: #0f172a; min-height: 100%; flex: 1; display: flex; flex-direction: column; } ");
        sb.Append(".dash-flow-main { flex: 1; overflow-y: auto; overflow-x: hidden; padding: var(--dash-flow-pad); width: 100%; min-height: 0; } ");
        sb.Append("@media (min-width: 1024px) { .dash-flow-main { padding: var(--dash-flow-pad-lg); } } ");
        sb.Append(".dash-flow-inner { max-width: var(--dash-flow-max); margin-inline: auto; width: 100%; box-sizing: border-box; } ");
        sb.Append(".dash-flow-grid { display: flex; flex-direction: column; gap: var(--dash-flow-gap); width: 100%; min-height: 0; } ");
        sb.Append("@media (min-width: 1024px) { .dash-flow-grid { display: grid; grid-template-columns: minmax(0, var(--dash-flow-left)) minmax(0, var(--dash-flow-right)); align-items: stretch; } } ");
        sb.Append(".dash-flow-preview-pane { width: var(--dash-preview-pane-w); max-width: var(--dash-preview-pane-w); display: flex; flex-direction: column; align-items: stretch; box-sizing: border-box; flex: 1 1 auto; min-height: 0; } ");
        sb.Append(".dash-flow-card { background: var(--dash-surface-bg); border: 1px solid var(--dash-border); border-radius: var(--dash-card-radius); box-sizing: border-box; } ");
        sb.Append(".main-content { background: var(--dash-page-bg) !important; } ");
        return sb.ToString();
    }
}
