using Microsoft.Extensions.Configuration;
using PuppeteerSharp;

namespace EBookDashboard.Services.PdfExport;

/// <summary>Shared headless Chromium executable resolution for PDF export and TOC measurement.</summary>
internal static class ChromiumLaunchHelper
{
    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static bool _fetched;

    private static readonly string[] LinuxBrowserCandidates =
    [
        "/usr/bin/google-chrome-stable",
        "/usr/bin/google-chrome",
        "/usr/bin/chromium-browser",
        "/usr/bin/chromium",
        "/snap/bin/chromium"
    ];

    /// <summary>Resolves Chrome/Edge path or downloads bundled Chromium.</summary>
    public static async Task<string?> ResolveExecutableAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var configured = configuration["Puppeteer:ExecutablePath"]
            ?? Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        if (OperatingSystem.IsLinux())
        {
            foreach (var candidate in LinuxBrowserCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        foreach (var win in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
                 })
        {
            if (File.Exists(win)) return win;
        }

        await EnsureChromiumAsync(cancellationToken);
        return null;
    }

    private static async Task EnsureChromiumAsync(CancellationToken cancellationToken)
    {
        if (_fetched) return;
        await FetchLock.WaitAsync(cancellationToken);
        try
        {
            if (_fetched) return;
            var bf = new BrowserFetcher();
            await bf.DownloadAsync();
            _fetched = true;
        }
        finally
        {
            FetchLock.Release();
        }
    }
}
