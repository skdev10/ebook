using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace EBookDashboard.Services;

/// <summary>
/// Builds a FastAPI-safe payload for <c>/api/book_chapters_name</c> and contextual chapter-title
/// recommendations tied to the book the user typed.
/// </summary>
public static class ChapterTitleSuggester
{
    private static readonly Regex FillerPrefix = new(
        @"^(the|a|an)\s+(complete\s+)?(guide|handbook|primer)\s+to\s+|^foundations\s+of\s+|^a\s+guide\s+to\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex FillerSuffix = new(
        @"\s+(foundations|handbook|guide|primer|essentials|basics|explained|for\s+beginners|101)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "of", "a", "an", "and", "in", "to", "for", "on", "with", "book", "chapter",
        "untitled", "complete", "guide", "handbook", "foundations", "primer"
    };

    /// <summary>Upstream highlight item keys — extra fields cause FastAPI 422 when extra=forbid.</summary>
    public static JArray NormalizeHighlights(JToken? highlights, string? bookTitle, string? topic, string? chapterName)
    {
        var result = new JArray();
        var subject = ExtractTopic(bookTitle, topic);
        var instructed = string.IsNullOrWhiteSpace(subject)
            ? "Suggest five original chapter titles for this book."
            : "The book is titled \"" + subject + "\". Suggest five original chapter titles clearly about " + subject + ".";
        if (highlights is JArray arr)
        {
            foreach (var item in arr)
            {
                if (item is not JObject obj)
                    continue;
                var name = FirstNonEmpty(obj["chapter_name"]?.ToString(), obj["chapterName"]?.ToString(), chapterName, bookTitle, "Chapter");
                var summary = FirstNonEmpty(
                    obj["detailed_bullet_summary"]?.ToString(),
                    obj["summary"]?.ToString(),
                    instructed,
                    topic,
                    bookTitle);
                result.Add(Highlight(name, summary));
            }
        }

        if (result.Count == 0)
            result.Add(Highlight(FirstNonEmpty(chapterName, bookTitle, "Chapter"), instructed));

        return result;
    }

    /// <summary>Only the three fields documented for <c>/api/book_chapters_name</c>.</summary>
    public static JObject BuildUpstreamPayload(string userId, string bookId, JArray highlights)
    {
        return new JObject
        {
            ["user_id"] = string.IsNullOrWhiteSpace(userId) ? "0" : userId.Trim(),
            ["book_id"] = string.IsNullOrWhiteSpace(bookId) ? "0" : bookId.Trim(),
            ["highlights"] = highlights ?? new JArray()
        };
    }

    /// <summary>Pulls chapter titles out of upstream JSON, HTML lists, or a titles array.</summary>
    public static IReadOnlyList<string> ParseTitles(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Array.Empty<string>();
        try
        {
            var token = JToken.Parse(json);
            return ParseTitles(token);
        }
        catch (Exception)
        {
            return SplitLines(json);
        }
    }

    /// <summary>Walks common response shapes for <c>suggest_chapter_name</c>.</summary>
    public static IReadOnlyList<string> ParseTitles(JToken? token)
    {
        var names = new List<string>();
        Walk(token, names);
        return Dedup(names);
    }

    /// <summary>Core topic from a book title, e.g. "Artificial Intelligence Foundations" → "Artificial Intelligence".</summary>
    public static string ExtractTopic(string? bookTitle, string? topic)
    {
        var raw = FirstNonEmpty(CleanSubject(bookTitle), CleanSubject(topic));
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        raw = FillerPrefix.Replace(raw, "").Trim();
        raw = FillerSuffix.Replace(raw, "").Trim();
        return string.IsNullOrWhiteSpace(raw) ? FirstNonEmpty(CleanSubject(bookTitle), CleanSubject(topic)) : raw;
    }

