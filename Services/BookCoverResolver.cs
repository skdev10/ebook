namespace EBookDashboard.Services;



/// <summary>Central cover URL resolution — generated cover or the cover-design-progress placeholder.</summary>

public static class BookCoverResolver

{

    /// <summary>Placeholder shown until the user generates a cover (Dashboard Continue Editing only).</summary>

    public const string PlaceholderWebPath = "/images/books/cover-in-progress.svg";



    /// <summary>Returns a displayable cover URL, or the progress placeholder when none exists.</summary>

    public static string ResolveDisplayUrl(string? coverPath, string? aiCoverPath = null)

    {

        var resolved = FirstNonEmpty(Normalize(coverPath), Normalize(aiCoverPath));

        return string.IsNullOrEmpty(resolved) ? PlaceholderWebPath : resolved;

    }



    /// <summary>Dashboard Continue Editing — progress image only when no AI/uploaded cover exists.</summary>

    public static string ResolveContinueEditingCoverUrl(string? coverPath, string? aiCoverPath = null)

    {

        if (HasGeneratedCover(coverPath, aiCoverPath))

            return FirstNonEmpty(Normalize(aiCoverPath), Normalize(coverPath))!;

        return PlaceholderWebPath;

    }



    /// <summary>My Books — real cover only; empty when none (never the progress placeholder).</summary>

    public static string ResolveMyBooksCoverUrl(string? coverPath, string? aiCoverPath = null)

    {

        if (!string.IsNullOrEmpty(Normalize(aiCoverPath)))

            return Normalize(aiCoverPath)!;

        var cover = Normalize(coverPath);

        if (string.IsNullOrEmpty(cover) || IsStockThemeCover(cover)

            || cover.Equals(PlaceholderWebPath, StringComparison.OrdinalIgnoreCase))

            return string.Empty;

        return cover;

    }



    /// <summary>True when the book has a real generated/uploaded cover (not stock theme art).</summary>

    public static bool HasGeneratedCover(string? coverPath, string? aiCoverPath = null)

    {

        if (!string.IsNullOrEmpty(Normalize(aiCoverPath))) return true;

        var cover = Normalize(coverPath);

        if (string.IsNullOrEmpty(cover)) return false;

        if (cover.Equals(PlaceholderWebPath, StringComparison.OrdinalIgnoreCase)) return false;

        if (IsStockThemeCover(cover)) return false;

        return true;

    }



    /// <summary>Seeded demo / theme artwork under /images/books/ — not a user-generated cover.</summary>

    public static bool IsStockThemeCover(string? path)

    {

        var p = Normalize(path);

        if (string.IsNullOrEmpty(p)) return false;

        if (!p.StartsWith("/images/books/", StringComparison.OrdinalIgnoreCase)) return false;

        if (p.Contains("cover-in-progress", StringComparison.OrdinalIgnoreCase)) return false;

        if (p.Contains("writing-new-book", StringComparison.OrdinalIgnoreCase)) return false;

        return true;

    }



    private static string? FirstNonEmpty(params string?[] values)

    {

        foreach (var v in values)

        {

            if (!string.IsNullOrWhiteSpace(v)) return v;

        }

        return null;

    }



    private static string? Normalize(string? path)

    {

        if (string.IsNullOrWhiteSpace(path)) return null;

        var p = path.Trim();

        if (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase)

            || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase)

            || p.StartsWith("data:", StringComparison.OrdinalIgnoreCase))

            return p;

        return p.StartsWith('/') ? p : "/" + p.TrimStart('/');

    }

}


