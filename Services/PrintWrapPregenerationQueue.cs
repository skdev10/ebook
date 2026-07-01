using EBookDashboard.Interfaces;

namespace EBookDashboard.Services;

/// <summary>Background fire-and-forget wrapper around <see cref="IPrintWrapGenerationService"/>.</summary>
public sealed class PrintWrapPregenerationQueue : IPrintWrapPregenerationQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PrintWrapPregenerationQueue> _logger;

    public PrintWrapPregenerationQueue(IServiceScopeFactory scopeFactory, ILogger<PrintWrapPregenerationQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public void QueueAfterFrontCoverSaved(int userId, int bookId)
    {
        if (userId <= 0 || bookId <= 0) return;

        _logger.LogInformation("Queued background print wrap generation for book {BookId} user {UserId}", bookId, userId);

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogDebug("Background print wrap generation starting for book {BookId}", bookId);
                using var scope = _scopeFactory.CreateScope();
                var gen = scope.ServiceProvider.GetRequiredService<IPrintWrapGenerationService>();
                var ok = await gen.TryGenerateFromSavedFrontAsync(
                    userId,
                    bookId,
                    pageCountOverride: null,
                    forceRegenerate: true,
                    cancellationToken: CancellationToken.None);
                _logger.LogInformation(
                    ok
                        ? "Background print wrap generation completed for book {BookId}"
                        : "Background print wrap generation skipped or failed for book {BookId}",
                    bookId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background print wrap queue failed for book {BookId} user {UserId}", bookId, userId);
            }
        });
    }
}
