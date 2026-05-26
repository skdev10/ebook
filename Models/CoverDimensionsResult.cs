namespace EBookDashboard.Models;

public class CoverDimensionsResult
{
    public double SpineInches { get; set; }
    public double SpineMm { get; set; }
    public double TotalWidthInches { get; set; }
    public double TotalWidthMm { get; set; }
    public double TotalHeightInches { get; set; } = 9.25;
    public double TotalHeightMm { get; set; } = 234.95;
    public double BleedInches { get; set; } = 0.125;
    public int Pages { get; set; }
    public string PaperType { get; set; } = "White paper";
    public string? Warning { get; set; }
}
