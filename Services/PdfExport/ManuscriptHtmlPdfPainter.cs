using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace EBookDashboard.Services.PdfExport;

/// <summary>Renders sanitized chapter HTML into PDFsharp pages with inline styles, colors, and images.</summary>
public sealed class ManuscriptHtmlPdfPainter
{
    private readonly PdfDocument _document;
    private readonly ExportPdfPageLayout _layout;
    private readonly ExportPdfTypography.TypefaceSet _faces;
    private readonly double _bodyPt;
    private readonly double _lineHeight;
    private readonly string _pageBgHex;

    private PdfPage _page = null!;
    private XGraphics _gfx = null!;
    private double _y;

    private static readonly Regex ColorHexRegex = new(@"#([0-9A-Fa-f]{6})", RegexOptions.Compiled);
    private static readonly Regex FontSizeRegex = new(@"font-size\s*:\s*([0-9.]+)\s*(px|pt)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FontFamilyRegex = new(@"font-family\s*:\s*([^;]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ColorPropRegex = new(@"color\s*:\s*([^;]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ManuscriptHtmlPdfPainter(
        PdfDocument document,
        ExportPdfPageLayout layout,
        ExportPdfTypography.TypefaceSet faces,
        double bodyPt,
        double lineHeight,
        string pageBgHex)
    {
        _document = document;
        _layout = layout;
        _faces = faces;
        _bodyPt = bodyPt;
        _lineHeight = lineHeight;
        _pageBgHex = pageBgHex;
    }

    public void PaintChapter(string heading, string bodyHtml)
    {
        EnsurePage(filledBackground: true);
        DrawHeading(heading);
        if (string.IsNullOrWhiteSpace(bodyHtml)) return;

        var wrap = new HtmlDocument();
        wrap.LoadHtml($"<div id=\"root\">{bodyHtml}</div>");
        var root = wrap.GetElementbyId("root");
        if (root == null) return;

        foreach (var child in root.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element))
            PaintElement(child, new RunStyle(_faces.BodyFamily, _bodyPt, false, false, null));
    }

    private void DrawHeading(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var font = MakeFont(_faces.HeadingFamily, _bodyPt + 2, true, false);
        var brush = XBrushes.Black;
        var rect = ContentRect();
        _gfx.DrawString(text.Trim(), font, brush, rect, XStringFormats.TopCenter);
        _y += font.GetHeight() + _bodyPt * 0.35;
    }

    private void PaintElement(HtmlNode node, RunStyle inherited)
    {
        if (node.Name.Equals("br", StringComparison.OrdinalIgnoreCase))
        {
            _y += _bodyPt * _lineHeight * 0.5;
            return;
        }

        if (node.Name.Equals("img", StringComparison.OrdinalIgnoreCase))
        {
            TryDrawImage(node);
            return;
        }

        if (node.Name.Equals("hr", StringComparison.OrdinalIgnoreCase))
        {
            EnsureSpace(_bodyPt);
            var y = _y + 4;
            _gfx.DrawLine(XPens.LightGray, _layout.MarginLeftPt, y, _layout.PageWidthPt - _layout.MarginRightPt, y);
            _y += 12;
            return;
        }

        var style = inherited.MergeFromNode(node, _faces);
        var isBlock = node.Name is "p" or "div" or "blockquote" or "li"
                      or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

        if (isBlock && node.Name.StartsWith('h') && node.Name.Length == 2)
        {
            var lvl = node.Name[1] - '0';
            var hSize = _bodyPt + Math.Max(0, 4 - lvl);
            style = style with { Family = _faces.HeadingFamily, SizePt = hSize, Bold = true };
            EnsureSpace(hSize * _lineHeight + 4);
            DrawInlineChildren(node, style, center: false);
            _y += hSize * _lineHeight * 0.35;
            return;
        }

        if (isBlock)
        {
            EnsureSpace(_bodyPt * _lineHeight);
            DrawInlineChildren(node, style, center: false);
            _y += _bodyPt * _lineHeight * 0.25;
            return;
        }

        if (node.NodeType == HtmlNodeType.Text)
        {
            DrawTextSegment(node.InnerText, style);
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
                DrawTextSegment(child.InnerText, style);
            else if (child.NodeType == HtmlNodeType.Element)
                PaintElement(child, style);
        }
    }

    private void DrawInlineChildren(HtmlNode node, RunStyle baseStyle, bool center)
    {
        var sb = new System.Text.StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            DrawTextSegment(sb.ToString(), baseStyle, center);
            sb.Clear();
        }

        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                sb.Append(HtmlEntity.DeEntitize(child.InnerText));
                continue;
            }
            if (child.NodeType != HtmlNodeType.Element) continue;
            Flush();
            PaintElement(child, baseStyle);
        }
        Flush();
    }

    private void DrawTextSegment(string? raw, RunStyle style, bool center = false)
    {
        var text = HtmlEntity.DeEntitize(raw ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (text.Length == 0) return;

        var font = MakeFont(style.Family, style.SizePt, style.Bold, style.Italic);
        var brush = style.Brush ?? XBrushes.Black;
        foreach (var line in WrapText(text, font, _layout.ContentWidthPt))
        {
            EnsureSpace(font.GetHeight());
            var rect = ContentRect();
            var format = center ? XStringFormats.TopCenter : XStringFormats.TopLeft;
            _gfx.DrawString(line, font, brush, rect, format);
            _y += font.GetHeight() * _lineHeight;
        }
    }

    private void TryDrawImage(HtmlNode img)
    {
        var src = img.GetAttributeValue("src", "");
        if (string.IsNullOrWhiteSpace(src)) return;
        try
        {
            XImage? image = null;
            if (src.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = src.IndexOf(',');
                if (comma > 0)
                {
                    var b64 = src[(comma + 1)..];
                    var bytes = Convert.FromBase64String(b64);
                    image = XImage.FromStream(new MemoryStream(bytes));
                }
            }
            else if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                var bytes = http.GetByteArrayAsync(src).GetAwaiter().GetResult();
                image = XImage.FromStream(new MemoryStream(bytes));
            }

            if (image == null) return;
            var maxW = _layout.ContentWidthPt;
            var scale = Math.Min(1.0, maxW / image.PixelWidth);
            var h = image.PixelHeight * scale;
            EnsureSpace(h + 8);
            _gfx.DrawImage(image, _layout.MarginLeftPt, _y, maxW, h);
            _y += h + 8;
            image.Dispose();
        }
        catch
        {
            /* skip broken image */
        }
    }

    private void EnsurePage(bool filledBackground)
    {
        if (_page != null && _gfx != null) return;
        _page = _document.AddPage();
        _page.Width = XUnit.FromPoint(_layout.PageWidthPt);
        _page.Height = XUnit.FromPoint(_layout.PageHeightPt);
        _gfx = XGraphics.FromPdfPage(_page);
        _y = _layout.MarginTopPt;
        if (filledBackground)
            _gfx.DrawRectangle(new XSolidBrush(ParseHexColor(_pageBgHex)), 0, 0, _page.Width.Point, _page.Height.Point);
    }

    private void EnsureSpace(double needed)
    {
        if (_page == null) EnsurePage(filledBackground: true);
        if (_y + needed <= _layout.MarginTopPt + _layout.ContentHeightPt) return;

        _gfx.Dispose();
        _page = _document.AddPage();
        _page.Width = XUnit.FromPoint(_layout.PageWidthPt);
        _page.Height = XUnit.FromPoint(_layout.PageHeightPt);
        _gfx = XGraphics.FromPdfPage(_page);
        _gfx.DrawRectangle(new XSolidBrush(ParseHexColor(_pageBgHex)), 0, 0, _page.Width.Point, _page.Height.Point);
        _y = _layout.MarginTopPt;
    }

    private XRect ContentRect() =>
        new(_layout.MarginLeftPt, _y, _layout.ContentWidthPt, _bodyPt * _lineHeight * 2);

    private XFont MakeFont(string family, double sizePt, bool bold, bool italic) =>
        new(family, sizePt, (bold, italic) switch
        {
            (true, true) => XFontStyleEx.BoldItalic,
            (true, false) => XFontStyleEx.Bold,
            (false, true) => XFontStyleEx.Italic,
            _ => XFontStyleEx.Regular
        });

    private static List<string> WrapText(string text, XFont font, double maxWidth)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(maxWidth, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var w in words)
        {
            var trial = current.Length == 0 ? w : $"{current} {w}";
            if (measure.MeasureString(trial, font).Width > maxWidth && current.Length > 0)
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(w);
            }
            else
                current.Append(current.Length == 0 ? w : $" {w}");
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }

    private static XColor ParseHexColor(string hex)
    {
        if (!hex.StartsWith('#')) hex = "#" + hex;
        var m = ColorHexRegex.Match(hex);
        if (!m.Success) return XColors.White;
        var rgb = m.Groups[1].Value;
        return XColor.FromArgb(
            Convert.ToInt32(rgb[..2], 16),
            Convert.ToInt32(rgb[2..4], 16),
            Convert.ToInt32(rgb[4..6], 16));
    }

    private static XBrush ParseColorBrush(string hex) => new XSolidBrush(ParseHexColor(hex));

    public void DisposeGfx()
    {
        _gfx?.Dispose();
    }

    private sealed record RunStyle(string Family, double SizePt, bool Bold, bool Italic, XBrush? Brush)
    {
        public RunStyle MergeFromNode(HtmlNode node, ExportPdfTypography.TypefaceSet faces)
        {
            var fam = Family;
            var size = SizePt;
            var bold = Bold || node.Name is "strong" or "b" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
            var italic = Italic || node.Name is "em" or "i";
            XBrush? brush = Brush;

            var style = node.GetAttributeValue("style", "");
            if (!string.IsNullOrWhiteSpace(style))
            {
                var fm = FontFamilyRegex.Match(style);
                if (fm.Success)
                {
                    var first = fm.Groups[1].Value.Split(',')[0].Trim().Trim('\'', '"');
                    if (first.Length > 0) fam = first;
                }
                var sm = FontSizeRegex.Match(style);
                if (sm.Success && double.TryParse(sm.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var sp))
                {
                    var unit = sm.Groups[2].Value;
                    size = unit.Equals("px", StringComparison.OrdinalIgnoreCase) ? sp * 0.75 : sp;
                }
                var cm = ColorPropRegex.Match(style);
                if (cm.Success)
                {
                    var c = cm.Groups[1].Value.Trim();
                    if (c.StartsWith('#') && c.Length >= 7)
                        brush = ParseColorBrush(c[..7]);
                }
            }

            return this with { Family = fam, SizePt = size, Bold = bold, Italic = italic, Brush = brush };
        }
    }
}
