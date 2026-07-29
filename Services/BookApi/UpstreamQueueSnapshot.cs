namespace EBookDashboard.Services.BookApi;

/// <summary>Parsed snapshot from GET /api/queue-data.</summary>
public sealed class UpstreamQueueSnapshot
{
    public int Running { get; init; }
    public int Waiting { get; init; }
    public int MaxConcurrent { get; init; }
    public int TotalRequests { get; init; }

    /// <summary>
    /// Heuristic for a live backlog with idle workers.
    /// Upstream <c>waiting</c> is often cumulative (≈ total_requests); ignore that case.
    /// </summary>
    public bool IsStuck
    {
        get
        {
            if (Running > 0 || Waiting <= 0) return false;
            // Cumulative counter: waiting tracks almost every request ever queued.
            if (TotalRequests > 0 && Waiting >= Math.Max(0, TotalRequests - 2))
                return false;
            return Waiting >= Math.Max(5, MaxConcurrent);
        }
    }

    /// <summary>All worker slots busy with a backlog.</summary>
    public bool IsSaturated => MaxConcurrent > 0 && Running >= MaxConcurrent && Waiting > 0;

    public string Describe()
        => $"running={Running}, waiting={Waiting}, max_concurrent={MaxConcurrent}, total={TotalRequests}";
}
