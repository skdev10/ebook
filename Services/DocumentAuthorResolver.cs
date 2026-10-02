using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace EBookDashboard.Services;

/// <summary>
/// Author for the title page and copyright line. Reads the manuscript, never the signed-in account name.
/// </summary>
public static class DocumentAuthorResolver
{
    /// <summary>
    /// EPUB <c>dc:creator</c>, DOCX core Creator, or PDF document info. Empty when the file has none.
    /// </summary>
    public static string? Resolve(byte[]? bytes, string? extension)
    {
        if (bytes == null || bytes.Length < 64)
            return null;

        var ext = (extension ?? "").Trim().ToLowerInvariant();
        try
        {
            var raw = ext switch
            {
                ".epub" => FromEpub(bytes),
                ".docx" => FromDocx(bytes),
                ".pdf" => FromPdf(bytes),
                _ => null
            };
            return Clean(raw);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>First <c>dc:creator</c> in an OPF document.</summary>
    public static string? ReadCreatorFromOpf(string? opfXml)
    {
        if (string.IsNullOrWhiteSpace(opfXml))
            return null;
        var match = Regex.Match(
            opfXml,
            @"<dc:creator\b[^>]*>([^<]+)</dc:creator>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? Clean(match.Groups[1].Value) : null;
    }

    private static string? FromEpub(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var container = zip.GetEntry("META-INF/container.xml");
        if (container == null)
            return null;

        string rootPath;
        using (var reader = new StreamReader(container.Open(), Encoding.UTF8))
        {
            var xml = reader.ReadToEnd();
            var path = Regex.Match(xml, @"full-path\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!path.Success)
                return null;
            rootPath = path.Groups[1].Value.Replace('\\', '/');
        }

        var opf = zip.GetEntry(rootPath);
        if (opf == null)
            return null;
        using var opfReader = new StreamReader(opf.Open(), Encoding.UTF8);
        return ReadCreatorFromOpf(opfReader.ReadToEnd());
    }

    private static string? FromDocx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, false);
        return doc.PackageProperties?.Creator;
    }

    private static string? FromPdf(byte[] bytes)
    {
        using var doc = PdfDocument.Open(bytes);
        return doc.Information?.Author;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = Regex.Replace(value.Trim(), @"\s+", " ");
        return text.Length > 250 ? text[..250] : text;
    }
}
