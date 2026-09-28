using System.Text;
using System.Text.RegularExpressions;

namespace EBookDashboard.Services;

/// <summary>
/// Shared PDF/import text cleanup: paragraph reflow, drop-cap merge, spacing, soft-hyphen repair.
/// Generic — not book-specific.
/// </summary>
public static class PdfImportTextNormalizer
{
    private static readonly HashSet<string> KnownHyphenCompounds = new(StringComparer.OrdinalIgnoreCase)
    {
        "jay-z", "peer-to-peer", "billion-dollar", "16-year-old", "all-out",
        "back-of-the-envelope", "well-known", "long-term", "short-term",
        "full-time", "part-time", "real-time", "open-source", "self-driving",
        "zero-to-one", "co-founder", "e-mail", "x-ray"
    };

    /// <summary>Normalize while preserving superscript/subscript private-use markers.</summary>
    public static string NormalizeKeepingMarkers(string? text) => Normalize(text, keepScriptMarkers: true);

    /// <summary>Normalize extracted PDF line/block text before HTML conversion.</summary>
    public static string Normalize(string? text) => Normalize(text, keepScriptMarkers: false);

    private static string Normalize(string? text, bool keepScriptMarkers)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var s = text.Replace("\u00AD", "").Replace("\u200B", "");
        s = s.Replace('\u00A0', ' ');
        if (!keepScriptMarkers)
            s = PdfImportScriptMarkup.StripMarkers(s);

        // Soft wrap hyphens: "exam-\nple" / "exam- ple" → "example" when not a known compound.
        s = Regex.Replace(s, @"(\p{L}{2,})-\s+(\p{L}{2,})", m =>
        {
            var a = m.Groups[1].Value;
            var b = m.Groups[2].Value;
            var compound = a + "-" + b;
            if (KnownHyphenCompounds.Contains(compound.ToLowerInvariant()))
                return compound;
            // Real compounds often have short second part or digits: "16-year", "peer-to"
            if (a.Length <= 3 || b.Length <= 3 || char.IsDigit(a[0]) || char.IsDigit(b[0]))
                return compound;
            return a + b;
        });

        // Keep intentional spaced hyphen compounds that remain: "billion- dollar" → "billion-dollar"
        s = Regex.Replace(s, @"(\p{L})-\s+(\p{L})", "$1-$2");

        // Drop-cap / separated first letter at line start: "S TART", "E VERY", "A S MATURE"
        s = Regex.Replace(s, @"(?m)^([A-Z])\s+([A-Z]{2,})\b", "$1$2");
        s = Regex.Replace(s, @"(?m)^([A-Z])\s+([A-Z])\b(?=\s+[A-Z])", "$1$2");
        // "A QUICK" wrongly became "AQUICK" in some extractors — reverse only when next is all-caps word of 4+
        // (do not undo intentional acronyms). Handled separately in MergeDropCapWord.

        // Apostrophe / quote spacing: "YOU ' VE" → "YOU'VE", "TODAY ' S" → "TODAY'S"
        s = Regex.Replace(s, @"(\p{L})\s+['’]\s+(\p{L})", "$1’$2");
        s = Regex.Replace(s, @"(\p{L})\s+['’](\p{L})", "$1’$2");
        s = Regex.Replace(s, @"(\p{L})['’]\s+(\p{L})", "$1’$2");

        // Punctuation spacing: "CROSSING ." → "CROSSING.", "EXPERIMENT :" → "EXPERIMENT:"
        s = Regex.Replace(s, @"\s+([.,;:!?])", "$1");
        s = Regex.Replace(s, @"([(\[{])\s+", "$1");
        s = Regex.Replace(s, @"\s+([)\]}])", "$1");

        // Collapse runs of spaces (keep newlines for callers that still use them)
        s = Regex.Replace(s, @"[^\S\r\n]{2,}", " ");

        // Uppercase letter gaps from tracked-out heads: "P RINCE" → "PRINCE" only at line start or whole token
        s = Regex.Replace(s, @"(?m)^((?:[A-Z]\s+){2,}[A-Z])\b", m =>
            Regex.Replace(m.Value, @"\s+", ""));

        return s.Trim();
    }

    /// <summary>
    /// Merge a huge first-letter word with the following word when PdfPig split a drop cap
    /// ("E" + "VERY" → "EVERY", "A" + "T" + "THE" handled via JoinPdfWords sizes).
    /// </summary>
    public static string MergeDropCapFragments(string? text)
    {
        var s = Normalize(text);
        // "E VERY MOMENT" already handled; also "AT THE" → "A" "TTHE" reverse is harder —
        // fix "X YYY" where X is single letter and YYY continues the word in lowercase/mixed:
        s = Regex.Replace(s, @"\b([A-Za-z])\s+([a-z]{2,})\b", "$1$2");
        return s;
    }

    /// <summary>Join a new PDF line onto an accumulating paragraph (smart hyphen + space).</summary>
    public static void AppendLineToParagraph(StringBuilder para, string line)
    {
        var t = NormalizeKeepingMarkers(line);
        if (t.Length == 0)
            return;

        if (para.Length == 0)
        {
            para.Append(t);
            return;
        }

        // Previous ends with soft hyphen → glue
        if (para[^1] == '-' || para[^1] == '\u00AD')
        {
            // Decide soft vs hard hyphen using next chars
            var without = para.ToString().TrimEnd('-', '\u00AD');
            var candidate = without + t;
            var compound = without[(without.LastIndexOf(' ') + 1)..] + "-" + PdfImportScriptMarkup.StripMarkers(t).Split(' ')[0];
            if (KnownHyphenCompounds.Contains(compound.ToLowerInvariant()))
            {
                para.Clear();
                para.Append(without);
                para.Append('-');
                para.Append(t);
            }
            else
            {
                para.Clear();
                para.Append(candidate);
            }
            return;
        }

        para.Append(' ');
        para.Append(t);
    }

    /// <summary>True when a vertical gap between PDF lines likely starts a new paragraph.</summary>
    public static bool LooksLikeParagraphBreak(double prevBottom, double nextTop, double medianLineHeight)
    {
        var gap = prevBottom - nextTop; // PDF Y grows upward; Top of next is lower → positive gap when next is below
        if (gap < 0)
            gap = -gap;
        var threshold = Math.Max(medianLineHeight * 1.35, 10.0);
        return gap >= threshold;
    }
}
