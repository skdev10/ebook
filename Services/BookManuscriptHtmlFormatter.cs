using System.Net;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Linq;

namespace EBookDashboard.Services;

/// <summary>
/// Aligns with <c>Views/Books/AIGenerateBook.cshtml</c> manuscript helpers: escapes, placeholders, markdown headings, hr, HTML whitelist.
/// </summary>
public static class BookManuscriptHtmlFormatter
{
    public sealed class PlaceholderContext
    {
        public string BookTitle { get; init; } = "";
        public string? Subtitle { get; init; }
        public string? Description { get; init; }
        public string? Genre { get; init; }
        public string? AuthorName { get; init; }
        public string? ChapterTitle { get; init; }
        public int? ChapterNumber { get; init; }
        public int? DisplayChapterNumber { get; init; }

        public PlaceholderContext WithChapter(string chapterTitle, int displayNumber, int storageChapterNumber) => new()
        {
            BookTitle = BookTitle,
            Subtitle = Subtitle,
            Description = Description,
            Genre = Genre,
            AuthorName = AuthorName,
            ChapterTitle = chapterTitle,
            DisplayChapterNumber = displayNumber,
            ChapterNumber = storageChapterNumber
        };
    }

    private static readonly Regex LikelyHtmlRegex = new(@"<\s*\w+[\s\S]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlaceholderBracketRegex = new(@"\[\s*([^\]]+?)\s*\]", RegexOptions.Compiled);
    private static readonly Regex PlaceholderMustacheRegex = new(@"\{\{\s*([^}]+?)\s*\}\}", RegexOptions.Compiled);
    private static readonly Regex HrLineRegex = new(@"^(?:\-{3,}|\*{3,}|_{3,})$", RegexOptions.Compiled);
    private static readonly Regex MdHeadingRegex = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "hr", "ul", "ol", "li", "strong", "b", "em", "i", "u",
        "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "code", "pre", "span", "div", "img"
    };

    private static readonly HashSet<string> AllowedImgAttrs = new(StringComparer.OrdinalIgnoreCase)
    {
        "src", "alt", "width", "height", "class", "style", "title"
    };

    public static string NormalizeManuscriptEscapes(string? str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        var s = str.Replace("\r\n", "\n").Replace("\r", "\n");
        string prev;
        do
        {
            prev = s;
            s = s.Replace("\\r\\n", "\n", StringComparison.Ordinal)
                .Replace("\\n", "\n", StringComparison.Ordinal)
                .Replace("\\r", "\n", StringComparison.Ordinal);
        } while (!string.Equals(s, prev, StringComparison.Ordinal));
        return s;
    }

    public static string ApplyPlaceholders(string? raw, PlaceholderContext? ctx)
    {
        if (raw == null || ctx == null) return raw ?? "";
        var s = raw;
        string? Lookup(string key)
        {
            var nk = NormPlaceholderKey(key);
            return nk switch
            {
                "title" or "booktitle" => ctx.BookTitle ?? "",
                "subtitle" => ctx.Subtitle ?? "",
                "description" => ctx.Description ?? "",
                "genre" => ctx.Genre ?? "",
                "author" or "authorname" => ctx.AuthorName ?? "",
                "chaptertitle" or "chapter" => ctx.ChapterTitle ?? "",
                "chapternumber" or "chapterno" => ctx.ChapterNumber?.ToString() ?? "",
                "displaychapternumber" => ctx.DisplayChapterNumber?.ToString() ?? "",
                _ => null
            };
        }

        s = PlaceholderBracketRegex.Replace(s, m =>
        {
            var v = Lookup(m.Groups[1].Value);
            return v ?? m.Value;
        });
        s = PlaceholderMustacheRegex.Replace(s, m =>
        {
            var v = Lookup(m.Groups[1].Value);
            return v ?? m.Value;
        });
        return s;
    }

    private static string NormPlaceholderKey(string key)
    {
        var parts = (key ?? "").Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts);
    }

    public static bool IsLikelyHtml(string? s) => !string.IsNullOrEmpty(s) && LikelyHtmlRegex.IsMatch(s);

    private static bool IsSafeImageSrc(string? src)
    {
        if (string.IsNullOrWhiteSpace(src)) return false;
        var s = src.Trim();
        if (s.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.StartsWith("/", StringComparison.Ordinal)) return true;
        return false;
    }

    public static string EscapeHtml(string? str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return WebUtility.HtmlEncode(str);
    }

    public static string SanitizeHtml(string unsafeHtml)
    {
        if (string.IsNullOrWhiteSpace(unsafeHtml)) return "";
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(unsafeHtml);
            foreach (var el in doc.DocumentNode.DescendantsAndSelf().ToList())
            {
                if (el.NodeType != HtmlNodeType.Element) continue;
                var name = el.Name.ToLowerInvariant();
                if (name is "#document" or "html" or "head" or "body") continue;
                if (!AllowedTags.Contains(name))
                {
                    var replacement = HtmlNode.CreateNode(EscapeHtml(el.InnerText));
                    el.ParentNode?.ReplaceChild(replacement, el);
                    continue;
                }

                var attrs = el.Attributes.ToList();
                foreach (var attr in attrs)
                {
                    var an = attr.Name.ToLowerInvariant();
                    if (name == "img")
                    {
                        if (!AllowedImgAttrs.Contains(an))
                        {
                            el.Attributes.Remove(attr);
                            continue;
                        }

                        if (an == "src" && !IsSafeImageSrc(attr.Value))
                        {
                            el.Attributes.Remove(attr);
                            continue;
                        }

                        if (an == "style" && Regex.IsMatch(attr.Value ?? "", @"expression\s*\(|javascript:", RegexOptions.IgnoreCase))
                            el.Attributes.Remove(attr);
                        continue;
                    }

                    if (an != "class" && an != "style")
                    {
                        el.Attributes.Remove(attr);
                        continue;
                    }

                    if (an == "style" && Regex.IsMatch(attr.Value ?? "", @"expression\s*\(|javascript:|url\s*\(", RegexOptions.IgnoreCase))
                        el.Attributes.Remove(attr);
                }

                if (name == "img" && string.IsNullOrWhiteSpace(el.GetAttributeValue("src", "")))
                    el.Remove();
            }

            return doc.DocumentNode.InnerHtml;
        }
        catch
        {
            return EscapeHtml(unsafeHtml);
        }
    }

    public static string FormatBodyToHtml(string? raw)
    {
        var s = NormalizeManuscriptEscapes(raw);
        if (string.IsNullOrWhiteSpace(s))
            return """<p class="manuscript-p">No content available.</p>""";

        if (IsLikelyHtml(s))
        {
            var htmlNorm = NormalizeManuscriptEscapes(s);
            return SanitizeHtml(htmlNorm);
        }

        var lines = s.Split('\n');
        var outParts = new List<string>();
        var para = new List<string>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            var inner = string.Join("<br/>", para.Select(EscapeHtml));
            outParts.Add($"""<p class="manuscript-p">{inner}</p>""");
            para.Clear();
        }

        foreach (var line in lines)
        {
            var t = line.Trim();
            if (string.IsNullOrEmpty(t))
            {
                FlushPara();
                continue;
            }

            var hm = MdHeadingRegex.Match(t);
            if (hm.Success)
            {
                FlushPara();
                var level = hm.Groups[1].Value.Length;
                var text = EscapeHtml(hm.Groups[2].Value.Trim());
                outParts.Add($"""<h{level} class="manuscript-heading manuscript-h{level}">{text}</h{level}>""");
                continue;
            }

            if (HrLineRegex.IsMatch(t))
            {
                FlushPara();
                outParts.Add("""<hr class="manuscript-hr" />""");
                continue;
            }

            para.Add(line);
        }

        FlushPara();
        return outParts.Count > 0
            ? string.Join("", outParts)
            : """<p class="manuscript-p">No content available.</p>""";
    }

    public static PlaceholderContext CreateBaseContext(
        string bookTitle,
        string? subtitle,
        string? description,
        string? genre,
        string? authorName) => new()
    {
        BookTitle = bookTitle ?? "",
        Subtitle = subtitle,
        Description = description,
        Genre = genre,
        AuthorName = authorName
    };

    private static readonly Regex ChapterBannerRegex = new(
        @"^\s*(chapter|ch\.?)\s*[0-9IVXLCMDivxlcdm]+\s*[\s:\.\-\u2013\u2014–—]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Mirrors formatter preview: placeholders, HTML conversion, then strip duplicate chapter-opening headings.
    /// </summary>
    public static string PrepareChapterBodyForExport(string? rawContent, PlaceholderContext ph, string? chapterDisplayTitle)
    {
        var bodyRaw = ApplyPlaceholders(rawContent ?? "", ph);
        var bodyHtml = FormatBodyToHtml(bodyRaw);
        return StripRedundantChapterOpenings(bodyHtml, chapterDisplayTitle);
    }

    /// <summary>Remove leading headings that duplicate the chapter title (formatter preview behaviour).</summary>
    public static string StripDuplicateLeadingHeading(string? title, string? contentHtml)
    {
        var titleText = NormalizeHeadingCompareKey(title);
        var html = (contentHtml ?? "").Trim();
        if (string.IsNullOrEmpty(titleText) || string.IsNullOrEmpty(html))
            return html;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"wrap\">" + html + "</div>");
            var wrap = doc.GetElementbyId("wrap");
            if (wrap == null) return html;

            while (true)
            {
                var first = wrap.ChildNodes.FirstOrDefault(n =>
                    n.NodeType == HtmlNodeType.Element && HeadingTags.Contains(n.Name));
                if (first == null) break;

                var headKey = NormalizeHeadingCompareKey(first.InnerText);
                if (string.IsNullOrEmpty(headKey)) break;

                var titleIsChapter = ChapterBannerRegex.IsMatch(titleText);
                var headIsChapter = ChapterBannerRegex.IsMatch(headKey);
                var matches = headKey == titleText
                              || (titleIsChapter && headIsChapter && headKey == titleText);
                if (!matches) break;

                first.Remove();
            }

            return string.Concat(wrap.ChildNodes.Select(n => n.OuterHtml)).Trim();
        }
        catch
        {
            return html;
        }
    }

    /// <summary>Strip AI-writer chapter banners and duplicate titles so PDF matches formatter preview.</summary>
    public static string StripRedundantChapterOpenings(string? contentHtml, string? chapterDisplayTitle)
    {
        var html = StripDuplicateLeadingHeading(chapterDisplayTitle, contentHtml ?? "");
        if (string.IsNullOrWhiteSpace(html)) return html;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"wrap\">" + html + "</div>");
            var wrap = doc.GetElementbyId("wrap");
            if (wrap == null) return html;

            var displayKey = NormalizeHeadingCompareKey(chapterDisplayTitle);

            while (true)
            {
                var node = wrap.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
                if (node == null) break;

                if (node.Name.Equals("hr", StringComparison.OrdinalIgnoreCase))
                {
                    node.Remove();
                    continue;
                }

                if (!HeadingTags.Contains(node.Name))
                    break;

                var cls = node.GetAttributeValue("class", "");
                var textKey = NormalizeHeadingCompareKey(node.InnerText);
                var isChapterBanner = cls.Contains("manuscript-chapter-heading", StringComparison.OrdinalIgnoreCase)
                                      || ChapterBannerRegex.IsMatch(textKey);
                var duplicatesTitle = !string.IsNullOrEmpty(displayKey)
                                      && !string.IsNullOrEmpty(textKey)
                                      && textKey == displayKey;

                if (!isChapterBanner && !duplicatesTitle)
                    break;

                node.Remove();
            }

            return string.Concat(wrap.ChildNodes.Select(n => n.OuterHtml)).Trim();
        }
        catch
        {
            return html;
        }
    }

    private static readonly HashSet<string> HeadingTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "h1", "h2", "h3", "h4", "h5", "h6"
    };

    private static string NormalizeHeadingCompareKey(string? text) =>
        Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
}
