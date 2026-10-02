namespace EBookDashboard.Services.PdfExport;

/// <summary>Progress hook for the background print job. Set inside the job before rendering.</summary>
public static class BookPrintProgress
{
    private static readonly AsyncLocal<Action<int, string>?> Current = new();

    public static void Bind(Action<int, string>? report) => Current.Value = report;

    public static void Report(int percent, string message) => Current.Value?.Invoke(percent, message);
}
