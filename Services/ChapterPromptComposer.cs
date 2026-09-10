using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EBookDashboard.Services;

/// <summary>
/// Separates the user's short chapter brief from AI system/continuity instructions.
/// </summary>
public static class ChapterPromptComposer
{
    private const int MaxContinuityChars = 14_000;

    private static readonly string[] BriefMarkers =
    [
        "pure narrative prose, no headings or meta:",
        "pure narrative prose:",
        "[Write ONLY the new chapter from this brief",
        "[Write ONLY the new chapter from the brief"
    ];

    /// <summary>User-facing brief only — strips continuity/system prefixes from legacy stored prompts.</summary>
    public static string NormalizeUserBrief(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var text = input.Trim();
        if (text.StartsWith('"') && text.EndsWith('"'))
        {
            try
            {
                var unquoted = JsonConvert.DeserializeObject<string>(text);
                if (!string.IsNullOrWhiteSpace(unquoted))
                    text = unquoted.Trim();
            }
            catch
            {
                // keep raw
            }
        }

        if (text.StartsWith('{'))
        {
            try
            {
                var jo = JObject.Parse(text);
                var fromJson = jo["chapterTopic"]?.ToString()
                    ?? jo["chapter_topic"]?.ToString()
                    ?? jo["UserInput"]?.ToString()
                    ?? jo["user_input"]?.ToString()
                    ?? jo["topic"]?.ToString();
                if (!string.IsNullOrWhiteSpace(fromJson))
                    return StripAugmentedPrompt(fromJson.Trim());
            }
            catch
            {
                // fall through
            }
        }

        return StripAugmentedPrompt(text);
    }

    /// <summary>Parses stored <c>apirawresponse.RequestData</c> for UI display.</summary>
    public static string ParseChapterTopicFromRequestData(string? requestData)
    {
        if (string.IsNullOrWhiteSpace(requestData))
            return string.Empty;

        var s = requestData.Trim();
        if (s.StartsWith('{'))
        {
            try
            {
                var jo = JObject.Parse(s);
                var topic = jo["chapterTopic"]?.ToString()
                    ?? jo["chapter_topic"]?.ToString()
                    ?? jo["UserInput"]?.ToString()
                    ?? jo["user_input"]?.ToString()
                    ?? jo["topic"]?.ToString()
                    ?? jo["Topic"]?.ToString();
                if (!string.IsNullOrWhiteSpace(topic))
                    return NormalizeUserBrief(topic);
            }
            catch
            {
                // fall through
            }
        }

        return NormalizeUserBrief(s);
    }

    /// <summary>JSON stored in DB — only the user brief, never system instructions.</summary>
    public static string SerializeStoredRequestData(string chapterTopic) =>
        JsonConvert.SerializeObject(new { chapterTopic = (chapterTopic ?? string.Empty).Trim() });

    /// <summary>Builds outbound user_input with optional prior-chapter continuity (server-side only).</summary>
    public static string BuildAugmentedUserInput(string userBrief, string continuityPrefix)
    {
        var brief = (userBrief ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(continuityPrefix))
            return brief;
        return continuityPrefix + brief;
    }

    /// <summary>Prior finalized chapter text for continuity — not shown in the UI topic field.</summary>
    public static async Task<string> BuildContinuityPrefixAsync(
        ApplicationDbContext db,
        int bookId,
        int beforeChapter,
        CancellationToken cancellationToken = default)
    {
        if (bookId <= 0 || beforeChapter <= 1)
            return string.Empty;

        var prior = await db.Chapters.AsNoTracking()
            .Where(c => c.BookId == bookId && c.ChapterNumber < beforeChapter)
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new { c.ChapterNumber, c.Title, c.Content })
            .ToListAsync(cancellationToken);

