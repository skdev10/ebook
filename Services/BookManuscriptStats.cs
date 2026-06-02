using System.Net;
using System.Text.RegularExpressions;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

/// <summary>Word count and description excerpt from live manuscript content.</summary>
public static class BookManuscriptStats
{
    private static readonly Regex HtmlTagRegex = new("<.*?>", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex WordRegex = new(@"\b\w+\b", RegexOptions.Compiled);

    public sealed record ManuscriptSummary(int WordCount, string Description);

    public static int CountWords(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;
        var plain = HtmlTagRegex.Replace(content, " ");
        plain = WebUtility.HtmlDecode(plain);
        return WordRegex.Matches(plain).Count;
    }

    public static string StripToPlain(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var plain = HtmlTagRegex.Replace(html, " ");
        return Regex.Replace(WebUtility.HtmlDecode(plain), @"\s+", " ").Trim();
    }

    public static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen) return text;
        return text.Substring(0, maxLen).TrimEnd() + "…";
    }

    public static ManuscriptSummary Resolve(Books? book, BookDetailsResponseDto? details)
    {
        var dbWords = book?.WordCount ?? 0;
        var dbDesc = (book?.Description ?? "").Trim();
        var subtitle = (details?.Subtitle ?? book?.Subtitle ?? "").Trim();
        var genre = (book?.Genre ?? details?.Genre ?? "").Trim();

        var chapters = details?.Success == true && details.Chapters != null
            ? details.Chapters.Where(c => !string.IsNullOrWhiteSpace(c.Content)).ToList()
            : new List<ChapterDto>();

        var computedWords = chapters.Sum(c => CountWords(c.Content));
        var wordCount = dbWords > 0 ? dbWords : computedWords;

        var description = dbDesc;
        if (string.IsNullOrWhiteSpace(description))
            description = subtitle;

        if (string.IsNullOrWhiteSpace(description) && chapters.Count > 0)
        {
            var first = chapters
                .OrderBy(c => c.ChapterNumber)
                .Select(c => StripToPlain(c.Content))
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (!string.IsNullOrWhiteSpace(first))
                description = Truncate(first, 320);
        }

        if (string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(genre))
            description = "A " + genre + " book.";

        if (string.IsNullOrWhiteSpace(description) && !string.IsNullOrWhiteSpace(book?.Title))
            description = "An ebook by the author of \"" + book.Title.Trim() + "\".";

        return new ManuscriptSummary(wordCount, description);
    }
}
