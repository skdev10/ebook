using System.Text.RegularExpressions;

/// <summary>Word-diff normalizer shared by corpus / sabotage proofs (no stopword blind spots).</summary>
public static class WordDiff
{
    public sealed record DiffReport(IReadOnlyList<string> Missing, IReadOnlyList<string> Extra);
    public sealed record Classified(string Kind, string Token, string SrcCtx, string OutCtx);

    public static string NormalizeForDiff(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = text
            .Replace("\u2019", "'").Replace("\u2018", "'")
            .Replace("\u201c", "\"").Replace("\u201d", "\"")
            .Replace("ﬁ", "fi").Replace("ﬂ", "fl").Replace("ﬀ", "ff")
            .Replace("ﬃ", "ffi").Replace("ﬄ", "ffl")
            .Replace("\u00AD", "").Replace("\u200B", "");
        s = Regex.Replace(s, @"\[figure\]", " ", RegexOptions.IgnoreCase);
        // Project Gutenberg boilerplate / license — not body text.
        s = Regex.Replace(s, @"\*\*\*\s*START OF[\s\S]*?\*\*\*", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\*\*\*\s*END OF[\s\S]*", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"Project Gutenberg[\s\S]{0,2500}?(?=CHAPTER|Chapter|BOOK|Book|PART|Part|I\.)", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"Copyright[\s\S]{0,2500}?Library of Congress[\s\S]{0,1200}?ISBN[\s\S]{0,500}", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bContents\b[\s\S]{0,2000}?(?=Preface\b)", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"(?i)\b(?:[A-Za-z]\s+){2,}[A-Za-z]\b", m => Regex.Replace(m.Value, @"\s+", ""));
        s = Regex.Replace(s, @"(\p{L})-\s+(\p{L})", "$1$2");
        s = Regex.Replace(s, @"(\p{L})'(\p{L})", "$1 $2");
        return s;
    }

    public static DiffReport DiffWords(string source, string output)
    {
        var src = Bag(Tokenize(source));
        var dst = Bag(Tokenize(output));
        dst = ReconstructFragments(dst, src.Keys);
        var missing = new List<string>();
        var extra = new List<string>();
        foreach (var (w, n) in src)
        {
            dst.TryGetValue(w, out var have);
            var deficit = n - have;
            if (deficit <= 0) continue;
            if (IsFalsePositiveToken(w, output)) continue;
            for (var i = 0; i < deficit; i++) missing.Add(w);
        }
        foreach (var (w, n) in dst)
        {
            src.TryGetValue(w, out var have);
            for (var i = 0; i < n - have; i++) extra.Add(w);
        }
        return new DiffReport(missing, extra);
    }

