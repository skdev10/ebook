using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

public interface IEpubExportService
{
    Task<byte[]> BuildEpubAsync(
        BookDetailsResponseDto details,
        string? coverImageUrlOrData,
        string? displayTitle,
        string? displayAuthor,
        CancellationToken cancellationToken = default);
}

public class EpubExportService : IEpubExportService
{
    public async Task<byte[]> BuildEpubAsync(
        BookDetailsResponseDto details,
        string? coverImageUrlOrData,
        string? displayTitle,
        string? displayAuthor,
        CancellationToken cancellationToken = default)
    {
        var title = WebUtility.HtmlEncode((displayTitle ?? details.BookTitle ?? "Untitled").Trim());
        if (string.IsNullOrEmpty(title)) title = "Untitled";
        var author = WebUtility.HtmlEncode((displayAuthor ?? details.AuthorName ?? "Author").Trim());
        if (string.IsNullOrEmpty(author)) author = "Author";

        var chapters = (details.Chapters ?? new List<ChapterDto>())
            .OrderBy(c => c.ChapterNumber > 0 ? c.ChapterNumber : int.MaxValue)
            .ToList();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);

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

            string? coverHref = null;
            byte[]? coverBytes = await ResolveCoverBytesAsync(coverImageUrlOrData, details.CoverImagePath, cancellationToken);
            if (coverBytes != null && coverBytes.Length > 0)
            {
                coverHref = "images/cover.jpg";
                WriteBinaryEntry(zip, $"OEBPS/{coverHref}", coverBytes);
                manifest.AppendLine($"    <item id=\"cover-image\" href=\"{coverHref}\" media-type=\"image/jpeg\" properties=\"cover-image\"/>");
                manifest.AppendLine("    <item id=\"cover-page\" href=\"cover.xhtml\" media-type=\"application/xhtml+xml\"/>");
                spine.AppendLine("    <itemref idref=\"cover-page\"/>");
                navItems.AppendLine("      <li><a href=\"cover.xhtml\">Cover</a></li>");

                var coverXhtml = $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE html>
                    <html xmlns="http://www.w3.org/1999/xhtml" lang="en">
                    <head><title>Cover</title></head>
                    <body style="margin:0;padding:0;text-align:center;">
                      <img src="{coverHref}" alt="Cover" style="max-width:100%;height:auto;"/>
                    </body></html>
                    """;
                WriteEntry(zip, "OEBPS/cover.xhtml", coverXhtml);
            }

            foreach (var ch in chapters)
            {
                if (string.IsNullOrWhiteSpace(ch.Content)) continue;
                itemIndex++;
                var id = $"chapter{itemIndex}";
                var href = $"chapter{itemIndex}.xhtml";
                var chTitle = WebUtility.HtmlEncode((ch.Title ?? $"Chapter {itemIndex}").Trim());
                var body = FormatChapterBody(ch.Content);
                var xhtml = $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE html>
                    <html xmlns="http://www.w3.org/1999/xhtml" lang="en">
                    <head><title>{chTitle}</title></head>
                    <body>
                      <h1>{chTitle}</h1>
                      {body}
                    </body></html>
                    """;
                WriteEntry(zip, $"OEBPS/{href}", xhtml);
                manifest.AppendLine($"    <item id=\"{id}\" href=\"{href}\" media-type=\"application/xhtml+xml\"/>");
                spine.AppendLine($"    <itemref idref=\"{id}\"/>");
                navItems.AppendLine($"      <li><a href=\"{href}\">{chTitle}</a></li>");
            }

            var opf = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="book-id">urn:uuid:{Guid.NewGuid()}</dc:identifier>
                    <dc:title>{title}</dc:title>
                    <dc:creator>{author}</dc:creator>
                    <dc:language>en</dc:language>
                    <meta property="dcterms:modified">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>
                  </metadata>
                  <manifest>
                    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
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
                <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" lang="en">
                <head><title>Navigation</title></head>
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

    private static string FormatChapterBody(string content)
    {
        var s = (content ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return "<p></p>";
        if (Regex.IsMatch(s, @"<\s*[a-z]", RegexOptions.IgnoreCase))
            return s;
        var paras = s.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var p in paras)
        {
            var t = WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br/>");
            if (!string.IsNullOrEmpty(t)) sb.Append("<p>").Append(t).Append("</p>");
        }
        return sb.Length > 0 ? sb.ToString() : $"<p>{WebUtility.HtmlEncode(s)}</p>";
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
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void WriteBinaryEntry(ZipArchive zip, string path, byte[] content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }
}
