using System.Text.Json;
using EBookDashboard.Models.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services.BookApi;

public interface IUpstreamQueueProbe
{
    Task<UpstreamQueueSnapshot?> TryGetSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads upstream queue status (fast GET) to detect stuck/saturated workers before LLM POSTs.</summary>
public sealed class UpstreamQueueProbe : IUpstreamQueueProbe
{
    private readonly IBookApiClient _bookApiClient;
    private readonly IOptionsSnapshot<ExternalApiOptions> _options;
    private readonly ILogger<UpstreamQueueProbe> _logger;

    public UpstreamQueueProbe(
        IBookApiClient bookApiClient,
        IOptionsSnapshot<ExternalApiOptions> options,
        ILogger<UpstreamQueueProbe> logger)
    {
        _bookApiClient = bookApiClient;
        _options = options;
        _logger = logger;
    }

    public async Task<UpstreamQueueSnapshot?> TryGetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var url = _bookApiClient.ResolveUrl(_options.Value.QueueDataUrl, "/api/queue-data");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _bookApiClient.SendAsync(req, BookApiCallTimeoutKind.Standard, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Queue probe HTTP {Code}: {Body}", (int)resp.StatusCode, Truncate(json, 200));
                return null;
            }
            return Parse(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Queue probe failed for {Url}", url);
            return null;
        }
    }

    internal static UpstreamQueueSnapshot? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("status", out var status))
                return null;
            return new UpstreamQueueSnapshot
            {
                Running = ReadInt(status, "running"),
                Waiting = ReadInt(status, "waiting"),
                MaxConcurrent = ReadInt(status, "max_concurrent"),
                TotalRequests = ReadInt(status, "total_requests")
            };
        }
        catch
        {
            return null;
        }
    }

    private static int ReadInt(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.TryGetInt32(out var v) ? v : 0;

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}
