using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.Content;

namespace EBookDashboard.Services;

/// <summary>
/// Builds PDF lines by vertical overlap, not by a shared Y.
/// A word stays on a line when its box overlaps that line by at least half of the
/// smaller height. Superscripts and symbols that sit beside the line are attached
/// afterwards so they keep their horizontal place.
/// </summary>
internal static class PdfWordLineBuilder
{
    internal sealed class Line
    {
        public string Text { get; set; } = "";
        public double AvgFontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsCentered { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
        public double Baseline { get; set; }
    }

    private sealed class Glyph
    {
        public string Text = "";
        public double Left, Right, Top, Bottom, FontSize, Baseline;
        public double InkLeft, InkRight;
        public bool Bold;
        public bool Italic;
        public bool Small;
        public int Script;
    }

    public static List<Line> Build(IReadOnlyList<Word> words, double pageWidth)
    {
        var glyphs = Explode(words);
        if (glyphs.Count == 0)
            return new List<Line>();

        var heights = glyphs.Select(g => Math.Max(0.5, g.Top - g.Bottom)).OrderBy(h => h).ToList();
        var sizes = glyphs.Select(g => g.FontSize).Where(s => s > 0).OrderBy(s => s).ToList();
        var medianHeight = heights[heights.Count / 2];
        var medianSize = sizes.Count > 0 ? sizes[sizes.Count / 2] : 12;

        foreach (var g in glyphs)
        {
            var h = Math.Max(0.5, g.Top - g.Bottom);
            var letters = 0;
            var digits = 0;
            foreach (var ch in g.Text)
            {
                if (char.IsLetter(ch)) letters++;
                else if (char.IsDigit(ch)) digits++;
            }
            // Small-caps words ("CROWN") and old-style years ("1999") stay in reading order.
            // Only superscripts and symbols are attached to a nearby line afterwards.
            var realWord = letters >= 2 || digits >= 4;
            g.Small = !realWord && (
                (g.FontSize > 0 && g.FontSize <= medianSize * 0.82 && h <= medianHeight * 0.9)
                || h <= medianHeight * 0.55);
        }

        var body = glyphs.Where(g => !g.Small).ToList();
        var lines = ClusterByOverlap(body);
        AttachDropCaps(lines);
        AttachSmallGlyphs(lines, glyphs.Where(g => g.Small).ToList(), medianSize);
        FoldSameBaselineFragments(lines, medianSize);

        var built = new List<Line>(lines.Count);
        foreach (var line in lines)
        {
            if (line.Count == 0)
                continue;
            line.Sort((a, b) => a.Left.CompareTo(b.Left));
            var text = CollapseTrackedLetters(JoinGlyphs(line, medianSize));
            if (string.IsNullOrWhiteSpace(PdfImportScriptMarkup.StripMarkers(text)))
                continue;
            var sizesOnLine = line.Select(g => g.FontSize).Where(s => s > 0).ToList();
            var avg = sizesOnLine.Count > 0 ? sizesOnLine.Average() : 0;
            var left = line.Min(g => g.Left);
            var right = line.Max(g => g.Right);
            var mid = (left + right) / 2.0;
            var bold = line.Count(g => g.Bold) >= line.Count * 0.55;
            var centered = pageWidth > 0 && Math.Abs(mid - pageWidth / 2.0) < pageWidth * 0.18
                           && (right - left) < pageWidth * 0.72;
            var baselines = line.Select(g => g.Baseline).OrderBy(b => b).ToList();
            built.Add(new Line
            {
                Text = text,
                AvgFontSize = avg,
                IsBold = bold,
                IsCentered = centered,
                Top = line.Max(g => g.Top),
                Bottom = line.Min(g => g.Bottom),
                Left = left,
                Right = right,
                Baseline = baselines[baselines.Count / 2]
            });
        }

        built.Sort((a, b) => b.Baseline.CompareTo(a.Baseline));
        return built;
    }

