namespace EBookDashboard.Models;

public class CoverRequest
{
    public BindingType Binding { get; set; } = BindingType.Paperback;
    public PaperType Paper { get; set; } = PaperType.White;
    public double TrimWidth { get; set; }
    public double TrimHeight { get; set; }
    public int PageCount { get; set; }
    public Unit Units { get; set; } = Unit.Inches;
    public ReadingDirection Direction { get; set; } = ReadingDirection.LeftToRight;
}