        var rawLatest = await db.APIRawResponse.AsNoTracking()
            .Where(r => r.BookId == bookId && r.Chapter > 0 && r.Chapter < beforeChapter)
            .OrderBy(r => r.Chapter)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => new { r.Chapter, r.Title, r.Content, r.ResponseData })
            .ToListAsync(cancellationToken);

        var rawByChapter = new Dictionary<int, (string? Title, string Text)>();
        foreach (var row in rawLatest)
        {
            if (rawByChapter.ContainsKey(row.Chapter))
                continue;
            var text = FirstUsablePlain(row.Content);
            if (string.IsNullOrEmpty(text))
                text = FirstUsablePlain(UpstreamResponseParser.ExtractContent(row.ResponseData));
            if (!string.IsNullOrEmpty(text))
                rawByChapter[row.Chapter] = (row.Title, text);
        }

        var sb = new StringBuilder();
        var seen = new HashSet<int>();
        foreach (var p in prior)
        {
            seen.Add(p.ChapterNumber);
            var plain = FirstUsablePlain(p.Content);
            if (string.IsNullOrEmpty(plain) && rawByChapter.TryGetValue(p.ChapterNumber, out var fromRaw))
                plain = fromRaw.Text;
            AppendContinuityBlock(sb, p.ChapterNumber, p.Title, plain);
        }

        foreach (var kv in rawByChapter.OrderBy(x => x.Key))
        {
            if (seen.Contains(kv.Key))
                continue;
            AppendContinuityBlock(sb, kv.Key, kv.Value.Title, kv.Value.Text);
        }

        if (sb.Length == 0)
            return string.Empty;

        return sb + "\n\n---\n\n[Write ONLY the new chapter from this brief — pure narrative prose, no headings or meta:]\n";
    }

    private static void AppendContinuityBlock(StringBuilder sb, int chapterNumber, string? title, string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain) || plain.Length < 40)
            return;
        if (plain.Length > 4000)
            plain = plain[..4000] + "…";
        var label = string.IsNullOrWhiteSpace(title) ? $"Prior chapter {chapterNumber}" : $"Prior chapter {chapterNumber} ({title.Trim()})";
        var block = $"[{label} (continuity only — do not repeat in output):]\n{plain}";
        if (sb.Length + block.Length + 200 > MaxContinuityChars)
            return;
        if (sb.Length > 0)
            sb.Append("\n\n");
        sb.Append(block);
    }

    private static string FirstUsablePlain(string? htmlOrText)
    {
        if (string.IsNullOrWhiteSpace(htmlOrText))
            return string.Empty;
        var plain = Regex.Replace(htmlOrText, "<[^>]+>", " ");
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        return plain;
    }

    /// <summary>Display label: Chapter {number}: {title}</summary>
    public static string FormatChapterLabel(int chapterNumber, string? title)
    {
        var n = chapterNumber > 0 ? chapterNumber : 1;
        var t = (title ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(t) || t.Equals($"Chapter {n}", StringComparison.OrdinalIgnoreCase))
            return $"Chapter {n}";
        t = Regex.Replace(t, @"^Chapter\s*\d+\s*[:\-–]\s*", "", RegexOptions.IgnoreCase).Trim();
        return string.IsNullOrEmpty(t) ? $"Chapter {n}" : $"Chapter {n}: {t}";
    }

    private static string StripAugmentedPrompt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        foreach (var marker in BriefMarkers)
        {
            var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;
            var tail = text[(idx + marker.Length)..].Trim();
            tail = tail.TrimStart(':', '\n', '\r', ' ', '"');
            if (!string.IsNullOrWhiteSpace(tail))
                return tail.Trim().Trim('"');
        }

        if (text.Contains("[Prior chapter", StringComparison.OrdinalIgnoreCase)
            && text.Contains("---", StringComparison.Ordinal))
        {
            var parts = text.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                var last = parts[^1].Trim();
                foreach (var marker in BriefMarkers)
                {
                    var mi = last.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (mi >= 0)
                    {
                        var brief = last[(mi + marker.Length)..].Trim().Trim('"');
                        if (!string.IsNullOrWhiteSpace(brief))
                            return brief;
                    }
                }
            }
        }

        return text.Trim();
    }

    /// <summary>Rewrite prompt for <c>/api/generate_chapter</c> when <c>/api/edit</c> has no stored chapter.</summary>
    public static string BuildEditRewritePrompt(string instructions, string originalChapter, string? title)
    {
        var changes = (instructions ?? string.Empty).Trim();
        var original = (originalChapter ?? string.Empty).Trim();
        if (original.Length > 100_000)
            original = original[..100_000];

        var heading = string.IsNullOrWhiteSpace(title) ? "" : $"Title: {title.Trim()}\n\n";
        return $"""
Revise the existing chapter using the author's instructions. Output only the complete revised chapter as polished literary prose.

Author instructions:
{changes}

{heading}Current chapter:
{original}

Rules:
- Output the full revised chapter only.
- Keep the same story unless the instructions change it.
- No meta-commentary, no "Chapter N" labels, no regenerate/UI language.
""";
    }
}
