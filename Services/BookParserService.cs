using HtmlAgilityPack;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using static EBookDashboard.Models.DTO.BookModels;

namespace EBookDashboard.Services
{
    public class BookParserService
    {
        public BookResult Parse(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new BookResult
                {
                    Book = new BookContent(),
                    Highlights = new HighlightContent()
                };
            }

            try
            {
                var json = JObject.Parse(rawJson);

                var result = new BookResult
                {
                    Book = ParseBook(json["data"]?["content"]?.ToString()),
                    Highlights = ParseHighlights(json["data"]?["highlights"]?.ToString())
                };

                return result;
            }
            catch (Exception ex)
            {
                // Return empty result if parsing fails
                Console.WriteLine($"Error parsing JSON: {ex.Message}");
                return new BookResult
                {
                    Book = new BookContent(),
                    Highlights = new HighlightContent()
                };
            }
        }

        // ===============================
        // BOOK CONTENT (ENDS BEFORE HIGHLIGHTS)
        // ===============================
        private BookContent ParseBook(string html)
        {
            var book = new BookContent();

            if (string.IsNullOrWhiteSpace(html))
            {
                return book;
            }

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Process all nodes recursively, not just direct children
                ProcessNode(doc.DocumentNode, book);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing HTML: {ex.Message}");
            }

            return book;
        }

        private void ProcessNode(HtmlNode node, BookContent book)
        {
            if (node == null) return;

            // Process current node
            if (node.Name == "h1" && string.IsNullOrEmpty(book.ChapterTitle))
            {
                book.ChapterTitle = node.InnerText.Trim();
            }
            else if (node.Name == "h2")
            {
                book.Sections.Add(new BookSection
                {
                    Tag = "h2",
                    Title = node.InnerText.Trim()
                });
            }
            else if (node.Name == "p")
            {
                var text = node.InnerText?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var section = new BookSection
                    {
                        Tag = "p",
                        Sentences = SplitSentences(text)
                    };
                    book.Sections.Add(section);
                }
            }
            else if (node.Name == "h3" || node.Name == "h4" || node.Name == "h5" || node.Name == "h6")
            {
                book.Sections.Add(new BookSection
                {
                    Tag = node.Name,
                    Title = node.InnerText.Trim()
                });
            }

            // Process child nodes recursively
            if (node.HasChildNodes)
            {
                foreach (var child in node.ChildNodes)
                {
                    ProcessNode(child, book);
                }
            }
        }

        // ===============================
        // HIGHLIGHTS (AFTER BOOK ENDS)
        // ===============================
        private HighlightContent ParseHighlights(string highlightJson)
        {
            var highlightContent = new HighlightContent();

            if (string.IsNullOrWhiteSpace(highlightJson))
            {
                return highlightContent;
            }

            try
            {
                // Try to parse as JSON first
                JObject highlightsObj;
                try
                {
                    highlightsObj = JObject.Parse(highlightJson);
                }
                catch
                {
                    // If it's not JSON, treat it as HTML
                    highlightsObj = new JObject
                    {
                        ["detailed_bullet_summary"] = highlightJson
                    };
                }

                highlightContent.ChapterName = highlightsObj["chapter_name"]?.ToString();

                var html = highlightsObj["detailed_bullet_summary"]?.ToString();
                if (string.IsNullOrWhiteSpace(html))
                {
                    // Try to get HTML directly if it's not in detailed_bullet_summary
                    html = highlightJson;
                }

                if (!string.IsNullOrWhiteSpace(html))
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var listItems = doc.DocumentNode.SelectNodes("//li") ?? Enumerable.Empty<HtmlNode>();
                    foreach (var li in listItems)
                    {
                        var item = new HighlightItem();

                        var strong = li.SelectSingleNode(".//strong");
                        if (strong != null)
                        {
                            item.Heading = strong.InnerText.Trim();
                            strong.Remove();
                        }

                        var text = li.InnerText?.Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            item.Sentences = SplitSentences(text);
                            highlightContent.Items.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing highlights: {ex.Message}");
            }

            return highlightContent;
        }

        // ===============================
        // SENTENCE SPLITTER
        // ===============================
        private List<string> SplitSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            return Regex
                .Split(text.Trim(), @"(?<=[\.!\?])\s+")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
    }
}
