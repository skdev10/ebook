using EBookDashboard.Configuration;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services;

/// <summary>Per-book cover projects for the Cover Design workspace.</summary>
public sealed class BookCoverDesignService : IBookCoverDesignService
{
    private readonly ApplicationDbContext _db;
    private readonly KdpSpecs _specs;
    private readonly KdpCalculationService _calc;

    public BookCoverDesignService(
        ApplicationDbContext db,
        IOptions<KdpSpecs> specs,
        KdpCalculationService calc)
    {
        _db = db;
        _specs = specs.Value;
        _calc = calc;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BookCoverDesign>> ListAsync(int userId, int bookId, CancellationToken ct = default)
    {
        if (!await OwnsBookAsync(userId, bookId, ct))
            return Array.Empty<BookCoverDesign>();

        return await _db.BookCoverDesigns.AsNoTracking()
            .Where(c => c.BookId == bookId && c.UserId == userId && !c.IsDeleted)
            .OrderBy(c => c.CoverId)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<BookCoverDesign> EnsureDefaultAsync(
        int userId, int bookId, string? preferredType = null, CancellationToken ct = default)
    {
        var existing = await _db.BookCoverDesigns
            .Where(c => c.BookId == bookId && c.UserId == userId && !c.IsDeleted)
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.CoverId)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            var active = existing.FirstOrDefault(c => c.IsActive) ?? existing[0];
            if (!active.IsActive)
            {
                foreach (var row in existing) row.IsActive = row.CoverId == active.CoverId;
                active.IsActive = true;
                active.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            return active;
        }

        var type = NormalizeType(preferredType) ?? await ResolvePreferredTypeAsync(userId, bookId, ct);
        return await CreateAsync(userId, bookId, type, ct);
    }

    /// <inheritdoc />
    public async Task<BookCoverDesign> CreateAsync(int userId, int bookId, string coverType, CancellationToken ct = default)
    {
        if (!await OwnsBookAsync(userId, bookId, ct))
            throw new InvalidOperationException("Book not found.");

        var type = NormalizeType(coverType) ?? "Ebook";
        var count = await _db.BookCoverDesigns.CountAsync(
            c => c.BookId == bookId && c.UserId == userId && !c.IsDeleted, ct);

        await DeactivateAllAsync(userId, bookId, ct);

        var dims = await ComputeDimsAsync(userId, bookId, type, ct);
        var row = new BookCoverDesign
        {
            BookId = bookId,
            UserId = userId,
            CoverType = type,
            DisplayName = $"Cover {count + 1} ({type})",
            TrimWidthIn = dims.TrimW,
            TrimHeightIn = dims.TrimH,
            SpineWidthIn = dims.Spine,
            BleedIn = dims.Bleed,
            FullWidthIn = dims.FullW,
            FullHeightIn = dims.FullH,
            Status = "Draft",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Seed front from current book / Settings when creating the first cover.
        if (count == 0)
        {
            var front = await ReadSettingAsync($"book:{bookId}:printReadyCoverFront", ct)
                        ?? await ReadSettingAsync($"book:{bookId}:aiCoverLastPreview", ct);
            var book = await _db.Books.AsNoTracking().FirstOrDefaultAsync(b => b.BookId == bookId, ct);
            row.FrontImagePath = FirstNonEmpty(front, book?.CoverImagePath);
            row.WrapImagePath = await ReadSettingAsync($"book:{bookId}:printReadyCoverWrap", ct);
            row.BackImagePath = await ReadSettingAsync($"book:{bookId}:printReadyCoverBack", ct);
            row.SpineImagePath = await ReadSettingAsync($"book:{bookId}:printReadyCoverSpine", ct);
            if (!string.IsNullOrWhiteSpace(row.FrontImagePath))
                row.Status = string.IsNullOrWhiteSpace(row.WrapImagePath) && NeedsWrap(type) ? "Draft" : "Ready";
        }

        _db.BookCoverDesigns.Add(row);
        await _db.SaveChangesAsync(ct);
        await SyncFormatAndSettingsAsync(row, ct);
        return row;
    }

    /// <inheritdoc />
    public async Task<BookCoverDesign?> DuplicateAsync(int userId, int coverId, CancellationToken ct = default)
    {
        var src = await _db.BookCoverDesigns
            .FirstOrDefaultAsync(c => c.CoverId == coverId && c.UserId == userId && !c.IsDeleted, ct);
        if (src == null) return null;

        var count = await _db.BookCoverDesigns.CountAsync(
            c => c.BookId == src.BookId && c.UserId == userId && !c.IsDeleted, ct);

        await DeactivateAllAsync(userId, src.BookId, ct);

        var copy = new BookCoverDesign
        {
            BookId = src.BookId,
            UserId = userId,
            CoverType = src.CoverType,
            DisplayName = $"Cover {count + 1} ({src.CoverType})",
            TrimWidthIn = src.TrimWidthIn,
            TrimHeightIn = src.TrimHeightIn,
            SpineWidthIn = src.SpineWidthIn,
            BleedIn = src.BleedIn,
            FullWidthIn = src.FullWidthIn,
            FullHeightIn = src.FullHeightIn,
            Status = src.Status,
            FrontImagePath = src.FrontImagePath,
            BackImagePath = src.BackImagePath,
            SpineImagePath = src.SpineImagePath,
            WrapImagePath = src.WrapImagePath,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.BookCoverDesigns.Add(copy);
        await _db.SaveChangesAsync(ct);
        await SyncFormatAndSettingsAsync(copy, ct);
        return copy;
    }

    /// <inheritdoc />
    public async Task<(bool Ok, string? Message)> SoftDeleteAsync(int userId, int coverId, CancellationToken ct = default)
    {
        var row = await _db.BookCoverDesigns
            .FirstOrDefaultAsync(c => c.CoverId == coverId && c.UserId == userId && !c.IsDeleted, ct);
        if (row == null) return (false, "Cover not found.");

        var remaining = await _db.BookCoverDesigns.CountAsync(
            c => c.BookId == row.BookId && c.UserId == userId && !c.IsDeleted && c.CoverId != coverId, ct);
        if (remaining == 0)
            return (false, "Keep at least one cover project for this book.");

        row.IsDeleted = true;
        row.IsActive = false;
        row.DeletedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var next = await _db.BookCoverDesigns
            .Where(c => c.BookId == row.BookId && c.UserId == userId && !c.IsDeleted)
            .OrderBy(c => c.CoverId)
            .FirstAsync(ct);
        next.IsActive = true;
        next.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await SyncFormatAndSettingsAsync(next, ct);
        return (true, null);
    }

    /// <inheritdoc />
    public async Task<BookCoverDesign?> ActivateAsync(int userId, int coverId, CancellationToken ct = default)
    {
        var row = await _db.BookCoverDesigns
            .FirstOrDefaultAsync(c => c.CoverId == coverId && c.UserId == userId && !c.IsDeleted, ct);
        if (row == null) return null;

        await DeactivateAllAsync(userId, row.BookId, ct);
        row.IsActive = true;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await SyncFormatAndSettingsAsync(row, ct);
        return row;
    }

    /// <inheritdoc />
    public async Task SyncActiveAssetsAsync(int userId, int bookId, CancellationToken ct = default)
    {
        var active = await _db.BookCoverDesigns
            .FirstOrDefaultAsync(c => c.BookId == bookId && c.UserId == userId && c.IsActive && !c.IsDeleted, ct);
        if (active == null) return;

        active.FrontImagePath = await ReadSettingAsync($"book:{bookId}:printReadyCoverFront", ct)
            ?? await ReadSettingAsync($"book:{bookId}:aiCoverLastPreview", ct)
            ?? active.FrontImagePath;
        active.WrapImagePath = await ReadSettingAsync($"book:{bookId}:printReadyCoverWrap", ct) ?? active.WrapImagePath;
        active.BackImagePath = await ReadSettingAsync($"book:{bookId}:printReadyCoverBack", ct) ?? active.BackImagePath;
        active.SpineImagePath = await ReadSettingAsync($"book:{bookId}:printReadyCoverSpine", ct) ?? active.SpineImagePath;

        var dims = await ComputeDimsAsync(userId, bookId, active.CoverType, ct);
        active.TrimWidthIn = dims.TrimW;
        active.TrimHeightIn = dims.TrimH;
        active.SpineWidthIn = dims.Spine;
        active.BleedIn = dims.Bleed;
        active.FullWidthIn = dims.FullW;
        active.FullHeightIn = dims.FullH;

        if (!string.IsNullOrWhiteSpace(active.FrontImagePath)
            && (!NeedsWrap(active.CoverType) || !string.IsNullOrWhiteSpace(active.WrapImagePath)))
            active.Status = "Ready";
        else if (!string.IsNullOrWhiteSpace(active.FrontImagePath))
            active.Status = "Draft";

        active.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private async Task SyncFormatAndSettingsAsync(BookCoverDesign row, CancellationToken ct)
    {
        // Persist format so wrap pipeline and Publish page agree with the active cover type.
        var fmt = row.CoverType.Equals("Ebook", StringComparison.OrdinalIgnoreCase) ? "Ebook"
            : row.CoverType.Equals("Hardcover", StringComparison.OrdinalIgnoreCase) ? "Hardcover"
            : "Paperback";

        var formatting = await _db.BookFormatting
            .FirstOrDefaultAsync(f => f.BookId == row.BookId && f.UserId == row.UserId, ct);
        if (formatting == null)
        {
            formatting = new BookFormatting
            {
                BookId = row.BookId,
                UserId = row.UserId,
                Format = fmt,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _db.BookFormatting.Add(formatting);
        }
        else
        {
            formatting.Format = fmt;
            formatting.UpdatedAt = DateTime.UtcNow;
        }

        await UpsertSettingAsync($"book:{row.BookId}:activeCoverId", row.CoverId.ToString(), ct);

        if (!string.IsNullOrWhiteSpace(row.FrontImagePath))
        {
            await UpsertSettingAsync($"book:{row.BookId}:printReadyCoverFront", row.FrontImagePath!, ct);
            await UpsertSettingAsync($"book:{row.BookId}:aiCoverLastPreview", row.FrontImagePath!, ct);
        }
        if (!string.IsNullOrWhiteSpace(row.WrapImagePath))
            await UpsertSettingAsync($"book:{row.BookId}:printReadyCoverWrap", row.WrapImagePath!, ct);
        if (!string.IsNullOrWhiteSpace(row.BackImagePath))
            await UpsertSettingAsync($"book:{row.BookId}:printReadyCoverBack", row.BackImagePath!, ct);
        if (!string.IsNullOrWhiteSpace(row.SpineImagePath))
            await UpsertSettingAsync($"book:{row.BookId}:printReadyCoverSpine", row.SpineImagePath!, ct);

        await _db.SaveChangesAsync(ct);
    }

    private async Task DeactivateAllAsync(int userId, int bookId, CancellationToken ct)
    {
        var rows = await _db.BookCoverDesigns
            .Where(c => c.BookId == bookId && c.UserId == userId && !c.IsDeleted && c.IsActive)
            .ToListAsync(ct);
        foreach (var r in rows)
        {
            r.IsActive = false;
            r.UpdatedAt = DateTime.UtcNow;
        }
        if (rows.Count > 0)
            await _db.SaveChangesAsync(ct);
    }

    private async Task<(double TrimW, double TrimH, double Spine, double Bleed, double FullW, double FullH)> ComputeDimsAsync(
        int userId, int bookId, string coverType, CancellationToken ct)
    {
        var calcRow = await _db.coverDesignCalculator.AsNoTracking()
            .Where(c => c.BookId == bookId && c.UserId == userId && c.isActive)
            .OrderByDescending(c => c.CoverId)
            .FirstOrDefaultAsync(ct);

        double trimW = 6, trimH = 9;
        var trimLabel = (calcRow?.TrimSize ?? "6 x 9 in").ToLowerInvariant();
        if (trimLabel.Contains("5.5") && trimLabel.Contains("8.5")) { trimW = 5.5; trimH = 8.5; }
        else if (trimLabel.Contains("8.5") && trimLabel.Contains("11")) { trimW = 8.5; trimH = 11; }
        else if (trimLabel.Contains("8.5") && trimLabel.Contains("8.5")) { trimW = 8.5; trimH = 8.5; }
        else if (trimLabel.Contains("8") && trimLabel.Contains("10") && !trimLabel.Contains("8.5")) { trimW = 8; trimH = 10; }

        var pageCount = calcRow?.PagesCount > 0 ? calcRow.PagesCount : 200;
        var paper = PaperType.White;
        var projectType = coverType.Equals("Hardcover", StringComparison.OrdinalIgnoreCase)
            ? ProjectType.Hardcover
            : ProjectType.Paperback;
        var coverEnum = coverType.Equals("Ebook", StringComparison.OrdinalIgnoreCase)
            ? CoverType.EbookFront
            : coverType.Equals("Hardcover", StringComparison.OrdinalIgnoreCase)
                ? CoverType.HardcoverWrap
                : CoverType.PaperbackWrap;

        var spine = coverEnum == CoverType.EbookFront
            ? 0
            : _calc.CalculateSpineWidthIn(pageCount, paper, projectType);
        var (fullW, fullH) = _calc.CalculateFullCoverSizeIn(trimW, trimH, spine, coverEnum);
        var bleed = coverEnum == CoverType.EbookFront
            ? 0
            : (coverEnum == CoverType.HardcoverWrap ? _specs.Hardcover.WrapTurnInIn : _specs.BleedIn);
        return (trimW, trimH, spine, bleed, fullW, fullH);
    }

    private async Task<string> ResolvePreferredTypeAsync(int userId, int bookId, CancellationToken ct)
    {
        var fmt = await _db.BookFormatting.AsNoTracking()
            .Where(f => f.BookId == bookId && f.UserId == userId)
            .Select(f => f.Format)
            .FirstOrDefaultAsync(ct);
        return NormalizeType(fmt) ?? "Paperback";
    }

    private async Task<bool> OwnsBookAsync(int userId, int bookId, CancellationToken ct) =>
        await _db.Books.AsNoTracking().AnyAsync(b => b.BookId == bookId && b.UserId == userId, ct);

    private async Task<string?> ReadSettingAsync(string key, CancellationToken ct)
    {
        var v = await _db.Settings.AsNoTracking()
            .Where(s => s.Key == key)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    private async Task UpsertSettingAsync(string key, string value, CancellationToken ct)
    {
        value = Settings.ClampValueLength(value, Settings.MaxShortValueLength) ?? "";
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting == null)
        {
            _db.Settings.Add(new Settings
            {
                SettingId = await _db.NextSettingIdAsync(ct),
                Key = key,
                Value = value,
                Category = "Book",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static bool NeedsWrap(string type) =>
        !type.Equals("Ebook", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeType(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0) return null;
        if (t.Equals("Print", StringComparison.OrdinalIgnoreCase)) return "Paperback";
        if (t.Equals("Both", StringComparison.OrdinalIgnoreCase)) return "Paperback";
        if (t.Equals("Ebook", StringComparison.OrdinalIgnoreCase) || t.Equals("eBook", StringComparison.OrdinalIgnoreCase))
            return "Ebook";
        if (t.Equals("Paperback", StringComparison.OrdinalIgnoreCase)) return "Paperback";
        if (t.Equals("Hardcover", StringComparison.OrdinalIgnoreCase)) return "Hardcover";
        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return null;
    }
}
