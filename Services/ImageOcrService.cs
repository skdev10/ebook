using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Tesseract;

namespace EBookDashboard.Services;

/// <summary>
/// Lightweight OCR for Writer: extract text from a scanned page / screenshot image.
/// Uses Tesseract with eng.traineddata under /tessdata (or ContentRoot/tessdata).
/// </summary>
public interface IImageOcrService
{
    /// <summary>Returns extracted plain text, or empty when nothing readable.</summary>
    Task<string> ExtractTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}

public sealed class ImageOcrService : IImageOcrService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ImageOcrService> _logger;
    private readonly string _tessDataPath;

    public ImageOcrService(IWebHostEnvironment env, ILogger<ImageOcrService> logger)
    {
        _env = env;
        _logger = logger;
        _tessDataPath = ResolveTessDataPath(env);
    }

    public Task<string> ExtractTextAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        if (imageBytes == null || imageBytes.Length == 0)
            return Task.FromResult(string.Empty);

        if (string.IsNullOrEmpty(_tessDataPath) || !File.Exists(Path.Combine(_tessDataPath, "eng.traineddata")))
        {
            _logger.LogWarning("OCR skipped — eng.traineddata not found under tessdata.");
            return Task.FromResult(string.Empty);
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(img);
                var text = page.GetText() ?? "";
                text = System.Text.RegularExpressions.Regex.Replace(text, @"[ \t]+\r?\n", "\n");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");
                return text.Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tesseract OCR failed");
                return string.Empty;
            }
        }, cancellationToken);
    }

    private static string ResolveTessDataPath(IWebHostEnvironment env)
    {
        var candidates = new[]
        {
            Path.Combine(env.ContentRootPath, "tessdata"),
            Path.Combine(AppContext.BaseDirectory, "tessdata"),
            Path.Combine(Directory.GetCurrentDirectory(), "tessdata")
        };
        foreach (var c in candidates)
        {
            if (Directory.Exists(c) && File.Exists(Path.Combine(c, "eng.traineddata")))
                return c;
        }
        return candidates[0];
    }
}
