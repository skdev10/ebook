using PdfSharp.Fonts;

namespace EBookDashboard.Services.PdfExport;

/// <summary>Embeds TTF fonts from wwwroot/fonts/pdf for Linux/production PDF export.</summary>
public sealed class ExportPdfFontResolver : IFontResolver
{
    private readonly string _fontDir;
    private static readonly object InitLock = new();
    private static bool _registered;

    private static readonly Dictionary<string, string> FaceFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["merriweather#regular"] = "Merriweather-Regular.ttf",
        ["merriweather#bold"] = "Merriweather-Bold.ttf",
        ["merriweather#italic"] = "Merriweather-Italic.ttf",
        ["playfair display#regular"] = "PlayfairDisplay-Regular.ttf",
        ["playfair display#bold"] = "PlayfairDisplay-Bold.ttf",
        ["playfair display#italic"] = "PlayfairDisplay-Italic.ttf",
        ["inter#regular"] = "Inter-Regular.ttf",
        ["inter#bold"] = "Inter-Bold.ttf",
        ["cormorant garamond#regular"] = "CormorantGaramond-Regular.ttf",
        ["cormorant garamond#bold"] = "CormorantGaramond-Bold.ttf",
        ["cormorant garamond#italic"] = "CormorantGaramond-Italic.ttf",
        ["eb garamond#regular"] = "EBGaramond-Regular.ttf",
        ["eb garamond#bold"] = "EBGaramond-Bold.ttf",
        ["lora#regular"] = "Lora-Regular.ttf",
        ["lora#bold"] = "Lora-Bold.ttf",
        ["dejavu sans#regular"] = "DejaVuSans.ttf",
        ["dejavu sans#bold"] = "DejaVuSans-Bold.ttf",
    };

    public ExportPdfFontResolver(string fontDirectory) =>
        _fontDir = fontDirectory;

    public static void EnsureRegistered(string fontDirectory)
    {
        if (_registered) return;
        lock (InitLock)
        {
            if (_registered) return;
            if (GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver = new ExportPdfFontResolver(fontDirectory);
            _registered = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var key = $"{familyName.Trim().ToLowerInvariant()}#{(isBold ? "bold" : isItalic ? "italic" : "regular")}";
        if (FaceFiles.ContainsKey(key) && File.Exists(Path.Combine(_fontDir, FaceFiles[key])))
            return new FontResolverInfo(key, isBold, isItalic);

        var regularKey = $"{familyName.Trim().ToLowerInvariant()}#regular";
        if (FaceFiles.ContainsKey(regularKey) && File.Exists(Path.Combine(_fontDir, FaceFiles[regularKey])))
            return new FontResolverInfo(regularKey, isBold, isItalic);

        return PlatformFontResolver.ResolveTypeface(familyName, isBold, isItalic);
    }

    public byte[]? GetFont(string faceName)
    {
        if (!FaceFiles.TryGetValue(faceName, out var file)) return null;
        var path = Path.Combine(_fontDir, file);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
