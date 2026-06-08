using EBookDashboard.Services;
using EBookDashboard.Services.BookApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EBookDashboard.Health;

public sealed class UpstreamBookApiHealthCheck : IHealthCheck
{
    private readonly IUpstreamQueueProbe _queueProbe;
    private readonly IConfiguration _configuration;

    public UpstreamBookApiHealthCheck(IUpstreamQueueProbe queueProbe, IConfiguration configuration)
    {
        _queueProbe = queueProbe;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ExternalApiKeyResolver.Resolve(_configuration)))
            return HealthCheckResult.Degraded("Upstream API key is not configured (ExternalApi__ApiKey).");

        var snapshot = await _queueProbe.TryGetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (snapshot == null)
            return HealthCheckResult.Degraded("Upstream GET /api/queue-data failed or returned invalid JSON.");

        if (snapshot.IsStuck)
            return HealthCheckResult.Degraded(
                $"Upstream queue stuck: {snapshot.Describe()}. Restart FastAPI workers on the book API server.");

        var block = UpstreamQueueGuard.GetBlockReason(snapshot, _configuration);
        if (block != null)
            return HealthCheckResult.Degraded($"Upstream overloaded: {snapshot.Describe()}.");

        return HealthCheckResult.Healthy($"Upstream queue OK ({snapshot.Describe()}).");
    }
}
