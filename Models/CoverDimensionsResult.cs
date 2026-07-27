namespace EBookDashboard.Models;

public class CoverDimensionsResult
{
    public double SpineInches { get; set; }
    public double SpineMm { get; set; }
    public double TotalWidthInches { get; set; }
    public double TotalWidthMm { get; set; }
    public double TotalHeightInches { get; set; }
    public double TotalHeightMm { get; set; }
    public double BleedInches { get; set; }
    public int Pages { get; set; }
    public string PaperType { get; set; } = "White paper";
    public string? Warning { get; set; }
}