    /// <summary>Pull a huge single letter onto the following word ("E" + "VEN" → "EVEN").</summary>
    public static List<Line> MergeDropCapLetters(List<Line> lines)
    {
        if (lines.Count < 2)
            return lines;
        var result = new List<Line>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var cur = lines[i];
            var plain = PdfImportScriptMarkup.StripMarkers(cur.Text).Trim();
            if (i + 1 < lines.Count
                && plain.Length == 1
                && char.IsLetter(plain[0])
                && cur.AvgFontSize >= lines[i + 1].AvgFontSize * 1.35)
            {
                var next = lines[i + 1];
                var nextPlain = PdfImportScriptMarkup.StripMarkers(next.Text).TrimStart();
                if (!nextPlain.StartsWith(plain, StringComparison.OrdinalIgnoreCase))
                    next.Text = plain + next.Text.TrimStart();
                next.Top = Math.Max(next.Top, cur.Top);
                result.Add(next);
                i++;
                continue;
            }

            result.Add(cur);
        }

        return result;
    }

    /// <summary>A heading that wraps ("WILL THEY" / "COME?") becomes one line.</summary>
    public static List<Line> MergeWrappedHeadings(List<Line> lines, double medianFont)
    {
        if (lines.Count < 2)
            return lines;
        var result = new List<Line>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var cur = lines[i];
            while (i + 1 < lines.Count && IsHeadingContinuation(cur, lines[i + 1], medianFont))
            {
                var next = lines[i + 1];
                var left = PdfImportScriptMarkup.StripMarkers(cur.Text).TrimEnd();
                var right = PdfImportScriptMarkup.StripMarkers(next.Text).Trim();
                cur.Text = left + " " + right;
                cur.Bottom = Math.Min(cur.Bottom, next.Bottom);
                cur.Right = Math.Max(cur.Right, next.Right);
                cur.Left = Math.Min(cur.Left, next.Left);
                cur.AvgFontSize = Math.Max(cur.AvgFontSize, next.AvgFontSize);
                cur.IsBold = cur.IsBold || next.IsBold;
                cur.IsCentered = cur.IsCentered && next.IsCentered;
                i++;
            }

            result.Add(cur);
        }

