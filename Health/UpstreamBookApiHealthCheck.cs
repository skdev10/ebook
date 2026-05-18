using Microsoft.Extensions.Configuration;
using EBookDashboard.Models.Options;
using EBookDashboard.Services;
using EBookDashboard.Services.BookApi;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Health;

public sealed class UpstreamBookApiHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ExternalApiOptions> _options;
    private readonly IConfiguration _configuration;

    public UpstreamBookApiHealthCheck(
        IHttpClientFactory httpClientFactory,
        IOptions<ExternalApiOptions> options,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _configuration = configuration;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ExternalApiKeyResolver.Resolve(_configuration)))
            return HealthCheckResult.Degraded("Upstream API key is not configured (ExternalApi__ApiKey).");

        var url = _options.Value.ResolveUrl(_options.Value.QueueDataUrl, "/api/queue-data");
        try
        {
            var client = _httpClientFactory.CreateClient(BookApiConstants.HttpClientNameShort);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await client
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
                return HealthCheckResult.Healthy("Upstream GET /api/queue-data succeeded.");
            return HealthCheckResult.Degraded($"Upstream returned HTTP {(int)resp.StatusCode}.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Upstream queue probe failed.", ex);
        }
    }
}
