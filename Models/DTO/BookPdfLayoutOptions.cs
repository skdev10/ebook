using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Models.DTO;

/// <summary>KDP 6×9 margin overrides — optional via <c>PdfExport:Margins</c> in appsettings.</summary>
public sealed class BookPdfLayoutOptions
{
    public string? MarginTop { get; set; }
    public string? MarginBottom { get; set; }
    /// <summary>Inside / gutter (left on recto pages).</summary>
    public string? MarginInside { get; set; }
    /// <summary>Outside edge.</summary>
    public string? MarginOutside { get; set; }

    public static BookPdfLayoutOptions FromConfiguration(IConfiguration? configuration)
    {
        var section = configuration?.GetSection("PdfExport:Margins:Trim6x9");
        if (section == null || !section.Exists()) return new BookPdfLayoutOptions();
        return new BookPdfLayoutOptions
        {
            MarginTop = section["Top"],
            MarginBottom = section["Bottom"],
            MarginInside = section["Inside"],
            MarginOutside = section["Outside"]
        };
    }
}
