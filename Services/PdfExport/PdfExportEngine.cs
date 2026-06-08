using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services.PdfExport;

/// <summary>PDF export engine names — set via <c>PdfExport:Engine</c> in appsettings.</summary>
public static class PdfExportEngine
{
    public const string Chromium = "Chromium";
    public const string PdfSharp = "PdfSharp";

    /// <summary>Reserved for future wkhtmltopdf / DinkToPdf integration.</summary>
    public const string DinkToPdf = "DinkToPdf";

    public static string Resolve(IConfiguration? configuration)
    {
        var raw = (configuration?["PdfExport:Engine"] ?? Chromium).Trim();
        if (raw.Equals(PdfSharp, StringComparison.OrdinalIgnoreCase)
            || raw.Equals("PDFsharp", StringComparison.OrdinalIgnoreCase))
            return PdfSharp;
        if (raw.Equals(DinkToPdf, StringComparison.OrdinalIgnoreCase)
            || raw.Equals("WkHtmlToPdf", StringComparison.OrdinalIgnoreCase))
            return DinkToPdf;
        return Chromium;
    }

    public static bool IsPdfSharpOnly(IConfiguration? configuration) =>
        Resolve(configuration) == PdfSharp;
}
