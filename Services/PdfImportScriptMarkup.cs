using System.Net;
using System.Text;

namespace EBookDashboard.Services;

/// <summary>
/// Encodes superscript/subscript runs extracted from PDF baselines into HTML
/// without double-encoding body text. Markers are private-use chars stripped before display.
/// </summary>
public static class PdfImportScriptMarkup
{
    public const char SupStart = '\uE000';
    public const char SupEnd = '\uE001';
    public const char SubStart = '\uE002';
    public const char SubEnd = '\uE003';

    /// <summary>Convert marked plain text to HTML with &lt;sup&gt;/&lt;sub&gt;.</summary>
    public static string ToHtml(string? markedPlain)
    {
        if (string.IsNullOrEmpty(markedPlain))
            return string.Empty;

        var sb = new StringBuilder(markedPlain.Length + 16);
        var i = 0;
        while (i < markedPlain.Length)
        {
            var ch = markedPlain[i];
            if (ch == SupStart)
            {
                i++;
                var end = markedPlain.IndexOf(SupEnd, i);
                if (end < 0) end = markedPlain.Length;
                sb.Append("<sup>").Append(WebUtility.HtmlEncode(markedPlain[i..end])).Append("</sup>");
                i = end < markedPlain.Length ? end + 1 : end;
                continue;
            }

            if (ch == SubStart)
            {
                i++;
                var end = markedPlain.IndexOf(SubEnd, i);
                if (end < 0) end = markedPlain.Length;
                sb.Append("<sub>").Append(WebUtility.HtmlEncode(markedPlain[i..end])).Append("</sub>");
                i = end < markedPlain.Length ? end + 1 : end;
                continue;
            }

            var next = i;
            while (next < markedPlain.Length
                   && markedPlain[next] is not (SupStart or SubStart))
                next++;
            sb.Append(WebUtility.HtmlEncode(markedPlain[i..next]));
            i = next;
        }

        return sb.ToString();
    }

    /// <summary>Strip script markers for plain-text compares / chapter detection.</summary>
    public static string StripMarkers(string? markedPlain)
    {
        if (string.IsNullOrEmpty(markedPlain))
            return string.Empty;
        return markedPlain
            .Replace(SupStart.ToString(), "")
            .Replace(SupEnd.ToString(), "")
            .Replace(SubStart.ToString(), "")
            .Replace(SubEnd.ToString(), "");
    }

    /// <summary>True when a letter's baseline is raised/lowered vs the line median.</summary>
    public static int ClassifyScript(double letterBaselineY, double medianBaselineY, double medianFontSize, double letterFontSize)
    {
        if (medianFontSize <= 0)
            return 0;
        var delta = letterBaselineY - medianBaselineY; // PDF Y grows upward
        var riseThresh = Math.Max(medianFontSize * 0.28, 1.8);
        var sizeSmall = letterFontSize > 0 && letterFontSize <= medianFontSize * 0.82;
        if (delta >= riseThresh && (sizeSmall || delta >= medianFontSize * 0.4))
            return 1; // superscript
        if (delta <= -riseThresh && (sizeSmall || delta <= -medianFontSize * 0.4))
            return -1; // subscript
        return 0;
    }

    /// <summary>Wrap a script run with markers (no nesting).</summary>
    public static string WrapRun(string text, int script)
    {
        if (string.IsNullOrEmpty(text) || script == 0)
            return text;
        return script > 0
            ? string.Concat(SupStart, text, SupEnd)
            : string.Concat(SubStart, text, SubEnd);
    }
}
