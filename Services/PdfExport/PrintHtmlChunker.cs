using System.Text;
using System.Text.RegularExpressions;

namespace EBookDashboard.Services.PdfExport;

/// <summary>
/// Splits a finished print document on chapter boundaries so Chromium renders about
/// a hundred pages at a time. Front matter stays on the first chunk only.
/// </summary>
public static class PrintHtmlChunker
{
    /// <summary>Chapter HTML kept in one Chromium print. Larger than this starts a new chunk.</summary>
    public const int ChunkCharBudget = 180_000;

    /// <summary>Whole-document size that is still printed in one pass.</summary>
    public const int SinglePassCharLimit = 450_000;

    public static bool ShouldChunk(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return false;
        var chapters = Regex.Matches(html, "<section class=\"chapter\"", RegexOptions.IgnoreCase).Count;
        return html.Length > SinglePassCharLimit || chapters > 24;
    }

    /// <summary>One self-contained HTML document per chunk, in reading order.</summary>
    public static IReadOnlyList<string> Split(string html)
    {
        if (string.IsNullOrEmpty(html) || !ShouldChunk(html))
            return new[] { html };

        const string rootOpen = "<div class=\"manuscript-root\">";
        var rootAt = html.IndexOf(rootOpen, StringComparison.Ordinal);
        var bodyClose = html.LastIndexOf("</body>", StringComparison.Ordinal);
        if (rootAt < 0 || bodyClose < 0)
            return new[] { html };

        var innerStart = rootAt + rootOpen.Length;
        var rootClose = html.LastIndexOf("</div>", bodyClose, StringComparison.Ordinal);
        if (rootClose <= innerStart)
            return new[] { html };

        var firstPrefix = html[..innerStart];
        var suffix = html[rootClose..];
        var bodyAt = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var bodyTagEnd = bodyAt < 0 ? -1 : html.IndexOf('>', bodyAt);
        var laterPrefix = bodyTagEnd > 0
            ? html[..(bodyTagEnd + 1)] + "\n" + rootOpen
            : firstPrefix;

        var inner = html[innerStart..rootClose];
        var sections = Regex.Split(inner, "(?=<section class=\"chapter\")")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        if (sections.Count <= 1)
            return new[] { html };

        var chunks = new List<string>();
        var batch = new StringBuilder();
        var batchChars = 0;
        foreach (var section in sections)
        {
            if (batchChars > 0 && batchChars + section.Length > ChunkCharBudget)
            {
                chunks.Add(Wrap(chunks.Count == 0 ? firstPrefix : laterPrefix, batch, suffix));
                batch.Clear();
                batchChars = 0;
            }

            batch.Append(section);
            batchChars += section.Length;
        }

        if (batchChars > 0)
            chunks.Add(Wrap(chunks.Count == 0 ? firstPrefix : laterPrefix, batch, suffix));

        return chunks.Count == 0 ? new[] { html } : chunks;
    }

    private static string Wrap(string prefix, StringBuilder body, string suffix)
    {
        var doc = new StringBuilder(prefix.Length + body.Length + suffix.Length + 8);
        doc.Append(prefix);
        doc.Append(body);
        doc.Append(suffix);
        return doc.ToString();
    }
}
