using EBookDashboard.Models;

namespace EBookDashboard.Models.ViewModels
{
    public class CoverDesignCalculatorVM
    {
        // Book info
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string? Description { get; set; }

        // Cover Design Data
        public CoverDesignCalculator? CoverDesignData { get; set; }

        // Form Properties for binding
        public string BindingType { get; set; } = "paperback";
        public string InteriorType { get; set; } = "bw";
        public string PaperType { get; set; } = "white";
        public string MeasurementUnits { get; set; } = "inches";
        public string TrimSize { get; set; } = "6x9";

        public int PagesCount { get; set; }
        public decimal Width { get; set; }
        public decimal Height { get; set; }
        public decimal SpineWidth { get; set; }
        public decimal SafeArea { get; set; }
        public string? SpineTitle { get; set; }
        public string? SpineDescription { get; set; }

        // Other properties if needed
        public int UserId { get; set; }

        /// <summary>Options for Book Cover Design Template dropdown (from bookcoverpages table).</summary>
        public List<BookCoverPages> BookCoverPages { get; set; } = new();

        // BookFormatting fields (Ebook/Print/Both, Novel/Non-Fiction/Children's/Comic, etc.)
        public string Format { get; set; } = "Ebook";
        public string InteriorStyle { get; set; } = "Novel";
        public string TextSize { get; set; } = "Medium";
        public string LineSpacing { get; set; } = "1.6";
        public string? PublishingPlatform { get; set; }
        public string? PublishingPlatforms { get; set; }
        public List<BookDropdownItem> UserBooks { get; set; } = new();
    }
}
