using System.Collections.Concurrent;
using System.Text.Json;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Services;

public interface ICoverGenerationJobQueue
{
    /// <summary>Starts background cover generation; returns immediately.</summary>
    void Start(int userId, DashboardGenerateCoverRequest req);

    /// <summary>Current job state for polling from Cover Design.</summary>
    CoverGenerationJobSnapshot? GetStatus(int userId, int bookId);
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

/// <summary>Background front-cover jobs so nginx/browser 504 does not abort upstream AI calls.</summary>
public sealed class CoverGenerationJobQueue : ICoverGenerationJobQueue
{
    private static readonly ConcurrentDictionary<string, CoverGenerationJobSnapshot> Jobs = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CoverGenerationJobQueue> _logger;

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
        Jobs[key] = new CoverGenerationJobSnapshot
        {
            Status = "processing",
            Message = "Generating front cover via AI…",
            UpdatedUtc = DateTime.UtcNow
        };

        var reqCopy = JsonSerializer.Deserialize<DashboardGenerateCoverRequest>(JsonSerializer.Serialize(req))!;

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("Background front cover generation started for book {BookId}", reqCopy.BookId);
                using var scope = _scopeFactory.CreateScope();
                var gen = scope.ServiceProvider.GetRequiredService<ICoverFrontGenerationService>();
                var result = await gen.GenerateAsync(userId, reqCopy, CancellationToken.None);

                Jobs[key] = new CoverGenerationJobSnapshot
                {
                    Status = result.Success ? "complete" : "error",
                    Message = result.Success ? "Cover ready." : result.Message,
                    CoverUrl = result.CoverUrl,
                    Options = result.Options,
                    ImageBase64 = result.ImageBase64,
                    ImageDataUrl = result.ImageDataUrl,
                    UpdatedUtc = DateTime.UtcNow
                };

                _logger.LogInformation(
                    "Background front cover generation {Outcome} for book {BookId}",
                    result.Success ? "completed" : "failed",
                    reqCopy.BookId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background front cover generation crashed for book {BookId}", reqCopy.BookId);
                Jobs[key] = new CoverGenerationJobSnapshot
                {
                    Status = "error",
                    Message = ex.Message,
                    UpdatedUtc = DateTime.UtcNow
                };
            }
        });
    }

    public CoverGenerationJobSnapshot? GetStatus(int userId, int bookId)
    {
        Jobs.TryGetValue(Key(userId, bookId), out var snap);
        return snap;
    }
}
