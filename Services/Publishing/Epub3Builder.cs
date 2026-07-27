using System.IO.Compression;
using System.Net;
using System.Text;
using EBookDashboard.Models;

namespace EBookDashboard.Services.Publishing;

/// <summary>
/// Builds a validated EPUB 3 package from a <see cref="Project"/> + active <see cref="ManuscriptVersion"/>:
/// mimetype first and stored (uncompressed), META-INF/container.xml, content.opf, nav.xhtml, toc.ncx,
/// and one XHTML document per top-level section (chapter). Structure is verified before returning bytes.
/// </summary>
public sealed class Epub3Builder
{
    private static readonly byte[] MimetypeBytes = "application/epub+zip"u8.ToArray();

    public byte[] Build(
        Project project,
        ManuscriptVersion version,
        LayoutProfile ebookProfile,
        byte[]? coverImageBytes = null,
        string? coverContentType = null)
    {
        var sections = version.Sections
            .Where(s => s.ParentSectionId == null)
            .OrderBy(s => s.OrderIndex)
            .ToList();
        if (sections.Count == 0)
            throw new InvalidOperationException("This manuscript has no content to export yet.");

        var childrenByParent = version.Sections
            .Where(s => s.ParentSectionId != null)
            .ToLookup(s => s.ParentSectionId!.Value);

        var title = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(project.Title) ? "Untitled" : project.Title.Trim());
        var author = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(project.AuthorName) ? "Unknown Author" : project.AuthorName.Trim());
        var bookUid = $"urn:uuid:{Guid.NewGuid()}";

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

            WriteEntry(zip, "OEBPS/styles.css", BuildStylesheet(ebookProfile));

            var manifest = new StringBuilder();
            var spine = new StringBuilder();
            var navItems = new StringBuilder();
            var ncxNavPoints = new StringBuilder();
            manifest.AppendLine("    <item id=\"styles\" href=\"styles.css\" media-type=\"text/css\"/>");

            string? coverImageId = null;
            if (coverImageBytes is { Length: > 0 })
            {
                var (href, media) = DetectImageAsset(coverImageBytes, coverContentType);
                coverImageId = "cover-image";
                WriteBinaryEntry(zip, $"OEBPS/{href}", coverImageBytes);
                manifest.AppendLine($"    <item id=\"{coverImageId}\" href=\"{href}\" media-type=\"{media}\" properties=\"cover-image\"/>");
                manifest.AppendLine("    <item id=\"cover-page\" href=\"cover.xhtml\" media-type=\"application/xhtml+xml\"/>");
                spine.AppendLine("    <itemref idref=\"cover-page\"/>");
                navItems.AppendLine("      <li><a href=\"cover.xhtml\">Cover</a></li>");
                ncxNavPoints.AppendLine("    <navPoint id=\"navpoint-cover\" playOrder=\"1\"><navLabel><text>Cover</text></navLabel><content src=\"cover.xhtml\"/></navPoint>");
                WriteEntry(zip, "OEBPS/cover.xhtml", $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE html>
                    <html xmlns="http://www.w3.org/1999/xhtml" xml:lang="en" lang="en">
                    <head><title>Cover</title></head>
                    <body style="margin:0;padding:0;text-align:center;">
                      <img src="{href}" alt="Cover" style="max-width:100%;height:auto;"/>
                    </body></html>
                    """);
            }

            var playOrder = coverImageId != null ? 1 : 0;
            var itemIndex = 0;
            foreach (var section in sections)
            {
                itemIndex++;
                playOrder++;
                var id = $"chapter{itemIndex}";
                var href = $"chapter{itemIndex}.xhtml";
                var heading = string.IsNullOrWhiteSpace(section.Title) ? $"Chapter {itemIndex}" : section.Title!;
                var encodedHeading = WebUtility.HtmlEncode(heading);

                var bodySb = new StringBuilder();
                bodySb.Append("<h1>").Append(encodedHeading).Append("</h1>");
                bodySb.Append(ToWellFormedXhtmlFragment(section.ContentHtml ?? string.Empty));
                foreach (var child in childrenByParent[section.Id].OrderBy(c => c.OrderIndex))
                {
                    if (!string.IsNullOrWhiteSpace(child.Title))
                        bodySb.Append("<h2>").Append(WebUtility.HtmlEncode(child.Title)).Append("</h2>");
                    bodySb.Append(ToWellFormedXhtmlFragment(child.ContentHtml ?? string.Empty));
                }

                var xhtml = $"""
                    <?xml version="1.0" encoding="UTF-8"?>
                    <!DOCTYPE html>
                    <html xmlns="http://www.w3.org/1999/xhtml" xml:lang="en" lang="en">
                    <head>
                      <title>{encodedHeading}</title>
                      <link rel="stylesheet" type="text/css" href="styles.css"/>
                    </head>
                    <body>
                    {bodySb}
                    </body></html>
                    """;
                WriteEntry(zip, $"OEBPS/{href}", xhtml);
                manifest.AppendLine($"    <item id=\"{id}\" href=\"{href}\" media-type=\"application/xhtml+xml\"/>");
                spine.AppendLine($"    <itemref idref=\"{id}\"/>");
                navItems.AppendLine($"      <li><a href=\"{href}\">{encodedHeading}</a></li>");
                ncxNavPoints.AppendLine($"    <navPoint id=\"navpoint-{id}\" playOrder=\"{playOrder}\"><navLabel><text>{encodedHeading}</text></navLabel><content src=\"{href}\"/></navPoint>");
            }

            manifest.AppendLine("    <item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
            manifest.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");
            spine.AppendLine("    <itemref idref=\"nav\" linear=\"no\"/>");

            var coverMeta = coverImageId != null ? $"""    <meta name="cover" content="{coverImageId}"/>""" : string.Empty;

            var opf = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id">
                  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                    <dc:identifier id="book-id">{bookUid}</dc:identifier>
                    <dc:title>{title}</dc:title>
                    <dc:creator>{author}</dc:creator>
                    <dc:language>en</dc:language>
                    <meta property="dcterms:modified">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>
                {coverMeta}
                  </metadata>
                  <manifest>
                {manifest}
                  </manifest>
                  <spine toc="ncx">
                {spine}
                  </spine>
                </package>
                """;
            WriteEntry(zip, "OEBPS/content.opf", opf);

            var ncx = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE ncx PUBLIC "-//NISO//DTD ncx 2005-1//EN" "http://www.daisy.org/z3986/2005/ncx-2005-1.dtd">
                <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
                  <head>
                    <meta name="dtb:uid" content="{bookUid}"/>
                    <meta name="dtb:depth" content="1"/>
                  </head>
                  <docTitle><text>{title}</text></docTitle>
                  <navMap>
                {ncxNavPoints}
                  </navMap>
                </ncx>
                """;
            WriteEntry(zip, "OEBPS/toc.ncx", ncx);

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

        var bytes = ms.ToArray();
        ValidateStructure(bytes);
        return bytes;
    }

    /// <summary>Verifies the required EPUB 3 files exist and that mimetype is first + uncompressed.</summary>
    private static void ValidateStructure(byte[] epubBytes)
    {
        using var ms = new MemoryStream(epubBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var entries = zip.Entries.ToList();
        if (entries.Count == 0 || entries[0].FullName != "mimetype")
            throw new InvalidOperationException("EPUB build failed validation: 'mimetype' must be the first entry.");
        if (entries[0].CompressedLength != entries[0].Length)
            throw new InvalidOperationException("EPUB build failed validation: 'mimetype' must be stored uncompressed.");

        var required = new[]
        {
            "META-INF/container.xml",
            "OEBPS/content.opf",
            "OEBPS/nav.xhtml",
            "OEBPS/toc.ncx"
        };
        foreach (var path in required)
        {
            if (entries.All(e => e.FullName != path))
                throw new InvalidOperationException($"EPUB build failed validation: missing required file '{path}'.");
        }
    }

    private static string BuildStylesheet(LayoutProfile profile)
    {
        var family = string.IsNullOrWhiteSpace(profile.BodyFontFamily) ? "Georgia" : profile.BodyFontFamily;
        var align = (profile.TextAlignment ?? "Justify").Trim().ToLowerInvariant();
        if (align is not ("left" or "center" or "right" or "justify")) align = "justify";
        return $$"""
            body { font-family: '{{family}}', serif; font-size: {{profile.BodyFontSizePt}}pt; line-height: {{profile.LineSpacing}}; margin: 1em; }
            h1 { font-family: '{{profile.H1FontFamily}}', serif; font-size: {{profile.H1FontSizePt}}pt; text-align: center; margin: 0 0 1em 0; }
            h2 { font-family: '{{profile.H2FontFamily}}', serif; font-size: {{profile.H2FontSizePt}}pt; margin: 1.2em 0 0.5em 0; }
            p { margin: 0 0 0.8em 0; text-align: {{align}}; text-indent: {{profile.FirstLineIndentIn}}in; }
            p:first-of-type { text-indent: 0; }
            img { max-width: 100%; height: auto; }
            """;
    }

    private static string ToWellFormedXhtmlFragment(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "<p></p>";
        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument { OptionOutputAsXml = true, OptionWriteEmptyNodes = true };
            doc.LoadHtml("<div xmlns=\"http://www.w3.org/1999/xhtml\">" + html + "</div>");
            var inner = doc.DocumentNode.SelectSingleNode("//div")?.InnerHtml ?? string.Empty;
            inner = System.Text.RegularExpressions.Regex.Replace(inner, @"<br\s*>", "<br/>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(inner) ? "<p></p>" : inner;
        }
        catch
        {
            return $"<p>{WebUtility.HtmlEncode(html)}</p>";
        }
    }

    private static (string Href, string MediaType) DetectImageAsset(byte[] bytes, string? contentTypeHint)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ("images/cover.png", "image/png");
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ("images/cover.jpg", "image/jpeg");
        if ((contentTypeHint ?? string.Empty).Contains("webp", StringComparison.OrdinalIgnoreCase))
            return ("images/cover.webp", "image/webp");
        return ("images/cover.jpg", "image/jpeg");
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
