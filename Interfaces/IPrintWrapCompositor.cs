using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Interfaces;

/// <summary>
/// Builds a KDP full-wrap PNG by compositing the exact saved front cover bytes — never re-prompting AI for the front panel.
/// </summary>
public interface IPrintWrapCompositor
{
    /// <summary>
    /// Composes back + spine + front at KDP dimensions. Throws when front bytes are missing/invalid.
    /// </summary>
    PrintWrapComposeResult Compose(PrintWrapComposeRequest request);
}

/// <summary>Input for local print-wrap compositing.</summary>
public sealed class PrintWrapComposeRequest
{
    public required byte[] FrontCoverBytes { get; init; }
    public required string FrontCoverAssetRef { get; init; }
    public required KdpCalculateResponse Layout { get; init; }
    public required string Title { get; init; }
    public string? Author { get; init; }
    public string? Description { get; init; }
    public BookTheme? Theme { get; init; }
}

/// <summary>Composed wrap PNG plus metadata for verification.</summary>
public sealed class PrintWrapComposeResult
{
    public required byte[] PngBytes { get; init; }
    public required string FrontCoverSha256 { get; init; }
    public required int CanvasWidthPx { get; init; }
    public required int CanvasHeightPx { get; init; }
    public required int FrontPanelX { get; init; }
    public required int FrontPanelY { get; init; }
    public required int FrontPanelWidth { get; init; }
    public required int FrontPanelHeight { get; init; }
    public bool DescriptionMissing { get; init; }
}
