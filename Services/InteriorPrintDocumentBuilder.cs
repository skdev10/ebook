using System.Text;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>
/// Builds print HTML using the same DOM structure as Book Formatter / AI Writer preview (WYSIWYG).
/// </summary>
public static class InteriorPrintDocumentBuilder
{
    /// <summary>Compose all narrative chapter sections for PDF/print.</summary>
    public static string BuildChapterSectionsHtml(
        IReadOnlyList<ChapterDto> chapters,
        BookManuscriptHtmlFormatter.PlaceholderContext phBase,
        BookPdfExportOptions opt)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(opt.InteriorStyle);
        var bodyExtraClass = interior == "Classic" ? "classic-body" : "";
        var ordered = BookChapterExportHelper.OrderForExport(chapters);
        var sb = new StringBuilder();
        var narrativeOrdinal = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var ch = ordered[i];
            if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                narrativeOrdinal++;

            var displayNum = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber) ? 0 : narrativeOrdinal;
            var phNum = displayNum > 0 ? displayNum : 1;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            var displayHeading = BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum);
            var titleHtml = BookManuscriptHtmlFormatter.EscapeHtml(displayHeading);
            var bodyHtml = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(ch.Content, ph, displayHeading);
            var bodyClass = string.IsNullOrEmpty(bodyExtraClass) ? null : bodyExtraClass;
            var sectionId = i + 1;
            var runningHead = InteriorPageMarkup.TruncateRunningHead(phBase.BookTitle);

            sb.Append(InteriorPageMarkup.BuildChapterSection(
                sectionId, runningHead, titleHtml, bodyHtml, bodyClass));
        }

        if (sb.Length == 0)
            sb.Append(InteriorPageMarkup.BuildEmptyChapterFallback());

        return sb.ToString();
    }

    /// <summary>Maps interior style to formatter shell class on <c>body</c> (mirrors preview).</summary>
    public static string PreviewShellClass(string? interiorStyle)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(interiorStyle);
        return interior switch
        {
            "Classic" => "fmt-style-classic",
            "Modern" => "fmt-style-modern",
            "Minimalist" => "fmt-style-clean-minimalist",
            "ElegantTrade" => "fmt-style-elegant-trade",
            _ => "fmt-style-traditional"
        };
    }

    public static string PreviewInteriorWrapClass(string? interiorStyle)
    {
        var interior = InteriorExportTheme.NormalizeInteriorStyle(interiorStyle);
        return interior switch
        {
            "Classic" => "interior-classic",
            "Modern" => "interior-modern",
            "Minimalist" => "interior-minimalist",
            "ElegantTrade" => "interior-elegant-trade",
            _ => "interior-novel"
        };
    }

    public static string GoogleFontLinks() =>
        """
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
        <link href="https://fonts.googleapis.com/css2?family=Cormorant+Garamond:ital,wght@0,400;0,600;1,400&family=EB+Garamond:ital,wght@0,400;0,600;1,400&family=Inter:wght@400;600;700;800&family=Lato:wght@400;700&family=Lora:ital,wght@0,400;0,600;1,400&family=Merriweather:ital,wght@0,400;0,700;1,400&family=Playfair+Display:ital,wght@0,400;0,700;1,400&family=Source+Serif+4:ital,wght@0,400;0,600;1,400&display=swap" rel="stylesheet" />
        """;

    private static readonly (string Family, string Weight, string File)[] EmbeddedFontFiles =
    [
        ("Merriweather", "400", "Merriweather-Regular.ttf"),
        ("Merriweather", "700", "Merriweather-Bold.ttf"),
        ("Playfair Display", "700", "PlayfairDisplay-Bold.ttf"),
        ("Inter", "400", "Inter-Regular.ttf"),
        ("Inter", "700", "Inter-Bold.ttf"),
        ("Cormorant Garamond", "400", "CormorantGaramond-Regular.ttf"),
        ("Cormorant Garamond", "700", "CormorantGaramond-Bold.ttf"),
        ("Cormorant Garamond", "400 italic", "CormorantGaramond-Italic.ttf"),
        ("EB Garamond", "400", "EBGaramond-Regular.ttf"),
        ("Lora", "400", "Lora-Regular.ttf"),
    ];

    /// <summary>Embedded @font-face (offline) + Google Fonts fallback — same fonts as BookPreview.</summary>
    public static string BuildFontStylesForExport(string? webRootPath)
    {
        var sb = new StringBuilder();
        var fontDir = string.IsNullOrEmpty(webRootPath)
            ? null
            : Path.Combine(webRootPath, "fonts", "pdf");
        var embeddedAny = false;

        if (fontDir != null && Directory.Exists(fontDir))
        {
            sb.AppendLine("<style>");
            foreach (var (family, weight, file) in EmbeddedFontFiles)
            {
                var path = Path.Combine(fontDir, file);
                if (!File.Exists(path)) continue;
                var info = new FileInfo(path);
                if (info.Length > 900_000) continue;

                var bytes = File.ReadAllBytes(path);
                var b64 = Convert.ToBase64String(bytes);
                var isItalic = weight.Contains("italic", StringComparison.OrdinalIgnoreCase);
                var weightNum = weight.Contains("700") ? "700" : "400";
                var style = isItalic ? "italic" : "normal";
                sb.AppendLine(FormattableString.Invariant(
                    $"@font-face {{ font-family: '{family}'; font-style: {style}; font-weight: {weightNum}; src: url(data:font/ttf;base64,{b64}) format('truetype'); font-display: swap; }}"));
                embeddedAny = true;
            }
            sb.AppendLine("</style>");
        }

        return embeddedAny ? sb.ToString() + GoogleFontLinks() : GoogleFontLinks();
    }
}