        return result;
    }

    /// <summary>Drop running headers, footers, and page numbers that repeat across pages.</summary>
    public static void StripRunningFurniture(IList<List<Line>> pages, IList<double> pageHeights)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var p = 0; p < pages.Count; p++)
        {
            var height = p < pageHeights.Count ? pageHeights[p] : 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in pages[p])
            {
                if (!InMarginBand(line, height))
                    continue;
                var key = FurnitureKey(line.Text);
                if (key.Length == 0 || !seen.Add(key))
                    continue;
                counts.TryGetValue(key, out var n);
                counts[key] = n + 1;
            }
        }

        var repeated = new HashSet<string>(
            counts.Where(kv => kv.Value >= 4).Select(kv => kv.Key),
            StringComparer.Ordinal);

        for (var p = 0; p < pages.Count; p++)
        {
            var height = p < pageHeights.Count ? pageHeights[p] : 0;
            pages[p] = pages[p].Where(line =>
            {
                if (!InMarginBand(line, height))
                    return true;
                var plain = PdfImportScriptMarkup.StripMarkers(line.Text).Trim();
                if (Regex.IsMatch(plain, @"^(?:\d{1,4}|[ivxlcdm]{1,6})$", RegexOptions.IgnoreCase))
                    return false;
                var key = FurnitureKey(line.Text);
                return key.Length == 0 || !repeated.Contains(key);
            }).ToList();
        }
    }

    private static bool IsHeadingContinuation(Line cur, Line next, double medianFont)
    {
        var a = PdfImportScriptMarkup.StripMarkers(cur.Text).Trim();
        var b = PdfImportScriptMarkup.StripMarkers(next.Text).Trim();
        if (a.Length < 2 || b.Length < 2 || a.Length > 90 || b.Length > 40)
            return false;
        if (b.Count(ch => ch == ' ') > 5)
            return false;
        var sizeA = cur.AvgFontSize;
        var sizeB = next.AvgFontSize;
        if (sizeA <= 0 || sizeB <= 0)
            return false;
        var ratio = Math.Max(sizeA, sizeB) / Math.Min(sizeA, sizeB);
        if (ratio > 1.15)
            return false;
        var big = medianFont > 0 && sizeA >= medianFont * 1.2 && sizeB >= medianFont * 1.2;
        var centered = cur.IsCentered && next.IsCentered && medianFont > 0 && sizeA >= medianFont * 1.05;
        return big || centered;
    }

    private static bool InMarginBand(Line line, double pageHeight)
    {
        if (pageHeight <= 0)
            return false;
        return line.Bottom >= pageHeight * 0.92 || line.Top <= pageHeight * 0.065;
    }

    private static string FurnitureKey(string? text)
    {
        var s = PdfImportScriptMarkup.StripMarkers(text).ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"\d+", "#");
        return s.Length > 48 ? "" : s;
    }

    private static List<Glyph> Explode(IReadOnlyList<Word> words)
    {
        var glyphs = new List<Glyph>(words.Count);
        foreach (var word in words)
        {
            var letters = word.Letters?.Where(l => !string.IsNullOrEmpty(l.Value)).ToList();
            if (letters is not { Count: > 0 })
            {
                var text = word.Text ?? "";
                if (text.Length == 0)
                    continue;
                glyphs.Add(new Glyph
                {
                    Text = text,
                    Left = word.BoundingBox.Left,
                    Right = word.BoundingBox.Right,
                    InkLeft = word.BoundingBox.Left,
                    InkRight = word.BoundingBox.Right,
                    Top = word.BoundingBox.Top,
                    Bottom = word.BoundingBox.Bottom,
                    FontSize = 12,
                    Baseline = word.BoundingBox.Bottom
                });
                continue;
            }

            var baselines = letters.Select(l => l.StartBaseLine.Y).OrderBy(y => y).ToList();
            var sizes = letters.Select(l => l.FontSize).Where(s => s > 0).OrderBy(s => s).ToList();
            // Word boxes often swallow the following space. Spacing uses the letter ink.
            var inkLeft = letters.Min(l => l.BoundingBox.Left);
            var inkRight = letters.Max(l => l.BoundingBox.Right);
            glyphs.Add(new Glyph
            {
                Text = JoinLetters(letters),
                Left = word.BoundingBox.Left,
                Right = word.BoundingBox.Right,
                InkLeft = inkLeft,
                InkRight = inkRight,
                Top = word.BoundingBox.Top,
                Bottom = word.BoundingBox.Bottom,
                FontSize = sizes.Count > 0 ? sizes[sizes.Count / 2] : 12,
                Baseline = baselines[baselines.Count / 2],
                Bold = letters.Count(l => IsBoldFont(l.FontName ?? "")) >= letters.Count * 0.55,
                Italic = letters.Count(l => IsItalicFont(l.FontName ?? "")) >= letters.Count * 0.55
            });
        }

        return glyphs;
    }

    /// <summary>Letterspaced headings ("C H A P T E R") are one word. Ordinary words are left alone.</summary>
    private static string CollapseTrackedLetters(string text)
    {
        return Regex.Replace(text, @"\b(?:[A-Za-z]\s+){3,}[A-Za-z]\b", match =>
        {
            var letters = Regex.Replace(match.Value, @"\s+", "");
            var upper = letters.Count(char.IsUpper);
            var lower = letters.Count(char.IsLower);
            if (upper == letters.Length || lower == letters.Length)
                return letters;
            return match.Value;
        });
    }

    /// <summary>Spaces inside a PdfPig word only when the letter gap itself is a word space.</summary>
    private static string JoinLetters(List<Letter> letters)
    {
        var ordered = letters.OrderBy(l => l.BoundingBox.Left).ToList();
        var sb = new StringBuilder();
        Letter? prev = null;
        foreach (var letter in ordered)
        {
            if (prev != null)
            {
                var gap = letter.BoundingBox.Left - prev.BoundingBox.Right;
                var size = Math.Max(prev.FontSize, letter.FontSize);
                if (size > 0 && gap > size * 0.15 && !StartsWithPunctuation(letter.Value ?? ""))
                    sb.Append(' ');
            }

            sb.Append(letter.Value);
            prev = letter;
        }

        return sb.ToString();
    }

    private static List<List<Glyph>> ClusterByOverlap(List<Glyph> body)
    {
        var ordered = body
            .OrderByDescending(g => (g.Top + g.Bottom) / 2.0)
            .ThenBy(g => g.Left)
            .ToList();
        var lines = new List<List<Glyph>>();
        foreach (var glyph in ordered)
        {
            List<Glyph>? best = null;
            var bestRatio = 0.0;
            foreach (var line in lines)
            {
                if (!OverlapsLine(glyph, line, out var ratio) || ratio < 0.5)
                    continue;
                // A drop cap is much taller than the line it sits beside. Joining it
                // would zip that line together with the two lines below it.
                var glyphH = Math.Max(0.5, glyph.Top - glyph.Bottom);
                var lineH = Math.Max(0.5, line.Max(g => g.Top) - line.Min(g => g.Bottom));
                if (Math.Max(glyphH, lineH) > Math.Min(glyphH, lineH) * 1.65)
                    continue;
                if (ratio > bestRatio)
                {
                    bestRatio = ratio;
                    best = line;
                }
            }

            if (best == null)
                lines.Add(new List<Glyph> { glyph });
            else
                best.Add(glyph);
        }

        return lines;
    }

    /// <summary>Put a one-letter drop cap on the first line it overlaps, not on every line.</summary>
    private static void AttachDropCaps(List<List<Glyph>> lines)
    {
        var caps = lines.Where(line =>
        {
            if (line.Count != 1)
                return false;
            var plain = line[0].Text.Trim();
            return plain.Length == 1 && char.IsLetter(plain[0]);
        }).ToList();

        foreach (var capLine in caps)
        {
            var cap = capLine[0];
            var capH = Math.Max(0.5, cap.Top - cap.Bottom);
            List<Glyph>? best = null;
            var bestTop = double.MinValue;
            foreach (var line in lines)
            {
                if (line == capLine || line.Count == 0)
                    continue;
                var lineTop = line.Max(g => g.Top);
                var lineBottom = line.Min(g => g.Bottom);
                var lineH = Math.Max(0.5, lineTop - lineBottom);
                if (capH < lineH * 1.5)
                    continue;
                var overlap = Math.Min(cap.Top, lineTop) - Math.Max(cap.Bottom, lineBottom);
                if (overlap <= 0 || lineTop <= bestTop)
                    continue;
                bestTop = lineTop;
                best = line;
            }

            if (best == null)
                continue;
            best.Add(cap);
            lines.Remove(capLine);
        }
    }

    /// <summary>
    /// A small-caps word or an old-style year can miss the overlap test and become its own line.
    /// Fold it back onto the line that shares its baseline, then X order puts "1999" after the title
    /// and "CROWN BUSINESS" at the start of its sentence.
    /// </summary>
    private static void FoldSameBaselineFragments(List<List<Glyph>> lines, double medianSize)
    {
        var guard = 0;
        while (guard++ < lines.Count + 2)
        {
            var folded = false;
            for (var i = 0; i < lines.Count; i++)
            {
                if (!IsFoldFragment(lines[i], medianSize))
                    continue;
                var fragBase = MedianBaseline(lines[i]);
                var best = -1;
                var bestDist = double.MaxValue;
                var limit = Math.Max(medianSize, 8) * 0.5;
                for (var j = 0; j < lines.Count; j++)
                {
                    if (j == i || lines[j].Count == 0 || IsFoldFragment(lines[j], medianSize))
                        continue;
                    var dist = Math.Abs(fragBase - MedianBaseline(lines[j]));
                    if (dist > limit || dist >= bestDist)
                        continue;
                    best = j;
                    bestDist = dist;
                }

                if (best < 0)
                    continue;
                lines[best].AddRange(lines[i]);
                lines.RemoveAt(i);
                folded = true;
                break;
            }

            if (!folded)
                break;
        }
    }

    private static bool IsFoldFragment(List<Glyph> line, double medianSize)
    {
        if (line.Count == 0 || line.Count > 4)
            return false;
        var text = string.Concat(line.Select(g => g.Text)).Trim();
        if (text.Length == 0 || text.Length > 24)
            return false;
        if (Regex.IsMatch(text, @"^\d{4}$"))
            return true;
        var size = line.Average(g => g.FontSize > 0 ? g.FontSize : medianSize);
        return size <= medianSize * 0.82 && text.Any(char.IsLetter);
    }

    private static double MedianBaseline(List<Glyph> line)
    {
        var values = line.Select(g => g.Baseline).OrderBy(b => b).ToList();
        return values[values.Count / 2];
    }

    private static void AttachSmallGlyphs(List<List<Glyph>> lines, List<Glyph> small, double medianSize)
    {
        foreach (var glyph in small)
        {
            List<Glyph>? best = null;
            var bestDist = double.MaxValue;
            var center = (glyph.Top + glyph.Bottom) / 2.0;
            foreach (var line in lines)
            {
                if (line.Count == 0)
                    continue;
                var left = line.Min(g => g.Left) - medianSize;
                var right = line.Max(g => g.Right) + medianSize;
                if (glyph.Right < left || glyph.Left > right)
                    continue;
                var lineCenter = line.Average(g => (g.Top + g.Bottom) / 2.0);
                var lineSize = line.Average(g => g.FontSize);
                var dist = Math.Abs(center - lineCenter);
                if (dist > Math.Max(lineSize * 0.85, 8) || dist >= bestDist)
                    continue;
                bestDist = dist;
                best = line;
            }

            if (best != null)
                best.Add(glyph);
            else
                lines.Add(new List<Glyph> { glyph });
        }
    }

    private static bool OverlapsLine(Glyph glyph, List<Glyph> line, out double ratio)
    {
        ratio = 0;
        var lineTop = line.Max(g => g.Top);
        var lineBottom = line.Min(g => g.Bottom);
        var overlap = Math.Min(glyph.Top, lineTop) - Math.Max(glyph.Bottom, lineBottom);
        if (overlap <= 0)
            return false;
        var smaller = Math.Min(Math.Max(0.5, glyph.Top - glyph.Bottom), Math.Max(0.5, lineTop - lineBottom));
        ratio = overlap / smaller;
        return true;
    }

    private static string JoinGlyphs(List<Glyph> glyphs, double medianSize)
    {
        var sb = new StringBuilder();
        var run = new StringBuilder();
        var runKind = 0; // 0 base, 1 sup, -1 sub
        var runEm = false;
        var runBold = false;
        Glyph? prev = null;

        void Flush()
        {
            if (run.Length == 0)
                return;
            var piece = run.ToString();
            if (runKind != 0)
                piece = PdfImportScriptMarkup.WrapRun(piece, runKind);
            if (runEm)
                piece = string.Concat(PdfImportScriptMarkup.EmStart, piece, PdfImportScriptMarkup.EmEnd);
            else if (runBold)
                piece = string.Concat(PdfImportScriptMarkup.StrongStart, piece, PdfImportScriptMarkup.StrongEnd);
            sb.Append(piece);
            run.Clear();
        }

        foreach (var glyph in glyphs)
        {
            if (glyph.Text.Length == 0)
                continue;
            var kind = 0;
            var gap = prev == null ? 0 : glyph.InkLeft - prev.InkRight;
            if (prev != null && runKind != 0
                && gap <= prev.FontSize * 0.25
                && Math.Abs(glyph.FontSize - prev.FontSize) <= 0.6
                && Math.Abs(glyph.Baseline - prev.Baseline) <= 1.0)
                kind = runKind;
            else if (prev != null && IsAttachedScript(prev, glyph, out var script))
                kind = script;

            var prevPlain = prev?.Text.Trim() ?? "";
            var nextPlain = glyph.Text.Trim();
            var trackedLetters = prevPlain.Length == 1 && nextPlain.Length == 1
                                 && char.IsLetter(prevPlain[0]) && char.IsLetter(nextPlain[0]);
            var dropCapJoin = prev != null
                              && prevPlain.Length == 1
                              && char.IsLetter(prevPlain[0])
                              && prev.FontSize >= glyph.FontSize * 1.35
                              && nextPlain.Length > 0
                              && char.IsLetter(nextPlain[0]);
            var smallCapWord = prevPlain.Length == 1
                               && nextPlain.Length > 1
                               && char.IsLetter(prevPlain[0])
                               && char.IsLetter(nextPlain[0])
                               && !dropCapJoin;
            var minGap = trackedLetters ? Math.Max(prev?.FontSize ?? 1, 1) * 0.6
                : smallCapWord ? 0.35
                : 1.05;
            var needsSpace = prev != null
                             && kind == 0
                             && runKind == 0
                             && !dropCapJoin
                             && gap > minGap
                             && !StartsWithPunctuation(glyph.Text);

            if (kind != runKind || glyph.Italic != runEm || (kind == 0 && glyph.Bold != runBold) || needsSpace)
            {
                Flush();
                if (needsSpace)
                    sb.Append(' ');
                runKind = kind;
                runEm = glyph.Italic;
                runBold = kind == 0 && glyph.Bold && !glyph.Italic;
            }

            run.Append(glyph.Text);
            prev = glyph;
        }

        Flush();
        return sb.ToString();
    }

    private static bool IsAttachedScript(Glyph prev, Glyph next, out int script)
    {
        script = 0;
        // A drop cap is a tall letter beside the line, not the base of a superscript.
        var prevPlain = prev.Text.Trim();
        if (prevPlain.Length == 1 && char.IsLetter(prevPlain[0]) && prev.FontSize >= next.FontSize * 1.8)
            return false;
        var gap = next.InkLeft - prev.InkRight;
        if (gap > prev.FontSize * 0.22)
            return false;
        if (next.FontSize > prev.FontSize * 0.85)
            return false;
        if (!IsShortScript(next.Text) || prev.Text.EndsWith('/') || next.Text.StartsWith('/'))
            return false;
        var rise = next.Baseline - prev.Baseline;
        if (rise >= prev.FontSize * 0.12)
        {
            script = 1;
            return true;
        }

        if (rise <= -prev.FontSize * 0.18 && next.Text.All(char.IsLetterOrDigit))
        {
            script = -1;
            return true;
        }

        return false;
    }

    /// <summary>Exponents such as 10^30 and a^n. A short word ("UR", "you") is not a superscript.</summary>
    private static bool IsShortScript(string text) =>
        text.Length is >= 1 and <= 4 && text.All(char.IsDigit)
        || (text.Length == 1 && char.IsLetter(text[0]));

    private static bool StartsWithPunctuation(string text) =>
        text.Length > 0 && text[0] is ',' or '.' or ';' or ':' or '!' or '?' or ')' or ']' or '%' or '\'' or '’' or '"' or '”';

    private static bool IsBoldFont(string name) =>
        name.Contains("Bold", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Black", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Heavy", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Semibold", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Demi", StringComparison.OrdinalIgnoreCase);

    private static bool IsItalicFont(string name) =>
        name.Contains("Italic", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
}