    private static bool IsFalsePositiveToken(string token, string outputRaw)
    {
        if (token.Length == 0) return true;
        // Letter-spaced display heads only: require whitespace BETWEEN letters
        // (do not use \s* — that also matches the contiguous word itself).
        if (token.Length >= 4)
        {
            var spaced = string.Join(@"\s+", token.Select(c => Regex.Escape(c.ToString())));
            if (Regex.IsMatch(outputRaw, spaced, RegexOptions.IgnoreCase))
                return true;
        }
        // Hyphenated compounds (stagnant-blooded, cannon-ball): each half is not a real loss.
        if (token.Length >= 3
            && (Regex.IsMatch(outputRaw, $@"\b{Regex.Escape(token)}-\p{{L}}", RegexOptions.IgnoreCase)
                || Regex.IsMatch(outputRaw, $@"\p{{L}}-{Regex.Escape(token)}\b", RegexOptions.IgnoreCase)))
            return true;
        // Drop-cap / mid-word space split: "H APPY"→happy, "Darc y"→darcy, "for gotten"→forgotten.
        if (token.Length >= 3
            && (Regex.IsMatch(outputRaw, $@"\b\p{{L}}{Regex.Escape(token)}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(outputRaw, $@"\b{Regex.Escape(token)}\p{{L}}{{1,5}}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(outputRaw, $@"\b\p{{L}}{{1,5}}{Regex.Escape(token)}\b", RegexOptions.IgnoreCase)))
            return true;
        return false;
    }

    public static List<Classified> ClassifyMissing(string sourceRaw, string outputRaw, IReadOnlyList<string> missing)
    {
        var outNorm = NormalizeForDiff(outputRaw);
        var list = new List<Classified>();
        foreach (var g in missing.GroupBy(x => x))
        {
            var token = g.Key;
            var srcCtx = Context(sourceRaw, token);
            var spaced = token.Length >= 4
                ? string.Join(@"\s+", token.Select(c => Regex.Escape(c.ToString())))
                : null;
            var letterSpaced = spaced != null && Regex.IsMatch(outputRaw, spaced, RegexOptions.IgnoreCase);
            var whole = Regex.IsMatch(outNorm, $@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase);
            var hyphenCompound = token.Length >= 3
                && (Regex.IsMatch(outputRaw, $@"\b{Regex.Escape(token)}-\p{{L}}", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(outputRaw, $@"\p{{L}}-{Regex.Escape(token)}\b", RegexOptions.IgnoreCase));
            // Pure roman numerals are chapter/folio markers — structural, not body-word loss.
            var romanMarker = Regex.IsMatch(token, @"^[ivxlcdm]+$", RegexOptions.IgnoreCase) && token.Length <= 8;
            // TOC/page-index numerals and mid-word chapter-split crumbs are not prose losses.
            var numericToc = Regex.IsMatch(token, @"^\d{1,4}$");
            var crumb = token.Length <= 3
                        || (token.Length <= 5 && !Regex.IsMatch(token, @"^[a-z]+$", RegexOptions.IgnoreCase));
            var illusMeta = Regex.IsMatch(token,
                @"^(frontispiece|dedication|heading|tailpiece|titlepage|illustrations?)$",
                RegexOptions.IgnoreCase);
            var dropCapOrSplit = token.Length >= 3
                && (Regex.IsMatch(outNorm, $@"\b\p{{L}}{Regex.Escape(token)}\b", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(outNorm, $@"\b{Regex.Escape(token)}\p{{L}}{{1,5}}\b", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(outNorm, $@"\b\p{{L}}{{1,5}}{Regex.Escape(token)}\b", RegexOptions.IgnoreCase));
            string kind;
            if (whole || letterSpaced || hyphenCompound || dropCapOrSplit) kind = "FALSE";
            else if (romanMarker || numericToc || illusMeta) kind = "ARTIFACT";
            else if (crumb && srcCtx.Contains("Chapter", StringComparison.OrdinalIgnoreCase)) kind = "ARTIFACT";
            else kind = "REAL";
            list.Add(new Classified(kind, $"{token}×{g.Count()}", srcCtx,
                whole ? "present after normalize"
                    : letterSpaced ? "letter-spaced in OUT"
                    : hyphenCompound ? "hyphenated compound half"
                    : dropCapOrSplit ? "drop-cap / mid-word split"
                    : romanMarker ? "roman chapter/folio marker"
                    : numericToc ? "TOC/page numeral"
                    : illusMeta ? "EPUB illustration/TOC label"
                    : crumb ? "fixture/split crumb"
                    : "(absent)"));
        }
        return list.OrderBy(c => c.Kind).ThenBy(c => c.Token).ToList();
    }

    private static string Context(string text, string token)
    {
        var m = Regex.Match(text ?? "", $@"(?is).{{0,35}}\b{Regex.Escape(token)}\b.{{0,35}}", RegexOptions.IgnoreCase);
        return m.Success ? Regex.Replace(m.Value, @"\s+", " ").Trim() : "(no ctx)";
    }

    private static List<string> Tokenize(string t) =>
        Regex.Matches(t.ToLowerInvariant(), @"[\p{L}\p{N}']+").Select(m => m.Value).ToList();

    private static Dictionary<string, int> Bag(List<string> toks)
    {
        var bag = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var w in toks)
        {
            if (bag.ContainsKey(w)) bag[w]++;
            else bag[w] = 1;
        }
        return bag;
    }

    private static Dictionary<string, int> ReconstructFragments(Dictionary<string, int> dst, IEnumerable<string> srcKeys)
    {
        var result = new Dictionary<string, int>(dst, StringComparer.Ordinal);
        foreach (var key in srcKeys)
        {
            if (key.Length < 5 || result.ContainsKey(key)) continue;
            // drop-cap: first letter + remainder
            var head = key[..1];
            var rest = key[1..];
            if (result.TryGetValue(head, out var hc) && hc > 0
                && result.TryGetValue(rest, out var rc) && rc > 0)
            {
                result[key] = result.GetValueOrDefault(key) + 1;
                result[head] = hc - 1;
                result[rest] = rc - 1;
            }
        }
        return result;
    }
}
