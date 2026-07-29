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
                $"Upstream queue may be stuck ({snapshot.Describe()}). Restart AI workers on :8001 if chapter/cover stays pending.");

        if (snapshot.Running > 0)
            return HealthCheckResult.Healthy(
                $"Upstream busy ({snapshot.Describe()}). New chapter/cover jobs may wait their turn.");

        return HealthCheckResult.Healthy($"Upstream queue OK ({snapshot.Describe()}).");
    }
}
