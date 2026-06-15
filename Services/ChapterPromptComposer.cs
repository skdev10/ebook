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

        var sb = new StringBuilder();
        foreach (var p in prior)
        {
            if (string.IsNullOrWhiteSpace(p.Content))
                continue;

            var plain = Regex.Replace(p.Content, "<[^>]+>", " ");
            plain = Regex.Replace(plain, @"\s+", " ").Trim();
            if (plain.Length < 40)
                continue;
            if (plain.Length > 4000)
                plain = plain[..4000] + "…";

            var block = $"[Prior chapter {p.ChapterNumber} (continuity only — do not repeat in output):]\n{plain}";
            if (sb.Length + block.Length + 200 > MaxContinuityChars)
                break;
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(block);
        }

        if (sb.Length == 0)
            return string.Empty;

        return sb + "\n\n---\n\n[Write ONLY the new chapter from this brief — pure narrative prose, no headings or meta:]\n";
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
}
