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
        var ordered = BookHtmlNormalizer.OmitDuplicateFrontMatter(
            BookChapterExportHelper.OrderForExport(chapters),
            opt.KeepOriginalCopyrightPage,
            phBase.BookTitle);
        var sb = new StringBuilder();
        var narrativeOrdinal = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var ch = ordered[i];
            if (InteriorFrontMatterBuilder.IsImportedContentsChapter(ch.Title))
                continue;

            var unnumbered = BookChapterExportHelper.IsUnnumberedSection(ch.ChapterNumber, ch.Title);
            if (!unnumbered)
                narrativeOrdinal++;

            var displayNum = unnumbered ? 0 : narrativeOrdinal;
            var phNum = displayNum > 0 ? displayNum : 1;
            var ph = phBase.WithChapter(ch.Title ?? "", phNum, ch.ChapterNumber > 0 ? ch.ChapterNumber : phNum);
            var chTitleRaw = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", ph);
            var displayHeading = BookChapterExportHelper.GetPreviewStyleHeading(chTitleRaw, ch.ChapterNumber, phNum);
            var numberStyle = BookPdfExportOptions.NormalizeChapterNumberStyle(opt.ChapterNumberStyle);
            var titleHtml = unnumbered
                ? BookManuscriptHtmlFormatter.EscapeHtml(displayHeading)
                : InteriorPageMarkup.BuildFormatterChapterTitleHtml(displayHeading, phNum, interior, numberStyle);
            var bodyHtml = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(ch.Content, ph, displayHeading);
            if (BookHtmlNormalizer.AllowsDropCap(ch.Title))
                bodyHtml = BookHtmlNormalizer.MarkDropCapParagraph(bodyHtml);
            var bodyClass = string.IsNullOrEmpty(bodyExtraClass) ? null : bodyExtraClass;
            var sectionId = i + 1;
            var runningHead = InteriorPageMarkup.TruncateRunningHead(phBase.BookTitle);
            var blockClass = BookHtmlNormalizer.AllowsDropCap(ch.Title) ? "has-drop-cap" : null;
            var sectionClass = BookChapterExportHelper.IsFrontMatterSectionTitle(ch.Title)
                ? "front-matter-flow"
                : null;

            sb.Append(InteriorPageMarkup.BuildChapterSection(
                sectionId, runningHead, titleHtml, bodyHtml, bodyClass, blockClass, sectionClass));
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
            "FineBook" => "fmt-style-classic",
            "Modern" => "fmt-style-modern",
            "Contemporary" => "fmt-style-modern",
            "Minimalist" => "fmt-style-clean-minimalist",
            "Clean" => "fmt-style-clean-minimalist",
            "ElegantTrade" => "fmt-style-elegant-trade",
            "ElegantTradePOD" => "fmt-style-elegant-trade",
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
            "Traditional" => "interior-traditional",
            "Contemporary" => "interior-contemporary",
            "FineBook" => "interior-fine-book",
            "Clean" => "interior-clean",
            "POD" => "interior-pod",
            "ElegantTradePOD" => "interior-elegant-trade-pod",
            _ => "interior-novel"
        };
    }

    public static string GoogleFontLinks() =>
        """
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
        <link href="https://fonts.googleapis.com/css2?family=Cormorant+Garamond:ital,wght@0,400;0,600;1,400&family=Cormorant+SC:wght@400;600&family=Crimson+Pro:ital,wght@0,400;0,600;1,400&family=DM+Sans:ital,wght@0,400;0,500;0,700;1,400&family=EB+Garamond:ital,wght@0,400;0,600;0,700;1,400&family=Inter:wght@400;600;700;800&family=Jost:wght@300;400;600&family=Lato:wght@400;700&family=Libre+Baskerville:ital,wght@0,400;0,700;1,400&family=Lora:ital,wght@0,400;0,600;1,400&family=Merriweather:ital,wght@0,400;0,700;1,400&family=Nunito+Sans:ital,wght@0,400;0,700;1,400&family=Outfit:wght@400;600;700&family=Playfair+Display:ital,wght@0,400;0,700;1,400&family=Raleway:wght@400;600;700&family=Source+Serif+4:ital,wght@0,400;0,600;1,400&family=Spectral:ital,wght@0,400;0,600;1,400&display=swap" rel="stylesheet" />
        """;

    private static readonly (string Family, string Weight, string File)[] EmbeddedFontFiles =
    [
        ("Merriweather", "400", "Merriweather-Regular.ttf"),
        ("Merriweather", "700", "Merriweather-Bold.ttf"),
        ("Playfair Display", "700", "PlayfairDisplay-Bold.ttf"),
        ("Inter", "400", "Inter-Regular.ttf"),
        ("Inter", "700", "Inter-Regular.ttf"),
        ("Cormorant Garamond", "400", "CormorantGaramond-Regular.ttf"),
        ("Cormorant Garamond", "700", "CormorantGaramond-Bold.ttf"),
        ("Cormorant Garamond", "400 italic", "CormorantGaramond-Italic.ttf"),
        ("Cormorant SC", "600", "CormorantSC-SemiBold.ttf"),
        ("Cormorant SC", "400", "CormorantSC-SemiBold.ttf"),
        ("EB Garamond", "400", "EBGaramond-Regular.ttf"),
        ("EB Garamond", "700", "EBGaramond-Bold.ttf"),
        ("Lora", "400", "Lora-Regular.ttf"),
        ("Libre Baskerville", "400", "LibreBaskerville-Regular.ttf"),
        ("Libre Baskerville", "700", "LibreBaskerville-Bold.ttf"),
        ("Libre Baskerville", "400 italic", "LibreBaskerville-Italic.ttf"),
    ];

    /// <summary>Embedded @font-face (offline). Print PDFs must use static TrueType/OpenType — never variable/WOFF (Type 3).</summary>
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
                // Skip huge packs and variable fonts (fvar) — Chromium embeds those as Type 3.
                if (info.Length > 3_000_000) continue;
                var bytes = File.ReadAllBytes(path);
                if (FontBytesLookVariable(bytes)) continue;

                var b64 = Convert.ToBase64String(bytes);
                var isItalic = weight.Contains("italic", StringComparison.OrdinalIgnoreCase);
                var weightNum = weight.Contains("700") ? "700" : weight.Contains("600") ? "600" : "400";
                var style = isItalic ? "italic" : "normal";
                var format = path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ? "opentype" : "truetype";
                var mime = format == "opentype" ? "font/otf" : "font/ttf";
                sb.AppendLine(FormattableString.Invariant(
                    $"@font-face {{ font-family: '{family}'; font-style: {style}; font-weight: {weightNum}; src: url(data:{mime};base64,{b64}) format('{format}'); font-display: block; }}"));
                embeddedAny = true;
            }
            sb.AppendLine("</style>");
        }

        // Never append Google Fonts for print — WOFF/variable often embed as Type 3.
        if (!embeddedAny)
        {
            sb.AppendLine("<style>");
            sb.AppendLine("body, .book-pdf-body { font-family: 'Times New Roman', Georgia, 'Libre Baskerville', serif; }");
            sb.AppendLine("</style>");
        }

        return sb.ToString();
    }

    /// <summary>True when a TTF/OTF contains an <c>fvar</c> table (variable font → Type 3 in Chromium/Skia).</summary>
    internal static bool FontBytesLookVariable(byte[] bytes)
    {
        if (bytes == null || bytes.Length < 12)
            return false;
        // sfnt: offset 4 = numTables (ushort BE)
        var numTables = (bytes[4] << 8) | bytes[5];
        if (numTables <= 0 || numTables > 64)
            return false;
        for (var i = 0; i < numTables; i++)
        {
            var off = 12 + i * 16;
            if (off + 4 > bytes.Length)
                break;
            // Tag is 4 ASCII chars
            if (bytes[off] == (byte)'f' && bytes[off + 1] == (byte)'v'
                && bytes[off + 2] == (byte)'a' && bytes[off + 3] == (byte)'r')
                return true;
        }
        return false;
    }
}
