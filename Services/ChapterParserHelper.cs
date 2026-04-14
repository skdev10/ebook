using EBookDashboard.Models;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class ChapterParserHelper
    {
        public static List<ParsedChapter> ParseChaptersFromContent(string htmlContent)
        {
            var chapters = new List<ParsedChapter>();

            if (string.IsNullOrEmpty(htmlContent))
                return chapters;

            // Pattern to match <h2> tags and their content
            var h2Pattern = @"<h2>(.*?)</h2>";
            var matches = Regex.Matches(htmlContent, h2Pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (matches.Count == 0)
            {
                // If no <h2> tags, treat entire content as one chapter
                chapters.Add(new ParsedChapter
                {
                    Title = "Chapter 1",
                    Content = htmlContent,
                    ChapterNumber = 1
                });
                return chapters;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                var chapter = new ParsedChapter();

                // Extract title (remove any HTML tags from title)
                var titleHtml = matches[i].Groups[1].Value.Trim();
                chapter.Title = Regex.Replace(titleHtml, "<.*?>", string.Empty);

                if (string.IsNullOrEmpty(chapter.Title))
                    chapter.Title = $"Chapter {i + 1}";

                // Extract content between current <h2> and next <h2>
                int startIndex = matches[i].Index + matches[i].Length;
                int endIndex = (i < matches.Count - 1) ? matches[i + 1].Index : htmlContent.Length;

                var chapterContent = htmlContent.Substring(startIndex, endIndex - startIndex).Trim();

                // Clean up the content
                chapter.Content = CleanHtmlContent(chapterContent);
                chapter.ChapterNumber = i + 1;

                chapters.Add(chapter);
            }

            return chapters;
        }
        public static string CleanHtmlContent(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            // Remove excessive whitespace but preserve paragraph structure
            html = Regex.Replace(html, @"\s+", " ");
            html = Regex.Replace(html, @"\n\s*\n", "\n\n");

            return html.Trim();
        }

        public static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            // Remove HTML tags for accurate word count
            var cleanText = Regex.Replace(text, "<.*?>", string.Empty);
            return cleanText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        public static int CalculateTotalWordCount(List<ParsedChapter> chapters)
        {
            return chapters.Sum(chapter => CountWords(chapter.Content));
        }


    }
}
