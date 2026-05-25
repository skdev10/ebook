namespace EBookDashboard.Application.Kdp.DTOs;

/// <summary>KDP paperback full-wrap dimensions matching the official Cover Calculator.</summary>
public sealed class KdpCalculateResponse
{
    public int PageCount { get; init; }
    public decimal FullCoverWidth { get; init; }
    public decimal FullCoverHeight { get; init; }
    public decimal FrontCoverWidth { get; init; }
    public decimal BackCoverWidth { get; init; }
    public decimal SpineWidth { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public int SpinePixels { get; init; }
    public decimal SafeAreaWidth { get; init; }
    public decimal SafeAreaHeight { get; init; }
    public decimal Bleed { get; init; }
    public decimal SpineMargin { get; init; }
    public decimal BarcodeMargin { get; init; }

    /// <summary>Horizontal safe-area inset total (trimWidth - safeAreaWidth).</summary>
    public decimal MarginWidth { get; init; }

    /// <summary>Vertical safe-area inset total (trimHeight - safeAreaHeight).</summary>
    public decimal MarginHeight { get; init; }

    /// <summary>Back panel X offset from left edge of full cover (inches).</summary>
    public decimal BackPanelXInches { get; init; }

    /// <summary>Spine panel X offset from left edge of full cover (inches).</summary>
    public decimal SpineXInches { get; init; }

    /// <summary>Front panel X offset from left edge of full cover (inches).</summary>
    public decimal FrontPanelXInches { get; init; }

    /// <summary>Panel top Y offset from top edge of full cover (inches).</summary>
    public decimal PanelTopYInches { get; init; }

    public string BindingType { get; init; } = "Paperback";
    public string InteriorType { get; init; } = "Standard Color";
    public string PaperType { get; init; } = "White Paper";
    public string ReadingDirection { get; init; } = "Right To Left";
    public string MeasurementUnit { get; init; } = "Inches";
    public int Dpi { get; init; }
    public bool BleedEnabled { get; init; }
    public decimal SpineInchesPerPage { get; init; }
}
