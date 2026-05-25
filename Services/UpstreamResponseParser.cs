using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace EBookDashboard.Services;

/// <summary>
/// Parses upstream FastAPI JSON into chapter body text and suggested names.
/// Handles multiple response shapes (data object, string data, root content/heading).
/// </summary>
public static class UpstreamResponseParser
{
    public static string ExtractContent(string? jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse))
            return string.Empty;

        var trimmed = jsonResponse.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('['))
            return trimmed;

        try
        {
            var root = JToken.Parse(trimmed);
            if (root is not JObject obj)
                return trimmed;

            var content = TryExtractContentFromObject(obj);
            if (!string.IsNullOrWhiteSpace(content))
                return content.Trim();

            return ExtractContentWithRegex(trimmed);
        }
        catch
        {
            return ExtractContentWithRegex(trimmed);
        }
    }

    public static List<string> ExtractSuggestedChapterNames(string? jsonResponse)
    {
        var chapters = new List<string>();
        if (string.IsNullOrWhiteSpace(jsonResponse))
            return chapters;

        try
        {
            var obj = JObject.Parse(jsonResponse);
            var chapterText = ReadToken(obj["data"], "suggest_chapter_name")
                ?? ReadToken(obj, "suggest_chapter_name");

            if (string.IsNullOrWhiteSpace(chapterText))
                return chapters;

            ParseChapterNameList(chapterText, chapters);
        }
        catch
        {
            /* best-effort */
        }

        return chapters;
    }

    /// <summary>Normalize upstream chapter generate/edit response for the AI Writer UI.</summary>
    public static string? NormalizeChapterJson(string? jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse) || !jsonResponse.TrimStart().StartsWith('{'))
            return null;

        try
        {
            var obj = JObject.Parse(jsonResponse);
            var content = ExtractContent(jsonResponse);
            var suggest = ReadToken(obj["data"], "suggest_chapter_name")
                ?? ReadToken(obj, "suggest_chapter_name");
            var heading = ReadToken(obj["data"], "heading")
                ?? ReadToken(obj, "heading")
                ?? ReadToken(obj["data"], "chapter_name")
                ?? ReadToken(obj, "chapter_name");

            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(suggest) && string.IsNullOrWhiteSpace(heading))
                return null;

            var data = obj["data"] as JObject ?? new JObject();
            if (!string.IsNullOrWhiteSpace(content))
                data["content"] = content;
            if (!string.IsNullOrWhiteSpace(suggest))
                data["suggest_chapter_name"] = suggest;
            if (!string.IsNullOrWhiteSpace(heading))
                data["heading"] = heading;

            obj["data"] = data;
            if (obj["status"] == null && !string.IsNullOrWhiteSpace(content))
                obj["status"] = "success";

            return obj.ToString(Formatting.None);
        }
        catch
        {
            return null;
        }
    }

    private static string TryExtractContentFromObject(JObject obj)
    {
        foreach (var key in new[] { "content", "chapter_content", "text", "body", "chapter_text", "result" })
        {
            var v = ReadToken(obj, key);
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }

        var data = obj["data"];
        if (data != null)
        {
            if (data.Type == JTokenType.String)
            {
                var s = data.ToString();
                if (!string.IsNullOrWhiteSpace(s) && !s.TrimStart().StartsWith('{'))
                    return s;
            }
            else if (data is JObject dataObj)
            {
                foreach (var key in new[] { "content", "chapter_content", "text", "body", "chapter_text", "result" })
                {
                    var v = ReadToken(dataObj, key);
                    if (!string.IsNullOrWhiteSpace(v))
                        return v;
                }

                var heading = ReadToken(dataObj, "heading") ?? ReadToken(dataObj, "chapter_name");
                if (!string.IsNullOrWhiteSpace(heading))
                    return heading;
            }
        }

        var rootHeading = ReadToken(obj, "heading") ?? ReadToken(obj, "chapter_name");
        if (!string.IsNullOrWhiteSpace(rootHeading))
            return rootHeading;

        var message = ReadToken(obj, "message");
        if (!string.IsNullOrWhiteSpace(message) && message.Length > 40)
            return message;

        return string.Empty;
    }

    private static string? ReadToken(JToken? parent, string property)
    {
        if (parent is not JObject o)
            return null;
        var t = o[property];
        if (t == null || t.Type == JTokenType.Null)
            return null;
        return t.Type == JTokenType.String ? t.ToString() : t.ToString(Formatting.None);
    }

    private static void ParseChapterNameList(string chapterText, List<string> chapters)
    {
        if (chapterText.Contains("<li>", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in Regex.Matches(chapterText, @"<li>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                if (!match.Success) continue;
                var name = Regex.Replace(match.Groups[1].Value, @"<[^>]*>", "").Trim();
                if (!string.IsNullOrEmpty(name))
                    chapters.Add(name);
            }
            return;
        }

        foreach (var line in chapterText.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = Regex.Replace(line.Trim(),
                @"^(?:\d+[\.\)]\s*|Chapter\s+\d+[:\.]\s*|-\s*|\*\s*)", "", RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrEmpty(clean))
                chapters.Add(clean);
        }

        if (chapters.Count == 0 && !string.IsNullOrWhiteSpace(chapterText))
            chapters.Add(chapterText.Trim());
    }

    private static string ExtractContentWithRegex(string jsonResponse)
    {
        foreach (var pattern in new[]
        {
            @"""content""\s*:\s*""((?:\\.|[^""\\])*)""",
            @"""content""\s*:\s*""(.*?)""",
            @"""heading""\s*:\s*""((?:\\.|[^""\\])*)"""
        })
        {
            var match = Regex.Match(jsonResponse, pattern, RegexOptions.Singleline);
            if (match.Success && match.Groups.Count > 1)
            {
                var extracted = match.Groups[1].Value;
                if (!string.IsNullOrWhiteSpace(extracted))
                    return extracted.Replace("\\n", "\n").Replace("\\\"", "\"");
            }
        }

        return string.Empty;
    }
}
