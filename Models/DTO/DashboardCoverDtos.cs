using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace EBookDashboard.Models.DTO
{
    /// <summary>POST /Dashboard/GenerateCover — AI cover generation from Cover Design page.</summary>
    public class DashboardGenerateCoverRequest
    {
        [Required]
        public int BookId { get; set; }

        /// <summary>Full structured prompt (for logs / future use). Image direction still sent in Description.</summary>
        public string? Prompt { get; set; }

        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Genre { get; set; }
        public string? Style { get; set; }
        public string? Description { get; set; }
    }

    /// <summary>POST /Dashboard/EditCover — refine cover from current image + new direction.</summary>
    public class DashboardEditCoverRequest
    {
        /// <summary>Optional — when set, last preview URL is saved under book:{id}:aiCoverLastPreview.</summary>
        public int BookId { get; set; }

        [Required]
        [JsonPropertyName("encoded_image")]
        public string EncodedImage { get; set; } = "";

        [JsonPropertyName("image_direction")]
        public string? ImageDirection { get; set; }

        public string? Size { get; set; }
    }

    /// <summary>POST /Dashboard/DownloadBookPdf — full book PDF with cover (data URL or server path) and formatted chapters.</summary>
    public class ExportBookPdfRequest
    {
        [Required]
        public int BookId { get; set; }

        /// <summary>PNG/JPEG data URL from live cover preview (<c>html2canvas</c>) so the PDF matches on-screen design.</summary>
        public string? CoverImageDataUrl { get; set; }

        public string? DisplayTitle { get; set; }
        public string? DisplayAuthor { get; set; }
        public string? DisplayGenre { get; set; }

        /// <summary>Optional overrides; when null, server loads saved formatter draft from <c>Settings</c>.</summary>
        [JsonPropertyName("interiorStyle")]
        public string? InteriorStyle { get; set; }
        [JsonPropertyName("textSize")]
        public string? TextSize { get; set; }
        [JsonPropertyName("lineSpacing")]
        public string? LineSpacing { get; set; }
        [JsonPropertyName("bookFormat")]
        public string? BookFormat { get; set; }
        [JsonPropertyName("publishingPlatform")]
        public string? PublishingPlatform { get; set; }
        [JsonPropertyName("publishingPlatforms")]
        public string? PublishingPlatforms { get; set; }

        /// <summary>Formatter accent color (#RRGGBB) from preview studio.</summary>
        [JsonPropertyName("previewAccent")]
        public string? PreviewAccent { get; set; }

        /// <summary>Ebook export format: <c>epub</c> (default) or <c>docx</c> (Word document).</summary>
        public string? ExportFormat { get; set; }
    }

    /// <summary>POST /Dashboard/GeneratePrintReadyCover — auto-generate back+spine+front cover set.</summary>
    public class PrintReadyCoverRequest
    {
        [Required]
        public int BookId { get; set; }

        public int? PageCount { get; set; }
        public string? TrimSize { get; set; }
        public string? CoverStyle { get; set; }
        public string? Size { get; set; }
        public string? Quality { get; set; }
        public string? Title { get; set; }
        public string? Author { get; set; }
        public string? Genre { get; set; }
        public string? Style { get; set; }
        public string? Description { get; set; }

        /// <summary>Paperback (KDP) or Hardcover (case-bound: 1" wrap + 3/8" hinges).</summary>
        public string? BindingType { get; set; }
    }

    /// <summary>POST /Dashboard/SavePrintReadyComposedWrap — persist client-side KDP-calibrated wrap PNG.</summary>
    public class SavePrintReadyWrapRequest
    {
        [Required]
        public int BookId { get; set; }

        public string? WrapImageDataUrl { get; set; }
        public string? WrapImageBase64 { get; set; }
        public int? PageCount { get; set; }
        public double? SpineInches { get; set; }
    }
}
