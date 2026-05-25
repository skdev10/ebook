namespace EBookDashboard.Models;

public class CoverResult
{
    public double SpineWidthInches { get; set; }
    public double FullCoverWidthInches { get; set; }
    public double FullCoverHeightInches { get; set; }
    public int FullCoverWidthPx { get; set; }
    public int FullCoverHeightPx { get; set; }
    public bool SpineTextAllowed { get; set; }
    public List<string> Warnings { get; set; } = new();

    public double SpineWidthMm => Math.Round(SpineWidthInches * 25.4, 2);
    public double FullCoverWidthMm => Math.Round(FullCoverWidthInches * 25.4, 2);
    public double FullCoverHeightMm => Math.Round(FullCoverHeightInches * 25.4, 2);
}
