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
        "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "code", "pre", "span", "div", "img",
        "figure", "figcaption", "sup", "sub", "small", "a",
        "table", "thead", "tbody", "tr", "th", "td", "caption",
        // Keep structured chapter chrome so we can strip it cleanly (do not flatten to InnerText).
        "header", "article", "nav", "section"
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
        // Local materialised figures for Chromium print (inlined to data: before SetContent).
        if (s.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) return true;
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
                    // Never dump chapter-opener InnerText into the body (that re-creates a duplicate heading).
                    var cls = el.GetAttributeValue("class", "") ?? "";
                    if (cls.Contains("fmt-chapter-opener", StringComparison.OrdinalIgnoreCase)
                        || cls.Contains("writer-chapter-opener", StringComparison.OrdinalIgnoreCase)
                        || cls.Contains("manuscript-chapter-heading", StringComparison.OrdinalIgnoreCase)
                        || cls.Contains("reader-page-title", StringComparison.OrdinalIgnoreCase))
                    {
                        el.Remove();
                        continue;
                    }

                    // Unwrap in document order. Replacing the element with InnerText
                    // drops nested markup and can move inline runs after the plain text.
                    var parent = el.ParentNode;
                    if (parent == null)
                    {
                        el.Remove();
                        continue;
                    }

                    foreach (var child in el.ChildNodes.ToList())
                        parent.InsertBefore(child, el);
                    el.Remove();
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

            return BookHtmlNormalizer.NormalizeFragment(doc.DocumentNode.InnerHtml);
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
            return SanitizeHtml(ChapterDocumentImportService.PromoteHeadingParagraphs(htmlNorm));
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

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
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

            var precededByBreak = i == 0 || string.IsNullOrWhiteSpace(lines[i - 1]);
            if (precededByBreak && ChapterDocumentImportService.LooksLikeStandaloneHeading(t))
            {
                FlushPara();
                outParts.Add($"""<h2 class="manuscript-heading manuscript-h2">{EscapeHtml(t)}</h2>""");
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
    /// Mirrors formatter preview: placeholders, strip duplicate chapter chrome, HTML conversion, strip again.
    /// Strip runs before sanitize so &lt;header class="fmt-chapter-opener"&gt; is not flattened into body text.
    /// </summary>
    public static string PrepareChapterBodyForExport(string? rawContent, PlaceholderContext ph, string? chapterDisplayTitle)
    {
        var cleaned = ChapterContentNormalizer.NormalizeForManuscript(rawContent);
        var bodyRaw = ApplyPlaceholders(cleaned, ph);
        // Remove saved openers / CHAPTER banners before SanitizeHtml can flatten <header> to plain text.
        bodyRaw = StripRedundantChapterOpenings(bodyRaw, chapterDisplayTitle);
        var bodyHtml = FormatBodyToHtml(bodyRaw);
        bodyHtml = StripRedundantChapterOpenings(bodyHtml, chapterDisplayTitle);
        bodyHtml = ChapterDocumentImportService.PromoteHeadingParagraphs(bodyHtml);
        bodyHtml = StripRedundantChapterOpenings(bodyHtml, chapterDisplayTitle);
        return WrapHeadingsWithFollowingContent(bodyHtml);
    }

    /// <summary>
    /// Wrap each in-body heading with its following block so Chromium PDF never orphans a heading alone on a page.
    /// Rebuilds markup (does not AppendChild across HAP documents — that clones and doubles every heading).
    /// </summary>
    public static string WrapHeadingsWithFollowingContent(string? contentHtml)
    {
        var html = (contentHtml ?? "").Trim();
        if (string.IsNullOrEmpty(html)) return html;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"wrap\">" + html + "</div>");
            var wrap = doc.GetElementbyId("wrap");
            if (wrap == null) return html;

            var children = wrap.ChildNodes
                .Where(n => n.NodeType == HtmlNodeType.Element)
                .ToList();

            var parts = new List<string>(children.Count);
            for (var i = 0; i < children.Count; i++)
            {
                var node = children[i];
                if (!HeadingTags.Contains(node.Name))
                {
                    parts.Add(node.OuterHtml);
                    continue;
                }

                // Include consecutive heading cluster + first following body block.
                var end = i + 1;
                while (end < children.Count && HeadingTags.Contains(children[end].Name))
                    end++;
                if (end >= children.Count)
                {
                    for (var k = i; k < children.Count; k++)
                        parts.Add(children[k].OuterHtml);
                    break;
                }

                var inner = string.Concat(
                    children.Skip(i).Take(end - i + 1).Select(n => n.OuterHtml));
                parts.Add("""<div class="manuscript-keep-next">""" + inner + "</div>");
                i = end;
            }

            return string.Concat(parts).Trim();
        }
        catch
        {
            return html;
        }
    }

    /// <summary>Remove leading headings that duplicate the chapter title (formatter preview behaviour).</summary>
    public static string StripDuplicateLeadingHeading(string? title, string? contentHtml)
    {
        var titleText = NormalizeHeadingCompareKey(title);
        var html = (contentHtml ?? "").Trim();
        if (string.IsNullOrEmpty(html))
            return html;

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"wrap\">" + html + "</div>");
            var wrap = doc.GetElementbyId("wrap");
            if (wrap == null) return html;

            while (true)
            {
                var first = wrap.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlNodeType.Element);
                if (first == null) break;

                // Unwrap keep-next so we can strip a duplicated title heading inside it.
                if (first.Name.Equals("div", StringComparison.OrdinalIgnoreCase)
                    && (first.GetAttributeValue("class", "") ?? "").Contains("manuscript-keep-next", StringComparison.OrdinalIgnoreCase))
                {
                    var innerKids = first.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element).ToList();
                    foreach (var kid in innerKids)
                        first.ParentNode.InsertBefore(kid, first);
                    first.Remove();
                    continue;
                }

                // Structured openers already rendered into saved HTML (preview/PDF would double them).
                if (IsStructuredChapterOpener(first))
                {
                    first.Remove();
                    continue;
                }

                if (HeadingTags.Contains(first.Name))
                {
                    var headKey = NormalizeHeadingCompareKey(first.InnerText);
                    if (string.IsNullOrEmpty(headKey)) break;

                    if (HeadingsMatch(titleText, headKey) || ChapterBannerRegex.IsMatch(headKey))
                    {
                        first.Remove();
                        continue;
                    }
                    break;
                }

                // Plain <p> title line that will later promote to <h2>.
                if (first.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    var pKey = NormalizeHeadingCompareKey(first.InnerText);
                    if (!string.IsNullOrEmpty(pKey)
                        && (HeadingsMatch(titleText, pKey) || ChapterBannerRegex.IsMatch(pKey))
                        && ChapterDocumentImportService.LooksLikeStandaloneHeading(first.InnerText.Trim()))
                    {
                        first.Remove();
                        continue;
                    }
                }

                break;
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

                if (IsStructuredChapterOpener(node))
                {
                    node.Remove();
                    continue;
                }

                if (HeadingTags.Contains(node.Name))
                {
                    var cls = node.GetAttributeValue("class", "");
                    var textKey = NormalizeHeadingCompareKey(node.InnerText);
                    var isChapterBanner = cls.Contains("manuscript-chapter-heading", StringComparison.OrdinalIgnoreCase)
                                          || ChapterBannerRegex.IsMatch(textKey);
                    var duplicatesTitle = HeadingsMatch(displayKey, textKey);

                    if (!isChapterBanner && !duplicatesTitle)
                        break;

                    node.Remove();
                    continue;
                }

                // Standalone paragraph that repeats "CHAPTER N" / chapter title.
                if (node.Name.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    var pKey = NormalizeHeadingCompareKey(node.InnerText);
                    if (!string.IsNullOrEmpty(pKey)
                        && (ChapterBannerRegex.IsMatch(pKey) || HeadingsMatch(displayKey, pKey))
                        && ChapterDocumentImportService.LooksLikeStandaloneHeading(node.InnerText.Trim()))
                    {
                        node.Remove();
                        continue;
                    }
                }

                break;
            }

            return string.Concat(wrap.ChildNodes.Select(n => n.OuterHtml)).Trim();
        }
        catch
        {
            return html;
        }
    }

    private static bool IsStructuredChapterOpener(HtmlNode node)
    {
        if (node == null) return false;
        var name = node.Name ?? "";
        var cls = node.GetAttributeValue("class", "") ?? "";
        if (name.Equals("header", StringComparison.OrdinalIgnoreCase)
            && (cls.Contains("fmt-chapter-opener", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("writer-chapter-opener", StringComparison.OrdinalIgnoreCase)
                || cls.Contains("manuscript-chapter-heading", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (name.Equals("article", StringComparison.OrdinalIgnoreCase)
            && cls.Contains("reader-page-title", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>True when two heading strings refer to the same chapter title (ignores Chapter N: prefixes).</summary>
    private static bool HeadingsMatch(string? a, string? b)
    {
        var ka = NormalizeHeadingCompareKey(a);
        var kb = NormalizeHeadingCompareKey(b);
        if (string.IsNullOrEmpty(ka) || string.IsNullOrEmpty(kb)) return false;
        if (ka == kb) return true;

        var sa = NormalizeHeadingCompareKey(ChapterBannerRegex.Replace(ka, ""));
        var sb = NormalizeHeadingCompareKey(ChapterBannerRegex.Replace(kb, ""));
        if (!string.IsNullOrEmpty(sa) && !string.IsNullOrEmpty(sb) && sa == sb)
            return true;
        if (!string.IsNullOrEmpty(sa) && sa == kb) return true;
        if (!string.IsNullOrEmpty(sb) && sb == ka) return true;
        return false;
    }

    private static readonly HashSet<string> HeadingTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "h1", "h2", "h3", "h4", "h5", "h6"
    };

    private static string NormalizeHeadingCompareKey(string? text) =>
        Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    /// <summary>
    /// Drop empty / whitespace-only paragraphs and heading shells left by Word/PDF import
    /// (blank pages and large gaps in the formatter preview).
    /// </summary>
    public static string StripEmptyHtmlBlocks(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var cleaned = html;
        // Empty block tags: <p></p>, <p>&nbsp;</p>, <p><br></p>, empty headings, etc.
        cleaned = Regex.Replace(
            cleaned,
            @"<(p|div|h[1-6]|li|span)(\s[^>]*)?>\s*(?:&nbsp;|&#160;|&emsp;|&ensp;|<br\s*/?>|\u00a0|\s)*</\1>",
            string.Empty,
            RegexOptions.IgnoreCase);
        // Collapse runs of blank lines left after removals
        cleaned = Regex.Replace(cleaned, @"(?:\s*<br\s*/?>\s*){3,}", "<br/>", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"(\s*\n){3,}", "\n\n");
        return cleaned.Trim();
    }
}
