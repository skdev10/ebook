namespace EBookDashboard.Models;

/// <summary>Stable catalog keys. Admin cannot create or delete these rows.</summary>
public static class PricingKeys
{
    public const string WritingPerPage = "writing.per_page";
    public const string CoverCustomImage = "cover.custom_image";
    public const string FormattingPremium = "formatting.premium";
    public const string ExportPaperback = "export.paperback";
    public const string ExportHardcover = "export.hardcover";
    public const string ExportEbook = "export.ebook";
}
