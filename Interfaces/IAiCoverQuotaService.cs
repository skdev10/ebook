namespace EBookDashboard.Interfaces;

public sealed class AiCoverQuotaSnapshot
{
    public int Used { get; set; }
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public bool LimitReached { get; set; }
}

public sealed class AiCoverReserveResult
{
    public bool Reserved { get; set; }
    public AiCoverQuotaSnapshot Snapshot { get; set; } = new();
}

public interface IAiCoverQuotaService
{
    /// <summary>Reads the user's lifetime AI cover quota.</summary>
    Task<AiCoverQuotaSnapshot> GetAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Atomically consumes one lifetime use when under the cap.</summary>
    Task<AiCoverReserveResult> TryReserveAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Refunds one use after a failed generation.</summary>
    Task ReleaseAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Sets used generations back to zero. Does not change the cap.</summary>
    Task ResetAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Sets a custom lifetime cap for this user (admin).</summary>
    Task SetLimitAsync(int userId, int limit, CancellationToken cancellationToken = default);
}
