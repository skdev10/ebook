using System.Text.RegularExpressions;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>Splits a whole-book HTML fragment into chapter DTOs for formatter preview and export.</summary>
public static class BookContentHtmlParser
{
    private static readonly Regex H2SplitRegex = new(
        @"<h2\b[^>]*>(.*?)</h2>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Parses <paramref name="html"/> into ordered chapters (by <c>&lt;h2&gt;</c> headings).</summary>
    public static List<ChapterDto> ToChapterDtos(string? html)
    {
        var sanitized = BookGenerationHtmlSanitizer.Sanitize(html);
        if (string.IsNullOrWhiteSpace(sanitized))
            return [];

        var matches = H2SplitRegex.Matches(sanitized);
        if (matches.Count == 0)
        {
            return
            [
                new ChapterDto
                {
                    ChapterNumber = 1,
                    Title = "Chapter 1",
                    Content = sanitized,
                    StatusCode = "Generated",
                    CreatedAt = DateTime.UtcNow
                }
            ];
        }

        var chapters = new List<ChapterDto>();
        for (var i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var title = StripInnerTags(m.Groups[1].Value).Trim();
            if (string.IsNullOrWhiteSpace(title))
                title = $"Chapter {i + 1}";

            var bodyStart = m.Index + m.Length;
            var bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : sanitized.Length;
            var body = sanitized[bodyStart..bodyEnd].Trim();
            body = Regex.Replace(body, @"<hr\s*/?>\s*$", "", RegexOptions.IgnoreCase).Trim();

            chapters.Add(new ChapterDto
            {
                ChapterNumber = i + 1,
                Title = title,
                Content = body,
                StatusCode = "Generated",
                CreatedAt = DateTime.UtcNow
            });
        }

        return chapters;
    }

    private static string StripInnerTags(string inner) =>
        Regex.Replace(inner ?? "", "<[^>]+>", "").Trim();
}
