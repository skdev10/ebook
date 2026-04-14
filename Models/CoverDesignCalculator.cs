using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("coverdesigncalculator")]
    public class CoverDesignCalculator
    {
        [Key]
        public int CoverId { get; set; }
        public int? UserId { get; set; }
        public int? BookId { get; set; }
        public string? BookDetails { get; set; } = string.Empty;
        public string? BindingType { get; set; } = string.Empty;
        public string? InteriorType { get; set; } = string.Empty;
        public string? PaperType { get; set; } = string.Empty;
        public string? PageTurnDirection  { get; set; } = string.Empty;
        public string? MeasurementUnits { get; set; } = string.Empty;
        public string? TrimSize { get; set; } = string.Empty;
        public int PagesCount { get; set; }
        public string? CoverTemplate { get; set; } = string.Empty;
        public string? CoverTemplateImagePath { get; set; } = string.Empty;
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal SafeArea { get; set; }
        public decimal SpineWidth { get; set; }
        public string? SpineTitle { get; set; } = string.Empty;
        public string? SpineDescription { get; set; } = string.Empty;
        public string? BookVolume { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? Status { get; set; } = string.Empty;
        public bool isActive  { get; set; }=true;
    }
}
