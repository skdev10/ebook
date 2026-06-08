using Newtonsoft.Json.Linq;

namespace EBookDashboard.Services;

/// <summary>
/// Strips API JSON wrappers from stored chapter bodies — mirrors client <c>getContentFromPossibleJson</c>.
/// Used by preview load, PDF export, and EPUB so all books share one pipeline.
/// </summary>
public static class ChapterContentNormalizer
{
    /// <summary>Returns clean manuscript HTML/text (never raw <c>{"status":"success","data":...}</c>).</summary>
    public static string NormalizeForManuscript(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var s = BookManuscriptHtmlFormatter.NormalizeManuscriptEscapes(raw.Trim());
        if (!LooksLikeJsonEnvelope(s))
            return s;

        var fromClientLogic = TryUnwrapLikeClientPreview(s);
        if (IsCleanManuscript(fromClientLogic))
            return fromClientLogic!.Trim();

        var fromParser = UpstreamResponseParser.ExtractContent(s);
        if (IsCleanManuscript(fromParser))
            return fromParser.Trim();

        var fromDataLiteral = UpstreamResponseParser.TryExtractDataStringLiteral(s);
        if (IsCleanManuscript(fromDataLiteral))
            return fromDataLiteral!.Trim();

        return s;
    }

    public static bool LooksLikeJsonEnvelope(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.TrimStart();
        if (t.StartsWith('{') || t.StartsWith('[')) return true;
        return s.Contains("\"status\"", StringComparison.Ordinal)
               && s.Contains("\"data\"", StringComparison.Ordinal);
    }

    private static bool IsCleanManuscript(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !LooksLikeJsonEnvelope(value);

    /// <summary>Same field order as formatter / AI Writer <c>getContentFromPossibleJson</c>.</summary>
    private static string? TryUnwrapLikeClientPreview(string json)
    {
        try
        {
            var root = JToken.Parse(json);
            if (root is not JObject obj)
                return null;

            var data = obj["data"];
            if (data is JObject dataObj)
            {
                foreach (var key in new[] { "content", "chapter_content", "text", "body", "chapter_text", "result" })
                {
                    var v = dataObj[key]?.ToString();
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }

                var chapters = dataObj["chapters"] as JArray;
                if (chapters is { Count: > 0 } && chapters[0] is JObject first)
                {
                    var c = first["content"]?.ToString() ?? first["Content"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(c))
                        return c;
                }
            }
            else if (data?.Type == JTokenType.String)
            {
                var dataStr = data.ToString();
                if (!string.IsNullOrWhiteSpace(dataStr))
                    return dataStr;
            }

            foreach (var key in new[] { "content", "chapter_content", "text", "body" })
            {
                var v = obj[key]?.ToString();
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }

            var rootChapters = obj["chapters"] as JArray;
            if (rootChapters is { Count: > 0 } && rootChapters[0] is JObject firstCh)
            {
                var c = firstCh["content"]?.ToString() ?? firstCh["Content"]?.ToString();
                if (!string.IsNullOrWhiteSpace(c))
                    return c;
            }
        }
        catch
        {
            /* fall through */
        }

        return null;
    }
}
