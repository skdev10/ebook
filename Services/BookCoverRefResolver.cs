namespace EBookDashboard.Services;

/// <summary>Resolves ebook front-panel cover references — never the full wrap when alternatives exist.</summary>
public static class BookCoverRefResolver
{
    /// <summary>
    /// Picks the best front-panel image ref, skipping any candidate identical to the full wrap.
    /// </summary>
    public static string ResolveEbookFrontCoverRef(
        string? printReadyFront,
        string? aiCover,
        string? bookCoverPath,
        string? wrap)
    {
        static string Norm(string? v) => (v ?? "").Trim();

        string Pick(string? candidate)
        {
            var c = Norm(candidate);
            if (string.IsNullOrEmpty(c)) return "";
            var w = Norm(wrap);
            if (!string.IsNullOrEmpty(w) && string.Equals(c, w, StringComparison.OrdinalIgnoreCase))
                return "";
            return c;
        }

        var fromPrintFront = Pick(printReadyFront);
        if (!string.IsNullOrEmpty(fromPrintFront)) return fromPrintFront;

        var fromAi = Pick(aiCover);
        if (!string.IsNullOrEmpty(fromAi)) return fromAi;

        var fromBook = Pick(bookCoverPath);
        if (!string.IsNullOrEmpty(fromBook)) return fromBook;

        return "";
    }

    public static string NormalizeCoverUrlRef(string? path)
    {
        var p = (path ?? "").Trim();
        if (string.IsNullOrEmpty(p)) return "";
        if (p.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            || p.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return p;
        return p.StartsWith("/") ? p : ("/" + p.TrimStart('/'));
    }
}
