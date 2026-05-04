using Humanizer;

namespace EBookDashboard.Models.DTO
{
    public class CoverDesignSaveDto
    {
        public int BookId { get; set; }

        public string BindingType { get; set; } = string.Empty;
        public string InteriorType { get; set; } = string.Empty;
        public string PaperType { get; set; } = string.Empty;
        public string MeasurementUnits { get; set; } = string.Empty;
        public string TrimSize { get; set; } = string.Empty;
        public string PageTurnDirection { get; set; } = string.Empty;

        public int PagesCount { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal SpineWidth { get; set; }
        public decimal SafeArea { get; set; }
    }
}

