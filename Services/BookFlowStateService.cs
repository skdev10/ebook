using EBookDashboard.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

/// <summary>Persists per-book creation flow step in Settings for dashboard resume.</summary>
public sealed class BookFlowStateService
{
    public const string StepGenerate = "generate";
    public const string StepFormat = "format";
    public const string StepCover = "cover";
    public const string StepPublish = "publish";
    /// <summary>Session key: user selected this book from Dashboard before entering the flow.</summary>
    public const string SessionEntryBookIdKey = "BookFlowEntryBookId";

    private readonly ApplicationDbContext _context;

    public BookFlowStateService(ApplicationDbContext context) => _context = context;

    public static bool IsPublishedStatus(string? status)
    {
        var s = (status ?? "").Trim();
        return s.Equals("Published", StringComparison.OrdinalIgnoreCase)
               || s.Equals("Finalized", StringComparison.OrdinalIgnoreCase)
               || s.Equals("Final", StringComparison.OrdinalIgnoreCase)
               || s.Equals("Paid", StringComparison.OrdinalIgnoreCase);
    }

    public static int StepToPercent(string? step) => (step ?? "").Trim().ToLowerInvariant() switch
    {
        StepFormat => 40,
        StepCover => 65,
        StepPublish => 85,
        _ => 20
    };

    public static string StepToLabel(string? step) => (step ?? "").Trim().ToLowerInvariant() switch
    {
        StepFormat => "Book Formatting",
        StepCover => "AI Cover Design",
        StepPublish => "Publish",
        _ => "AI Writer"
    };

    public static int StepRank(string? step) => (step ?? "").Trim().ToLowerInvariant() switch
    {
        StepPublish => 4,
        StepCover => 3,
        StepFormat => 2,
        StepGenerate => 1,
        _ => 0
    };

    public static bool IsStepAtLeast(string? current, string requiredStep) =>
        StepRank(current) >= StepRank(requiredStep);

    /// <summary>True when the user picked this book from the Dashboard before entering the flow.</summary>
    public static bool SessionEntryMatches(HttpContext context, int bookId)
    {
        if (bookId <= 0) return false;
        var entry = context.Session.GetInt32(SessionEntryBookIdKey);
        return entry.HasValue && entry.Value == bookId;
    }

    public async Task SaveStepAsync(int bookId, string step, string? formatPath = null, CancellationToken ct = default)
    {
        if (bookId <= 0 || string.IsNullOrWhiteSpace(step)) return;
        await UpsertAsync($"book:{bookId}:flowStep", step.Trim(), ct);
        if (!string.IsNullOrWhiteSpace(formatPath))
            await UpsertAsync($"book:{bookId}:flowPath", formatPath.Trim(), ct);
        await UpsertAsync($"book:{bookId}:flowUpdatedAt", DateTime.UtcNow.ToString("O"), ct);
    }

    public async Task<(string Step, string Path)> GetStepAsync(int bookId, CancellationToken ct = default)
    {
        if (bookId <= 0) return (StepGenerate, "ebook");
        var keys = new[] { $"book:{bookId}:flowStep", $"book:{bookId}:flowPath" };
        var rows = await _context.Settings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", ct);
        var step = rows.GetValueOrDefault($"book:{bookId}:flowStep", "").Trim();
        if (string.IsNullOrEmpty(step)) step = StepGenerate;
        var path = rows.GetValueOrDefault($"book:{bookId}:flowPath", "").Trim();
        if (string.IsNullOrEmpty(path)) path = "ebook";
        return (step, path);
    }

