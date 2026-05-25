using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Application.Kdp.DTOs;

/// <summary>Request body for POST /api/kdp/calculate.</summary>
public sealed class KdpCalculateRequest
{
    [Range(1, 10000)]
    public int PageCount { get; set; } = 24;

    [Range(0.1, 20)]
    public decimal TrimWidth { get; set; } = Kdp.Constants.KdpPaperbackConstants.DefaultTrimWidthInches;

    [Range(0.1, 20)]
    public decimal TrimHeight { get; set; } = Kdp.Constants.KdpPaperbackConstants.DefaultTrimHeightInches;

    [Range(72, 600)]
    public int Dpi { get; set; } = Kdp.Constants.KdpPaperbackConstants.DefaultDpi;

    public bool Bleed { get; set; } = true;

    /// <summary>Optional override; defaults to Standard Color.</summary>
    public string? InteriorType { get; set; }

    /// <summary>Optional override; defaults to White Paper.</summary>
    public string? PaperType { get; set; }
}
