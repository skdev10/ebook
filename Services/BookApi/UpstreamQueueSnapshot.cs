namespace EBookDashboard.Services.BookApi;

/// <summary>Parsed snapshot from GET /api/queue-data.</summary>
public sealed class UpstreamQueueSnapshot
{
    public int Running { get; init; }
    public int Waiting { get; init; }
    public int MaxConcurrent { get; init; }
    public int TotalRequests { get; init; }

    /// <summary>Workers idle while backlog exists — upstream is stuck.</summary>
    public bool IsStuck => Waiting > 0 && Running == 0;

    /// <summary>All worker slots busy with a backlog.</summary>
    public bool IsSaturated => MaxConcurrent > 0 && Running >= MaxConcurrent && Waiting > 0;

    public string Describe()
        => $"running={Running}, waiting={Waiting}, max_concurrent={MaxConcurrent}, total={TotalRequests}";
}