    public string BuildResumeUrl(int bookId, string step, string formatPath)
    {
        var enc = Uri.EscapeDataString(bookId.ToString());
        return step switch
        {
            StepFormat => formatPath.Equals("print", StringComparison.OrdinalIgnoreCase)
                ? $"/BookDesign/CoverDesignCalculatorFixing?bookId={enc}&format=Paperback"
                : $"/BookDesign/CoverDesignCalculatorFixing?bookId={enc}&format=Ebook",
            StepCover => $"/Dashboard/CoverDesign?bookId={enc}",
            StepPublish => formatPath.Equals("print", StringComparison.OrdinalIgnoreCase)
                ? $"/Dashboard/Publish?bookId={enc}&flow=printready"
                : $"/Dashboard/Publish?bookId={enc}",
            _ => $"/Books/AIGenerateBook?bookId={enc}"
        };
    }

    public static string? PreviousStep(string? step) => (step ?? "").Trim().ToLowerInvariant() switch
    {
        StepCover => StepFormat,
        StepFormat => StepGenerate,
        StepPublish => StepCover,
        _ => null
    };

    /// <summary>Move flow back one step without deleting saved work (soft back navigation).</summary>
    public async Task RegressStepAsync(int bookId, string currentStep, CancellationToken ct = default)
    {
        if (bookId <= 0 || string.IsNullOrWhiteSpace(currentStep)) return;
        var prev = PreviousStep(currentStep);
        if (prev == null) return;
        var (_, path) = await GetStepAsync(bookId, ct);
        await SaveStepAsync(bookId, prev, path, ct);
    }

    /// <summary>Hard-reset the current step and move flow back one step (destructive — wipe flows only).</summary>
    public async Task RegressAndResetAsync(int bookId, string currentStep, CancellationToken ct = default)
    {
        if (bookId <= 0 || string.IsNullOrWhiteSpace(currentStep)) return;
        await ResetStepAsync(bookId, currentStep, ct);
        var prev = PreviousStep(currentStep);
        if (prev == null) return;
        var (_, path) = await GetStepAsync(bookId, ct);
        await SaveStepAsync(bookId, prev, path, ct);
    }

    /// <summary>Clears persisted assets for the step the user is leaving when navigating back.</summary>
    public async Task ResetStepAsync(int bookId, string step, CancellationToken ct = default)
    {
        if (bookId <= 0 || string.IsNullOrWhiteSpace(step)) return;
        var normalized = step.Trim().ToLowerInvariant();
        var keys = normalized switch
        {
            StepCover => new[]
            {
                $"book:{bookId}:aiCoverLastPreview",
                $"book:{bookId}:aiCoverPrompt",
                $"book:{bookId}:printReadyCoverFront",
                $"book:{bookId}:printReadyCoverWrap",
                $"book:{bookId}:printReadyCoverWrapApi",
                $"book:{bookId}:printReadyCoverBack",
                $"book:{bookId}:printReadyCoverSpine",
                $"book:{bookId}:printReadySpineInches",
                $"book:{bookId}:printReadyPageCount",
                $"book:{bookId}:printReadyTrimSize"
            },
            StepFormat => new[] { $"book:{bookId}:formattingDraft" },
            StepGenerate => new[]
            {
                $"book:{bookId}:formattingDraft",
                $"book:{bookId}:aiCoverLastPreview",
                $"book:{bookId}:printReadyCoverFront",
                $"book:{bookId}:printReadyCoverWrap",
                $"book:{bookId}:printReadyCoverWrapApi"
            },
            _ => Array.Empty<string>()
        };
        if (keys.Length == 0) return;
        var rows = await _context.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(ct);
        if (rows.Count == 0) return;
        _context.Settings.RemoveRange(rows);
        await _context.SaveChangesAsync(ct);
    }

    private async Task UpsertAsync(string key, string value, CancellationToken ct)
    {
        value = Models.Settings.ClampValueLength(value, Models.Settings.MaxShortValueLength) ?? "";
        var row = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row == null)
        {
            var id = await _context.NextSettingIdAsync(ct);
            _context.Settings.Add(new Models.Settings
            {
                SettingId = id,
                Key = key,
                Value = value,
                Category = "BookFlow",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            row.Value = value;
            row.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync(ct);
    }
}