    /// <summary>True when a suggestion mentions the book topic (or its acronym).</summary>
    public static bool IsRelevantToBook(string? suggestion, string? bookTitle, string? topic)
    {
        var subject = ExtractTopic(bookTitle, topic);
        if (string.IsNullOrWhiteSpace(suggestion) || string.IsNullOrWhiteSpace(subject))
            return false;
        var hay = suggestion.Trim();
        var acronym = TryAcronym(subject);
        if (!string.IsNullOrEmpty(acronym) && hay.Contains(acronym, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var token in Tokens(subject))
        {
            if (hay.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Keeps only suggestions that clearly relate to the typed book title.</summary>
    public static IReadOnlyList<string> FilterRelevant(IEnumerable<string>? titles, string? bookTitle, string? topic)
    {
        if (titles == null)
            return Array.Empty<string>();
        return Dedup(titles.Where(t => IsRelevantToBook(t, bookTitle, topic)));
    }

    /// <summary>Topic-aware recommendations so the AI icon always returns titles about this book.</summary>
    public static IReadOnlyList<string> BuildLocalSuggestions(string? bookTitle, string? topic, string? currentChapterTitle = null)
    {
        var subject = ExtractTopic(bookTitle, topic);
        if (string.IsNullOrWhiteSpace(subject))
            subject = "this topic";
        var shortName = TryAcronym(subject) ?? subject;

        var candidates = new[]
        {
            "Introduction to " + subject,
            "Importance of " + shortName,
            "Applications of " + shortName,
            "History of " + subject,
            "Future of " + subject
        };

        var outList = new List<string>();
        foreach (var title in candidates)
        {
            var t = title.Trim();
            if (string.Equals(t, currentChapterTitle, StringComparison.OrdinalIgnoreCase))
                continue;
            outList.Add(t);
        }

        return Dedup(outList);
    }

    /// <summary>Prefers relevant API titles, then fills remaining slots from the book title.</summary>
    public static IReadOnlyList<string> MergeSuggestions(
        IEnumerable<string>? apiTitles,
        string? bookTitle,
        string? topic,
        string? currentChapterTitle = null,
        int take = 5)
    {
        var local = BuildLocalSuggestions(bookTitle, topic, currentChapterTitle);
        var relevantApi = FilterRelevant(apiTitles, bookTitle, topic);
        var merged = new List<string>();
        foreach (var t in relevantApi.Concat(local))
        {
            if (merged.Count >= take)
                break;
            if (merged.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
                continue;
            merged.Add(t);
        }
        return merged;
    }

    /// <summary>HTML list matching generate_chapter's <c>suggest_chapter_name</c> shape.</summary>
    public static string ToHtmlList(IEnumerable<string> titles)
    {
        var items = titles?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => "<li>" + System.Net.WebUtility.HtmlEncode(t.Trim()) + "</li>")
            ?? Array.Empty<string>();
        return "<ul>" + string.Join("", items) + "</ul>";
    }

    private static string? TryAcronym(string subject)
    {
        var words = subject.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && char.IsLetter(w[0]))
            .ToList();
        if (words.Count < 2)
            return null;
        return string.Concat(words.Select(w => char.ToUpperInvariant(w[0])));
    }

    private static IEnumerable<string> Tokens(string subject)
    {
        foreach (var raw in subject.Split(new[] { ' ', '-', '/', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Trim().Trim('.', ',', ';');
            if (t.Length < 3 || StopWords.Contains(t))
                continue;
            yield return t;
        }
    }

    private static JObject Highlight(string chapterName, string summary)
    {
        var name = FirstNonEmpty(chapterName, "Chapter");
        var body = FirstNonEmpty(summary, "Suggest chapter titles for this book.");
        if (body.Length < 12)
            body = "Write recommended chapter titles about " + name + ".";
        if (name.Length > 200)
            name = name[..200];
        if (body.Length > 4000)
            body = body[..4000];
        return new JObject
        {
            ["chapter_name"] = name,
            ["detailed_bullet_summary"] = body
        };
    }

    private static void Walk(JToken? val, List<string> names)
    {
        if (val == null || val.Type == JTokenType.Null)
            return;
        if (val is JArray arr)
        {
            foreach (var item in arr)
                Walk(item, names);
            return;
        }
        if (val is JObject obj)
        {
            Walk(FirstUseful(obj,
                    "suggest_chapter_name", "SuggestChapterName", "chapter_titles", "chapterTitles",
                    "titles", "chapters", "chapter_names", "chapterNames", "suggestions", "names", "result", "data"),
                names);
            var single = obj["chapter_name"]?.ToString();
            if (!string.IsNullOrWhiteSpace(single) && obj["suggest_chapter_name"] == null && obj["titles"] == null && obj["chapter_titles"] == null)
                names.Add(single);
            return;
        }
        SplitInto(val.ToString(), names);
    }

    private static JToken? FirstUseful(JObject obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            var t = obj[key];
            if (t == null || t.Type == JTokenType.Null)
                continue;
            if (t.Type == JTokenType.String && string.IsNullOrWhiteSpace(t.ToString()))
                continue;
            if (t is JArray arr && arr.Count == 0)
                continue;
            return t;
        }
        return null;
    }

    private static IReadOnlyList<string> SplitLines(string s)
    {
        var names = new List<string>();
        SplitInto(s, names);
        return Dedup(names);
    }

    private static void SplitInto(string s, List<string> names)
    {
        if (string.IsNullOrWhiteSpace(s))
            return;
        if (s.Contains("<li", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match m in Regex.Matches(s, "<li[^>]*>([\\s\\S]*?)</li>", RegexOptions.IgnoreCase))
            {
                var t = Regex.Replace(m.Groups[1].Value, "<[^>]+>", "").Trim();
                if (!string.IsNullOrWhiteSpace(t))
                    names.Add(t);
            }
            return;
        }
        foreach (var part in s.Split(new[] { '\r', '\n', ';', '•', '‣' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = Regex.Replace(part, @"^[\s\-\*\d\.\)\(]+", "").Trim();
            if (!string.IsNullOrWhiteSpace(t))
                names.Add(t);
        }
    }

    private static IReadOnlyList<string> Dedup(IEnumerable<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outList = new List<string>();
        foreach (var raw in names)
        {
            var t = CleanSubject(raw);
            if (string.IsNullOrWhiteSpace(t))
                continue;
            if (t.Length > 200)
                t = t[..200];
            if (!seen.Add(t))
                continue;
            outList.Add(t);
        }
        return outList;
    }

    private static string CleanSubject(string? value)
    {
        var t = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(t))
            return "";
        if (string.Equals(t, "Untitled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "Untitled Book", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(t, @"^Chapter\s*\d+$", RegexOptions.IgnoreCase))
            return "";
        return t;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }
        return "";
    }
}
