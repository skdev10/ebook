using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace EBookDashboard.Services;

/// <summary>Strict whitelist sanitizer for AI whole-book HTML fragments (PDF/formatter safe).</summary>
public static class BookGenerationHtmlSanitizer
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "h1", "h2", "h3", "p", "blockquote", "ul", "ol", "li", "hr", "strong", "em", "br"
    };

    private static readonly Regex CodeFenceRegex = new(
        @"^\s*```(?:html)?\s*\r?\n?|\r?\n?```\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Strips markdown code fences and preamble; returns trimmed HTML fragment.</summary>
    public static string StripCodeFences(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();
        s = CodeFenceRegex.Replace(s, "");
        var htmlStart = s.IndexOf('<');
        if (htmlStart > 0)
            s = s[htmlStart..];
        return s.Trim();
    }

    /// <summary>Whitelist tags only; removes all attributes and disallowed elements.</summary>
    public static string Sanitize(string? raw)
    {
        var s = StripCodeFences(raw);
        if (string.IsNullOrWhiteSpace(s)) return "";

        try
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(s);
            foreach (var el in doc.DocumentNode.DescendantsAndSelf().ToList())
            {
                if (el.NodeType != HtmlNodeType.Element) continue;
                var name = el.Name.ToLowerInvariant();
                if (name is "#document" or "html" or "head" or "body")
                    continue;

                if (!AllowedTags.Contains(name))
                {
                    if (name == "b")
                    {
                        el.Name = "strong";
                        name = "strong";
                    }
                    else if (name == "i")
                    {
                        el.Name = "em";
                        name = "em";
                    }
                    else
                    {
                        var parent = el.ParentNode;
                        if (parent == null) continue;
                        if (el.ChildNodes.Count == 0)
                        {
                            el.Remove();
                            continue;
                        }

                        var children = el.ChildNodes.ToList();
                        foreach (var child in children)
                            parent.InsertBefore(child, el);
                        el.Remove();
                        continue;
                    }
                }

                el.Attributes.RemoveAll();
            }

            var inner = doc.DocumentNode.InnerHtml.Trim();
            return inner;
        }
        catch
        {
            return BookManuscriptHtmlFormatter.EscapeHtml(s);
        }
    }
}
