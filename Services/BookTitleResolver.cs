using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

/// <summary>Resolves a human-readable book title when the DB row is still a placeholder.</summary>
public static class BookTitleResolver
{
    public static bool IsPlaceholderTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return true;
        var t = title.Trim();
        return t.Equals("Untitled", StringComparison.OrdinalIgnoreCase)
               || t.Equals("Untitled Book", StringComparison.OrdinalIgnoreCase)
               || t.Equals("No Book Selected", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the best display title without mutating the database.</summary>
    public static async Task<string> ResolveDisplayTitleAsync(
        ApplicationDbContext context,
        int userId,
        int bookId,
        string? storedTitle,
        CancellationToken ct = default)
    {
        var t = (storedTitle ?? "").Trim();
        if (!IsPlaceholderTitle(t)) return t;

        var writerKey = $"book:{bookId}:writerBookTitle";
        var writerTitle = await context.Settings.AsNoTracking()
            .Where(s => s.Key == writerKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(writerTitle) && !IsPlaceholderTitle(writerTitle))
            return writerTitle.Trim();

        var apiTitles = await context.APIRawResponse.AsNoTracking()
            .Where(r => r.UserId == userId && r.BookId == bookId && r.Title != null && r.Title != "")
            .OrderBy(r => r.Chapter)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => r.Title!)
            .ToListAsync(ct);

        foreach (var candidate in apiTitles)
        {
            var c = candidate.Trim();
            if (IsPlaceholderTitle(c)) continue;
            if (c.StartsWith("Chapter ", StringComparison.OrdinalIgnoreCase)) continue;
            return c;
        }

        return string.IsNullOrWhiteSpace(t) ? "Untitled Book" : t;
    }

    /// <summary>Persists a user-facing title to Settings and the Books row when still a placeholder.</summary>
    public static async Task SyncBookTitleAsync(
        ApplicationDbContext context,
        int userId,
        int bookId,
        string? title,
        CancellationToken ct = default)
    {
        if (bookId <= 0 || IsPlaceholderTitle(title)) return;
        var trimmed = title!.Trim();
        if (trimmed.Length > 250) trimmed = trimmed[..250];

        var book = await context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, ct);
        if (book != null)
        {
            book.Title = trimmed;
            book.UpdatedAt = DateTime.UtcNow;
        }

        var writerKey = $"book:{bookId}:writerBookTitle";
        var row = await context.Settings.FirstOrDefaultAsync(s => s.Key == writerKey, ct);
        if (row == null)
        {
            var id = await context.NextSettingIdAsync(ct);
            context.Settings.Add(new Settings
            {
                SettingId = id,
                Key = writerKey,
                Value = Settings.ClampValueLength(trimmed, Settings.MaxShortValueLength) ?? trimmed,
                Category = "Book",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            row.Value = Settings.ClampValueLength(trimmed, Settings.MaxShortValueLength) ?? trimmed;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(ct);
    }
}
