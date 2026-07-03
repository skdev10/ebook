using System.Collections.Concurrent;
using System.Text.Json;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

public interface ICoverGenerationJobQueue
{
    void Start(int userId, DashboardGenerateCoverRequest req);
    CoverGenerationJobSnapshot? GetStatus(int userId, int bookId);
    Task<CoverGenerationJobSnapshot?> LoadStatusFromDbAsync(int bookId, CancellationToken cancellationToken);
}

public sealed class CoverGenerationJobSnapshot
{
    public string Status { get; init; } = "idle";
    public string Message { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string[] Options { get; init; } = Array.Empty<string>();
    public string? ImageBase64 { get; init; }
    public string? ImageDataUrl { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

/// <summary>Background front-cover jobs — survives gateway timeouts; status persisted to Settings for reliable polling.</summary>
public sealed class CoverGenerationJobQueue : ICoverGenerationJobQueue
{
    private static readonly ConcurrentDictionary<string, CoverGenerationJobSnapshot> Jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CoverGenerationJobQueue> _logger;

    private static string StatusKey(int bookId) => $"book:{bookId}:frontCoverGenStatus";
    private static string MessageKey(int bookId) => $"book:{bookId}:frontCoverGenMessage";
    private static string CoverUrlKey(int bookId) => $"book:{bookId}:frontCoverGenCoverUrl";
    private static string OptionsKey(int bookId) => $"book:{bookId}:frontCoverGenOptions";

    public CoverGenerationJobQueue(IServiceScopeFactory scopeFactory, ILogger<CoverGenerationJobQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    private static string Key(int userId, int bookId) => $"{userId}:{bookId}";

    public void Start(int userId, DashboardGenerateCoverRequest req)
    {
        if (userId <= 0 || req.BookId <= 0) return;

        var key = Key(userId, req.BookId);
        var existing = GetStatus(userId, req.BookId);
        if (existing?.Status == "processing")
        {
            _logger.LogInformation("Cover generation already in progress for book {BookId}", req.BookId);
            return;
        }

        var processing = new CoverGenerationJobSnapshot
        {
            Status = "processing",
            Message = "Generating front cover via AI…",
            UpdatedUtc = DateTime.UtcNow
        };
        Jobs[key] = processing;
        _ = PersistSnapshotAsync(req.BookId, processing);

        var reqCopy = JsonSerializer.Deserialize<DashboardGenerateCoverRequest>(JsonSerializer.Serialize(req))!;

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Background front cover generation started for book {BookId}", reqCopy.BookId);
                using var scope = _scopeFactory.CreateScope();
                var gen = scope.ServiceProvider.GetRequiredService<ICoverFrontGenerationService>();
                var result = await gen.GenerateAsync(userId, reqCopy, CancellationToken.None);

                var snap = new CoverGenerationJobSnapshot
                {
                    Status = result.Success ? "complete" : "error",
                    Message = result.Success ? "Cover ready." : result.Message,
                    CoverUrl = result.CoverUrl,
                    Options = result.Options,
                    ImageBase64 = result.ImageBase64,
                    ImageDataUrl = result.ImageDataUrl,
                    UpdatedUtc = DateTime.UtcNow
                };
                Jobs[key] = snap;
                await PersistSnapshotAsync(reqCopy.BookId, snap);

                _logger.LogInformation(
                    "Background front cover generation {Outcome} for book {BookId}",
                    result.Success ? "completed" : "failed",
                    reqCopy.BookId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background front cover generation crashed for book {BookId}", reqCopy.BookId);
                var err = new CoverGenerationJobSnapshot
                {
                    Status = "error",
                    Message = FormatSaveError(ex),
                    UpdatedUtc = DateTime.UtcNow
                };
                Jobs[key] = err;
                await PersistSnapshotAsync(reqCopy.BookId, err);
            }
        });
    }

    private static string FormatSaveError(Exception ex)
    {
        if (ex is Microsoft.EntityFrameworkCore.DbUpdateException dbEx && dbEx.InnerException != null)
            return dbEx.InnerException.Message;
        return ex.Message;
    }

    public CoverGenerationJobSnapshot? GetStatus(int userId, int bookId)
    {
        Jobs.TryGetValue(Key(userId, bookId), out var snap);
        return snap;
    }

    public async Task<CoverGenerationJobSnapshot?> LoadStatusFromDbAsync(int bookId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var keys = new[] { StatusKey(bookId), MessageKey(bookId), CoverUrlKey(bookId), OptionsKey(bookId) };
        var rows = await db.Settings.AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);

        var status = rows.GetValueOrDefault(StatusKey(bookId), "").Trim();
        if (string.IsNullOrEmpty(status)) return null;

        string[] options = Array.Empty<string>();
        var optJson = rows.GetValueOrDefault(OptionsKey(bookId), "").Trim();
        if (!string.IsNullOrEmpty(optJson))
        {
            try { options = JsonSerializer.Deserialize<string[]>(optJson) ?? Array.Empty<string>(); }
            catch (JsonException) { /* ignore */ }
        }

        return new CoverGenerationJobSnapshot
        {
            Status = status,
            Message = rows.GetValueOrDefault(MessageKey(bookId), "").Trim(),
            CoverUrl = rows.GetValueOrDefault(CoverUrlKey(bookId), "").Trim(),
            Options = options,
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private async Task PersistSnapshotAsync(int bookId, CoverGenerationJobSnapshot snap)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await UpsertAsync(db, StatusKey(bookId), snap.Status);
            await UpsertAsync(db, MessageKey(bookId), Settings.ClampValueLength(snap.Message ?? "", Settings.MaxShortValueLength) ?? "");
            await UpsertAsync(db, CoverUrlKey(bookId), Settings.ClampValueLength(snap.CoverUrl ?? "", Settings.MaxShortValueLength) ?? "");
            var optJson = snap.Options.Length > 0 ? JsonSerializer.Serialize(snap.Options) : "";
            optJson = Settings.ClampValueLength(optJson, Settings.MaxShortValueLength) ?? "";
            await UpsertAsync(db, OptionsKey(bookId), optJson);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist cover job status for book {BookId}", bookId);
        }
    }

    private static async Task UpsertAsync(ApplicationDbContext db, string key, string value)
    {
        value = Settings.ClampValueLength(value ?? "", Settings.MaxShortValueLength) ?? "";
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
        {
            row = new Settings
            {
                SettingId = await db.NextSettingIdAsync(),
                Key = key,
                Category = "Book",
                CreatedAt = DateTime.UtcNow
            };
            db.Settings.Add(row);
        }
        row.Value = value;
        row.UpdatedAt = DateTime.UtcNow;
    }
}
