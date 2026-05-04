using EBookDashboard.Models;
using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models
{
    public class BookDesign
    {
        [Key]
        public int DesignId { get; set; }
        public string DesignName { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string SizeName { get; set; } = string.Empty;          // e.g. A4, A5, 6x9
        public decimal WidthMM { get; set; }
        public decimal HeightMM { get; set; }
        public string Orientation { get; set; } = string.Empty;   // Portrait / Landscape
        public int MinPages { get; set; }
        public Boolean IsActive { get; set; }
    }
}
