using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Models.DTO;
using HtmlAgilityPack;

namespace EBookDashboard.Services;

/// <summary>
/// One normalization pass for every upload: keep semantic inline markup (including
/// sup/sub), drop converter junk, and merge headings that were split by a line break.
/// Layout must not run before this.
/// </summary>
public static class BookHtmlNormalizer
{
    private static readonly HashSet<string> SemanticTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "hr", "ul", "ol", "li", "strong", "b", "em", "i", "u",
        "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "code", "pre", "span", "div",
        "img", "figure", "figcaption", "header", "article", "nav", "section",
        "sup", "sub", "small", "a", "table", "thead", "tbody", "tr", "th", "td", "caption"
    };

    private static readonly Regex VersionLabelRegex = new(
        @"^v?\d+\.\d+(?:\.\d+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Clean a fragment. Plain text (no tags) is returned unchanged.</summary>
    public static string NormalizeFragment(string? html)
    {
        var raw = html ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        if (raw.IndexOf('<') < 0)
            return raw;

        try
        {
            var doc = new HtmlDocument { OptionFixNestedTags = true };
            doc.LoadHtml("<div id=\"norm-root\">" + raw + "</div>");
            var root = doc.GetElementbyId("norm-root");
            if (root == null)
                return raw;

            foreach (var el in root.DescendantsAndSelf().ToList())
            {
                if (el.NodeType != HtmlNodeType.Element)
                    continue;
                var name = el.Name.ToLowerInvariant();
                if (name is "#document" or "html" or "head" or "body" or "div" && el.Id == "norm-root")
                    continue;

                if (IsConverterPageBreak(el) || IsVersionLabel(el))
                {
                    el.Remove();
                    continue;
                }

                StripConverterAttributes(el);

                if (!SemanticTags.Contains(name))
                {
                    Unwrap(el);
                    continue;
                }

                if (name == "span" && IsEmptySpan(el))
                    el.Remove();
            }

            MergeBreaksInsideHeadings(root);
            MergeSplitHeadings(root);
            return string.Concat(root.ChildNodes.Select(n => n.OuterHtml)).Trim();
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>True for preface / conclusion style openings that may use a drop cap. Index, TOC, copyright, credits, and lists may not.</summary>
    public static bool AllowsDropCap(string? title)
    {
        var key = BookChapterExportHelper.NormalizeFrontMatterTitleKey(title);
        if (string.IsNullOrEmpty(key))
            return true;
        if (key is "index" or "contents" or "table of contents" or "toc"
            or "copyright" or "copyright page" or "title page" or "half title"
            or "illustration credits" or "illustration credit" or "credits"
            or "acknowledgments" or "acknowledgements" or "bibliography"
            or "glossary" or "references" or "notes" or "endnotes"
            or "about the author" or "about the authors")
            return false;

        if (BookChapterExportHelper.IsBackMatterSectionTitle(title))
            return key is "conclusion" or "epilogue" or "afterword"
                   || key.StartsWith("conclusion ", StringComparison.Ordinal)
                   || key.StartsWith("epilogue ", StringComparison.Ordinal);

        if (BookChapterExportHelper.IsFrontMatterSectionTitle(title))
            return key is "preface" or "foreword" or "introduction" or "prologue"
                   || key.StartsWith("preface ", StringComparison.Ordinal)
                   || key.StartsWith("foreword ", StringComparison.Ordinal)
                   || key.StartsWith("introduction ", StringComparison.Ordinal)
                   || key.StartsWith("prologue ", StringComparison.Ordinal);

        return true;
    }

    /// <summary>
    /// Drop source title pages and tables of contents (we generate those).
    /// Copyright stays when <paramref name="keepOriginalCopyright"/> is true.
    /// </summary>
    public static List<ChapterDto> OmitDuplicateFrontMatter(
        IReadOnlyList<ChapterDto>? chapters,
        bool keepOriginalCopyright,
        string? bookTitle = null)
    {
        var list = chapters ?? Array.Empty<ChapterDto>();
        var titleKey = Norm(bookTitle);
        return list.Where(ch =>
        {
            if (IsTitlePage(ch, titleKey) && !ContainsFigure(ch))
                return false;
            if (IsTableOfContents(ch))
                return false;
            if (IsCopyright(ch) && !keepOriginalCopyright)
                return false;
            return true;
        }).ToList();
    }

    private static bool ContainsFigure(ChapterDto chapter) =>
        (chapter.Content ?? "").Contains("<img", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The title-page image and the copyright prose often share one imported chapter.
    /// Split them into the printed title page and the verso copyright page, and drop
    /// the source contents list that follows the copyright text.
    /// </summary>
    public static void TakePrintedFrontMatter(
        List<ChapterDto> chapters,
        bool useSourceTitlePage,
        bool keepOriginalCopyright,
        out string? titleHtml,
        out string? copyrightHtml)
    {
        titleHtml = null;
        copyrightHtml = null;
        var index = chapters.FindIndex(ch => IsTitlePage(ch) && ContainsFigure(ch));
        if (index < 0)
            index = chapters.FindIndex(ch =>
                ContainsFigure(ch)
                && (ch.Content ?? "").Contains("all rights reserved", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return;

        var content = chapters[index].Content ?? "";
        var art = TitleArtworkHtml(content);
        if (useSourceTitlePage && !string.IsNullOrWhiteSpace(art))
            titleHtml = "<div class=\"title-page source-title-page book-preview-sheet\">" + art + "</div>";

        var prose = RemoveTitlePageLabels(RemoveFigures(content));
        prose = RemoveImportedContentsRun(prose);
        chapters.RemoveAt(index);
        if (!keepOriginalCopyright || !prose.Contains("all rights reserved", StringComparison.OrdinalIgnoreCase))
            return;
        copyrightHtml = "<div class=\"front-matter-page copyright-page source-copyright book-preview-sheet\"><div class=\"copyright-block\">"
                        + prose + "</div></div>";
    }

    /// <summary>
    /// Pull a source title-page image out of the chapter list. When
    /// <paramref name="useSourceTitlePage"/> is false the artwork chapter is dropped.
    /// </summary>
    public static string? PullSourceTitlePage(List<ChapterDto> chapters, bool useSourceTitlePage)
    {
        var index = chapters.FindIndex(ch => IsTitlePage(ch) && ContainsFigure(ch));
        if (index < 0)
            return null;
        var content = chapters[index].Content;
        chapters.RemoveAt(index);
        if (!useSourceTitlePage)
            return null;
        var art = TitleArtworkHtml(content);
        if (string.IsNullOrWhiteSpace(art))
            return null;
        return "<div class=\"title-page source-title-page book-preview-sheet\">" + art + "</div>";
    }

    /// <summary>Figures only. The words "Title Page" are not printed.</summary>
    public static string TitleArtworkHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "";
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"tp\">" + html + "</div>");
            var root = doc.GetElementbyId("tp");
            if (root == null)
                return "";
            var kept = new StringBuilder();
            foreach (var node in root.Descendants("figure").ToList())
                kept.Append(node.OuterHtml);
            if (kept.Length == 0)
            {
                foreach (var img in root.Descendants("img").ToList())
                    kept.Append(img.OuterHtml);
            }
            return kept.ToString();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Pull the imported copyright chapter out of the list and wrap it as the verso page.
    /// The source contents list is removed so only the generated contents page remains.
    /// </summary>
    public static string? PullCopyrightPage(List<ChapterDto> chapters, bool keepOriginal)
    {
        var index = chapters.FindIndex(IsCopyright);
        if (index < 0 || !keepOriginal)
            return null;
        var body = RemoveImportedContentsRun(chapters[index].Content);
        chapters.RemoveAt(index);
        if (string.IsNullOrWhiteSpace(body))
            return null;
        return "<div class=\"front-matter-page copyright-page source-copyright book-preview-sheet\"><div class=\"copyright-block\">"
               + body + "</div></div>";
    }

    private static string RemoveFigures(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"root\">" + html + "</div>");
            var root = doc.GetElementbyId("root");
            if (root == null)
                return html;
            foreach (var node in root.Descendants("figure").ToList())
                node.Remove();
            foreach (var node in root.Descendants("img").ToList())
                node.Remove();
            return root.InnerHtml;
        }
        catch
        {
            return html;
        }
    }

    private static string RemoveTitlePageLabels(string html)
    {
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"root\">" + html + "</div>");
            var root = doc.GetElementbyId("root");
            if (root == null)
                return html;
            foreach (var node in root.Descendants().Where(n => n.NodeType == HtmlNodeType.Element).ToList())
            {
                var text = WebUtility.HtmlDecode(node.InnerText ?? "").Trim();
                if (text.Equals("Title Page", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("Half Title", StringComparison.OrdinalIgnoreCase))
                    node.Remove();
            }
            return root.InnerHtml;
        }
        catch
        {
            return html;
        }
    }

    /// <summary>
    /// Copyright prose from a chapter that may also hold the title-page image and the source contents list.
    /// </summary>
    public static string CopyrightProse(string? html)
    {
        var prose = RemoveTitlePageLabels(RemoveFigures(html ?? ""));
        return RemoveImportedContentsRun(prose);
    }

    /// <summary>
    /// Drop a run of contents entries ("1 The Challenge of the Future" … "About the Authors").
    /// A catalog line such as "1. New business enterprises." is not an entry.
    /// </summary>
    public static string RemoveImportedContentsRun(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html ?? "";
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"root\">" + html + "</div>");
            var root = doc.GetElementbyId("root");
            if (root == null)
                return html;
            foreach (var parent in root.DescendantsAndSelf().ToList())
            {
                var nodes = parent.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element).ToList();
                if (nodes.Count < 4)
                    continue;
                var runStart = -1;
                var runLength = 0;
                void Close(int end)
                {
                    if (runLength >= 4 && runStart >= 0)
                    {
                        for (var n = runStart; n < end; n++)
                            nodes[n].Remove();
                    }
                    runStart = -1;
                    runLength = 0;
                }
                for (var i = 0; i < nodes.Count; i++)
                {
                    var text = WebUtility.HtmlDecode(nodes[i].InnerText ?? "").Trim();
                    if (IsContentsEntry(text))
                    {
                        if (runStart < 0)
                            runStart = i;
                        runLength++;
                        continue;
                    }
                    Close(i);
                }
                Close(nodes.Count);
            }
            return root.InnerHtml;
        }
        catch
        {
            return html;
        }
    }

    /// <summary>A contents row, not a Library of Congress catalog line.</summary>
    public static bool IsContentsEntry(string? text)
    {
        var t = Regex.Replace((text ?? "").Trim(), @"\s+", " ");
        if (t.Length == 0 || t.Length > 90)
            return false;
        if (Regex.IsMatch(t, @"^(contents|table of contents)$", RegexOptions.IgnoreCase))
            return true;
        if (Regex.IsMatch(t, @"^\d{1,2}\.\s", RegexOptions.None))
            return false;
        if (Regex.IsMatch(t,
                @"^(preface|foreword|introduction|dedication|epigraph|prologue|conclusion|acknowledgments|acknowledgements|illustration credits|index|about the authors?)(\b|:)",
                RegexOptions.IgnoreCase))
            return true;
        return Regex.IsMatch(t, @"^\d{1,2}\s+\p{L}", RegexOptions.None);
    }

    public static bool IsTitlePage(ChapterDto chapter, string? bookTitleKey = null)
    {
        var key = BookChapterExportHelper.NormalizeFrontMatterTitleKey(chapter.Title);
        if (key is "title page" or "half title" or "titlepage" or "cover")
            return true;
        if (!string.IsNullOrEmpty(bookTitleKey) && key == bookTitleKey && PlainLength(chapter.Content) < 280)
            return true;
        return false;
    }

    public static bool IsCopyright(ChapterDto chapter)
    {
        var key = BookChapterExportHelper.NormalizeFrontMatterTitleKey(chapter.Title);
        if (key.Contains("copyright", StringComparison.Ordinal))
            return true;
        var plain = Plain(chapter.Content);
        if (plain.Length > 4000)
            return false;
        return plain.Contains("all rights reserved", StringComparison.OrdinalIgnoreCase)
               && (plain.Contains("copyright", StringComparison.OrdinalIgnoreCase)
                   || plain.Contains("isbn", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsTableOfContents(ChapterDto chapter)
    {
        if (InteriorFrontMatterBuilder.IsImportedContentsChapter(chapter.Title))
            return true;
        var key = BookChapterExportHelper.NormalizeFrontMatterTitleKey(chapter.Title);
        return key is "contents" or "table of contents" or "toc" or "nav";
    }

    /// <summary>Add <c>drop-cap-start</c> to the first paragraph. The letter itself stays in the word so a short word ("If") keeps its following space.</summary>
    public static string MarkDropCapParagraph(string? html)
    {
        var raw = html ?? "";
        if (string.IsNullOrWhiteSpace(raw) || raw.IndexOf("<p", StringComparison.OrdinalIgnoreCase) < 0)
            return raw;
        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml("<div id=\"dc\">" + raw + "</div>");
            var root = doc.GetElementbyId("dc");
            var p = root?.Descendants("p").FirstOrDefault();
            if (p == null)
                return raw;
            var cls = p.GetAttributeValue("class", "");
            if (cls.Contains("drop-cap-start", StringComparison.Ordinal))
                return raw;
            p.SetAttributeValue("class", string.IsNullOrEmpty(cls) ? "drop-cap-start" : cls + " drop-cap-start");
            return string.Concat(root!.ChildNodes.Select(n => n.OuterHtml));
        }
        catch
        {
            return raw;
        }
    }

    private static void MergeBreaksInsideHeadings(HtmlNode root)
    {
        foreach (var h in root.Descendants().Where(n => n.Name.Length == 2 && n.Name[0] == 'h').ToList())
        {
            foreach (var br in h.Descendants("br").ToList())
            {
                var space = root.OwnerDocument.CreateTextNode(" ");
                br.ParentNode.ReplaceChild(space, br);
            }
        }
    }

    /// <summary>Join "If You Build It, Will They" + "COME?" when the second heading is a leftover fragment.</summary>
    private static void MergeSplitHeadings(HtmlNode root)
    {
        var kids = root.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element).ToList();
        for (var i = 0; i < kids.Count - 1; i++)
        {
            var a = kids[i];
            var b = kids[i + 1];
            if (!IsHeading(a) || !IsHeading(b))
                continue;
            var aText = Collapse(a.InnerText);
            var bText = Collapse(b.InnerText);
            if (!IsHeadingFragment(aText, bText))
                continue;
            var joiner = root.OwnerDocument.CreateTextNode(" ");
            a.AppendChild(joiner);
            foreach (var child in b.ChildNodes.ToList())
                a.AppendChild(child);
            b.Remove();
            kids.RemoveAt(i + 1);
            i--;
        }
    }

    private static bool IsHeadingFragment(string first, string second)
    {
        if (string.IsNullOrEmpty(second) || second.Length > 24)
            return false;
        if (first.EndsWith('.') || first.EndsWith('!') || first.EndsWith('?'))
            return false;
        var words = second.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 2)
            return false;
        var letters = second.Count(char.IsLetter);
        var upper = second.Count(ch => char.IsLetter(ch) && char.IsUpper(ch));
        var allCaps = letters > 0 && upper >= letters;
        return allCaps || second.EndsWith('?') || second.Length <= 12;
    }

    private static bool IsHeading(HtmlNode n) =>
        n.Name.Length == 2 && n.Name[0] is 'h' or 'H' && char.IsDigit(n.Name[1]);

    private static void Unwrap(HtmlNode el)
    {
        var parent = el.ParentNode;
        if (parent == null)
        {
            el.Remove();
            return;
        }

        foreach (var child in el.ChildNodes.ToList())
            parent.InsertBefore(child, el);
        el.Remove();
    }

    private static void StripConverterAttributes(HtmlNode el)
    {
        var cls = el.GetAttributeValue("class", "");
        if (!string.IsNullOrEmpty(cls))
        {
            var kept = cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(c => !c.StartsWith("calibre", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (kept.Length == 0)
                el.Attributes.Remove("class");
            else
                el.SetAttributeValue("class", string.Join(' ', kept));
        }

        var style = el.GetAttributeValue("style", "");
        if (!string.IsNullOrEmpty(style))
        {
            var parts = style.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0
                            && !p.StartsWith("font-size", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith("font-family", StringComparison.OrdinalIgnoreCase)
                            && !p.StartsWith("line-height", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (parts.Length == 0)
                el.Attributes.Remove("style");
            else
                el.SetAttributeValue("style", string.Join("; ", parts));
        }
    }

    private static bool IsConverterPageBreak(HtmlNode el)
    {
        var cls = el.GetAttributeValue("class", "");
        return cls.Contains("calibre_pb", StringComparison.OrdinalIgnoreCase)
               || cls.Contains("pagebreak", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionLabel(HtmlNode el)
    {
        if (el.ChildNodes.Any(n => n.NodeType == HtmlNodeType.Element))
            return false;
        var text = Collapse(el.InnerText);
        return text.Length > 0 && text.Length <= 8 && VersionLabelRegex.IsMatch(text);
    }

    private static bool IsEmptySpan(HtmlNode el) =>
        el.Name.Equals("span", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrWhiteSpace(el.InnerText)
        && !el.Descendants("img").Any();

    private static string Plain(string? html) =>
        Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", " ")), @"\s+", " ").Trim();

    private static int PlainLength(string? html) => Plain(html).Length;

    private static string Norm(string? s) =>
        Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    private static string Collapse(string? s) =>
        Regex.Replace(WebUtility.HtmlDecode(s ?? ""), @"\s+", " ").Trim();
}
