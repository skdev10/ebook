using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Models.DTO;
using HtmlAgilityPack;

namespace EBookDashboard.Services;

public interface IEpubExportService
{
    Task<byte[]> BuildEpubAsync(
        BookDetailsResponseDto details,
        string? coverImageUrlOrData,
        string? displayTitle,
        string? displayAuthor,
        BookPdfExportOptions? exportOptions = null,
        int pageCountForCover = 0,
        string? trimSizeForCover = null,
        CancellationToken cancellationToken = default);
}

public class EpubExportService : IEpubExportService
{
    private static readonly byte[] MimetypeBytes = "application/epub+zip"u8.ToArray();

    public async Task<byte[]> BuildEpubAsync(
        BookDetailsResponseDto details,
        string? coverImageUrlOrData,
        string? displayTitle,
        string? displayAuthor,
        BookPdfExportOptions? exportOptions = null,
        int pageCountForCover = 0,
        string? trimSizeForCover = null,
        CancellationToken cancellationToken = default)
    {
        var title = WebUtility.HtmlEncode((displayTitle ?? details.BookTitle ?? "Untitled").Trim());
        if (string.IsNullOrEmpty(title)) title = "Untitled";
        var author = WebUtility.HtmlEncode((displayAuthor ?? details.AuthorName ?? "Author").Trim());
        if (string.IsNullOrEmpty(author)) author = "Author";

        var descriptionPlain = (details.Description ?? "").Trim();
        if (string.IsNullOrWhiteSpace(descriptionPlain) && details.Chapters != null)
        {
            var firstBody = details.Chapters
                .Where(c => !string.IsNullOrWhiteSpace(c.Content))
                .OrderBy(c => c.ChapterNumber)
                .Select(c => c.Content!)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstBody))
                descriptionPlain = BookManuscriptStats.Truncate(BookManuscriptStats.StripToPlain(firstBody), 500);
        }
        var descriptionMeta = string.IsNullOrWhiteSpace(descriptionPlain)
            ? ""
            : $"    <dc:description>{WebUtility.HtmlEncode(descriptionPlain)}</dc:description>\n";

        var baseCtx = BookManuscriptHtmlFormatter.CreateBaseContext(
            displayTitle ?? details.BookTitle ?? "Untitled",
            details.Subtitle,
            details.Description,
            details.Genre,
            displayAuthor ?? details.AuthorName);

        var chapters = BookChapterExportHelper.OrderForExport(details.Chapters);

        if (chapters.Count == 0)
            throw new InvalidOperationException("No chapter content to export.");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteBinaryEntry(zip, "mimetype", MimetypeBytes, CompressionLevel.NoCompression);

            WriteEntry(zip, "META-INF/container.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
                  <rootfiles>
                    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
                  </rootfiles>
                </container>
                """);

            var manifest = new StringBuilder();
            var spine = new StringBuilder();
            var navItems = new StringBuilder();
            var itemIndex = 0;

            var exportOpt = exportOptions ?? new BookPdfExportOptions();
            var interiorTpl = InteriorExportTheme.PdfBodyTemplateClass(exportOpt.InteriorStyle);
            var epubCss = InteriorExportTheme.BuildEpubStylesheet(exportOpt);

            WriteEntry(zip, "OEBPS/styles.css", epubCss);
            manifest.AppendLine("    <item id=\"styles\" href=\"styles.css\" media-type=\"text/css\"/>");

            string? coverImageId = null;
            byte[]? coverBytes = await ResolveCoverBytesAsync(coverImageUrlOrData, details.CoverImagePath, cancellationToken);
            if (coverBytes != null && coverBytes.Length > 0)
                coverBytes = CoverWrapPanelExtractor.EnsureFrontPanelBytes(coverBytes, pageCountForCover, trimSizeForCover);
            if (coverBytes != null && coverBytes.Length > 0)
            {
                var (coverHref, coverMedia) = DetectCoverAsset(coverBytes);
                coverImageId = "cover-image";
                WriteBinaryEntry(zip, $"OEBPS/{coverHref}", coverBytes);
                manifest.AppendLine($"    <item id=\"{coverImageId}\" href=\"{coverHref}\" media-type=\"{coverMedia}\" properties=\"cover-image\"/>");
                manifest.AppendLine("    <item id=\"cover-page\" href=\"cover.xhtml\" media-type=\"application/xhtml+xml\"/>");
                spine.AppendLine("    <itemref idref=\"cover-page\"/>");
                navItems.AppendLine("      <li><a href=\"cover.xhtml\">Cover</a></li>");

                var coverXhtml = $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE html>
                    <html xmlns="http://www.w3.org/1999/xhtml" xml:lang="en" lang="en">
                    <head><title>Cover</title></head>
                    <body style="margin:0;padding:0;text-align:center;">
                      <img src="{coverHref}" alt="Cover" style="max-width:100%;height:auto;"/>
                    </body></html>
                    """;
                WriteEntry(zip, "OEBPS/cover.xhtml", coverXhtml);
            }

            var narrativeOrdinal = 0;
            foreach (var ch in chapters)
            {
                itemIndex++;
                if (!BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber))
                    narrativeOrdinal++;

                var displayOrd = BookChapterExportHelper.IsFrontMatter(ch.ChapterNumber)
                    ? 0
                    : narrativeOrdinal;
                var storageNum = ch.ChapterNumber > 0 ? ch.ChapterNumber : Math.Max(1, displayOrd);
                var phNum = displayOrd > 0 ? displayOrd : 1;
                var chCtx = baseCtx.WithChapter(ch.Title ?? "", phNum, storageNum);
                var chTitleApplied = BookManuscriptHtmlFormatter.ApplyPlaceholders(ch.Title ?? "", chCtx);
                var heading = BookChapterExportHelper.GetPreviewStyleHeading(chTitleApplied, ch.ChapterNumber, phNum);
                var id = $"chapter{itemIndex}";
                var href = $"chapter{itemIndex}.xhtml";
                var chTitle = WebUtility.HtmlEncode(heading);
                var body = FormatChapterBodyXhtml(ch.Content ?? "", chCtx, heading);
                var xhtml = $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE html>
                    <html xmlns="http://www.w3.org/1999/xhtml" xml:lang="en" lang="en">
                    <head>
                      <title>{chTitle}</title>
                      <link rel="stylesheet" type="text/css" href="styles.css"/>
                    </head>
                    <body class="{interiorTpl}">
                      <h1>{chTitle}</h1>
                      {body}
                    </body></html>
                    """;
                WriteEntry(zip, $"OEBPS/{href}", xhtml);
                manifest.AppendLine($"    <item id=\"{id}\" href=\"{href}\" media-type=\"application/xhtml+xml\"/>");
                spine.AppendLine($"    <itemref idref=\"{id}\"/>");
                navItems.AppendLine($"      <li><a href=\"{href}\">{chTitle}</a></li>");
            }

            manifest.AppendLine("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
            spine.AppendLine("    <itemref idref=\"nav\" linear=\"no\"/>");

            var coverMeta = coverImageId != null
                ? $"""    <meta name="cover" content="{coverImageId}"/>"""
                : "";

            var opf = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="book-id">urn:uuid:{Guid.NewGuid()}</dc:identifier>
                    <dc:title>{title}</dc:title>
                    <dc:creator>{author}</dc:creator>
                    <dc:language>en</dc:language>
                {descriptionMeta}    <meta property="dcterms:modified">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>
                {coverMeta}
                  </metadata>
                  <manifest>
                {manifest}
                  </manifest>
                  <spine>
                {spine}
                  </spine>
                </package>
                """;
            WriteEntry(zip, "OEBPS/content.opf", opf);

            var nav = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE html>
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="en" lang="en">
                <head>
                  <title>Contents</title>
                  <link rel="stylesheet" type="text/css" href="styles.css"/>
                </head>
                <body>
                  <nav epub:type="toc" id="toc">
                    <h1>Contents</h1>
                    <ol>
                {navItems}
                    </ol>
                  </nav>
                </body></html>
                """;
            WriteEntry(zip, "OEBPS/nav.xhtml", nav);
        }

        return ms.ToArray();
    }

    private static string FormatChapterBodyXhtml(
        string content,
        BookManuscriptHtmlFormatter.PlaceholderContext ctx,
        string chapterDisplayTitle)
    {
        var html = BookManuscriptHtmlFormatter.PrepareChapterBodyForExport(content, ctx, chapterDisplayTitle);
        return ToWellFormedXhtmlFragment(html);
    }

    private static string ToWellFormedXhtmlFragment(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "<p></p>";
        try
        {
            var doc = new HtmlDocument { OptionOutputAsXml = true, OptionWriteEmptyNodes = true };
            doc.LoadHtml("<div xmlns=\"http://www.w3.org/1999/xhtml\">" + html + "</div>");
            var inner = doc.DocumentNode.SelectSingleNode("//div")?.InnerHtml ?? "";
            inner = Regex.Replace(inner, @"<br\s*>", "<br/>", RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(inner) ? "<p></p>" : inner;
        }
        catch
        {
            return $"<p>{WebUtility.HtmlEncode(html)}</p>";
        }
    }

    private static (string Href, string MediaType) DetectCoverAsset(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ("images/cover.png", "image/png");
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("images/cover.jpg", "image/jpeg");
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46)
            return ("images/cover.webp", "image/webp");
        return ("images/cover.jpg", "image/jpeg");
    }

    private static async Task<byte[]?> ResolveCoverBytesAsync(string? cover, string? fallbackPath, CancellationToken ct)
    {
        var src = (cover ?? "").Trim();
        if (string.IsNullOrEmpty(src)) src = (fallbackPath ?? "").Trim();
        if (string.IsNullOrEmpty(src)) return null;

        if (src.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            var ix = src.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (ix < 0) return null;
            try { return Convert.FromBase64String(src[(ix + 7)..]); } catch { return null; }
        }

        if (src.StartsWith("/"))
        {
            var webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var physical = Path.Combine(webRoot, src.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(physical))
                return await File.ReadAllBytesAsync(physical, ct);
        }

        if (Uri.TryCreate(src, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            return await http.GetByteArrayAsync(uri, ct);
        }

        return null;
    }

    private static void WriteEntry(ZipArchive zip, string path, string content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteBinaryEntry(ZipArchive zip, string path, byte[] content, CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }
}
