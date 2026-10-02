using System.Net;
using System.Text;
using HtmlAgilityPack;

namespace EBookDashboard.Services;

/// <summary>
/// Splits one block at a word boundary while keeping inline ancestors (em, sup, span)
/// open on both halves. The concatenation of the two halves' text equals the original
/// text, character for character, in document order.
/// </summary>
public static class HtmlParagraphSplitter
{
    /// <summary>Document-order text, including whitespace. Tags are not included.</summary>
    public static string VisibleText(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        var doc = new HtmlDocument();
        doc.LoadHtml("<div id=\"s\">" + html + "</div>");
        var root = doc.GetElementbyId("s");
        if (root == null)
            return html;
        return string.Concat(EnumerateText(root).Select(t => HtmlEntity.DeEntitize(t.Text ?? "")));
    }

    public static int CountWords(string? html)
    {
        var text = VisibleText(html);
        var n = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                continue;
            while (i < text.Length && !char.IsWhiteSpace(text[i]))
                i++;
            n++;
            i--;
        }

        return n;
    }

    /// <summary>Keep <paramref name="wordsOnLeft"/> words on the left. The whitespace after that word stays on the right.</summary>
    public static (string Left, string Right) SplitAtWord(string blockHtml, int wordsOnLeft)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<div id=\"s\">" + (blockHtml ?? "") + "</div>");
        var root = doc.GetElementbyId("s") ?? throw new InvalidOperationException("Empty fragment.");
        var block = root.ChildNodes.FirstOrDefault(n => n.NodeType == HtmlNodeType.Element)
                    ?? throw new InvalidOperationException("Block element required.");

        var left = new StringBuilder();
        var right = new StringBuilder();
        var stack = new List<Frame>();
        var wordsKept = 0;
        var onRight = wordsOnLeft <= 0;

        void Switch()
        {
            if (onRight)
                return;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (!stack[i].ClosedOnLeft)
                {
                    left.Append(stack[i].Close);
                    stack[i].ClosedOnLeft = true;
                }
            }

            foreach (var frame in stack)
                right.Append(frame.Open);
            onRight = true;
        }

        void WriteText(string? raw)
        {
            var s = raw ?? "";
            var i = 0;
            while (i < s.Length)
            {
                if (onRight)
                {
                    (onRight ? right : left).Append(WebUtility.HtmlEncode(s[i..]));
                    return;
                }

                var ws = i;
                while (i < s.Length && char.IsWhiteSpace(s[i]))
                    i++;
                if (i > ws)
                    left.Append(WebUtility.HtmlEncode(s[ws..i]));
                if (i >= s.Length)
                    return;

                var start = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]))
                    i++;
                left.Append(WebUtility.HtmlEncode(s[start..i]));
                wordsKept++;
                if (wordsKept >= wordsOnLeft)
                    Switch();
            }
        }

        void Walk(HtmlNode node)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child is HtmlTextNode text)
                {
                    WriteText(text.Text);
                    continue;
                }

                if (child.NodeType != HtmlNodeType.Element)
                    continue;

                var open = OpenTag(child);
                var close = "</" + child.Name + ">";
                var frame = new Frame(open, close);
                var side = onRight ? right : left;
                side.Append(open);
                if (onRight)
                    frame.OpenedOnRight = true;
                stack.Add(frame);
                Walk(child);
                stack.RemoveAt(stack.Count - 1);
                if (frame.OpenedOnRight || frame.ClosedOnLeft)
                    right.Append(close);
                else
                    left.Append(close);
            }
        }

        var rootOpen = OpenTag(block);
        var rootClose = "</" + block.Name + ">";
        left.Append(rootOpen);
        stack.Add(new Frame(rootOpen, rootClose));
        Walk(block);
        if (!onRight)
            left.Append(rootClose);
        else
        {
            if (!stack[0].ClosedOnLeft)
                left.Append(rootClose);
            right.Append(rootClose);
        }

        var rightHtml = right.Length == 0 || right.ToString() == rootOpen + rootClose ? "" : right.ToString();
        return (left.ToString(), rightHtml);
    }

    private static string OpenTag(HtmlNode el)
    {
        var sb = new StringBuilder();
        sb.Append('<').Append(el.Name);
        foreach (var attr in el.Attributes)
        {
            sb.Append(' ').Append(attr.Name).Append("=\"");
            sb.Append(WebUtility.HtmlEncode(attr.Value));
            sb.Append('"');
        }

        sb.Append('>');
        return sb.ToString();
    }

    private static IEnumerable<HtmlTextNode> EnumerateText(HtmlNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child is HtmlTextNode text)
                yield return text;
            else if (child.NodeType == HtmlNodeType.Element)
            {
                foreach (var nested in EnumerateText(child))
                    yield return nested;
            }
        }
    }

    private sealed class Frame
    {
        public Frame(string open, string close)
        {
            Open = open;
            Close = close;
        }

        public string Open { get; }
        public string Close { get; }
        public bool ClosedOnLeft { get; set; }
        public bool OpenedOnRight { get; set; }
    }
}
