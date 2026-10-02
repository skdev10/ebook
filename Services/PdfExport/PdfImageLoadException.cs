namespace EBookDashboard.Services.PdfExport;

/// <summary>Raised when a print image is missing or has no pixel size. The export must not continue.</summary>
public sealed class PdfImageLoadException : InvalidOperationException
{
    public PdfImageLoadException(IReadOnlyList<string> missingFiles)
        : base(BuildMessage(missingFiles))
    {
        MissingFiles = missingFiles;
    }

    public IReadOnlyList<string> MissingFiles { get; }

    private static string BuildMessage(IReadOnlyList<string> missingFiles)
    {
        var names = missingFiles.Count == 0
            ? "(unknown image)"
            : string.Join(", ", missingFiles);
        return "PDF export stopped because these images are missing: " + names;
    }
}
