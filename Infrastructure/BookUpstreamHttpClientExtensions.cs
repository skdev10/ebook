using EBookDashboard.Models.Options;
using EBookDashboard.Services.BookApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Infrastructure;

public static class BookUpstreamHttpClientExtensions
{
    public static IServiceCollection AddBookUpstreamHttpClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<ExternalApiOptions>, ExternalApiOptionsValidator>();
        services.AddOptions<ExternalApiOptions>()
            .BindConfiguration(ExternalApiOptions.SectionName)
            .ValidateOnStart();

        services.AddTransient<BookApiAuthenticationHandler>();
        services.AddTransient<BookApiLoggingHandler>();

        AddClient(services, BookApiConstants.HttpClientNameShort, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(48), maxRetries: 2);

        var longMins = BookApiUpstreamCancellation.ResolveTimeoutMinutes(configuration);
        var longAttempt = TimeSpan.FromMinutes(longMins);
        var longTotal = TimeSpan.FromMinutes(Math.Min(longMins + 10, 120));
        // LLM calls block until upstream finishes — retries only multiply wait time on failure.
        AddClient(services, BookApiConstants.HttpClientNameLong, longAttempt, longTotal, maxRetries: 1);

        services.AddScoped<IBookApiClient, BookApiClient>();
        return services;
    }

    private static void AddClient(IServiceCollection services, string name, TimeSpan attemptTimeout, TimeSpan totalTimeout, int maxRetries = 3)
    {
        services.AddHttpClient(name, client => { client.Timeout = Timeout.InfiniteTimeSpan; })
            .AddHttpMessageHandler<BookApiLoggingHandler>()
            .AddHttpMessageHandler<BookApiAuthenticationHandler>()
            .AddStandardResilienceHandler(options =>
            {
                // Standard resilience requires MaxRetryAttempts >= 1 (0 crashes on startup).
                options.Retry.MaxRetryAttempts = Math.Max(1, maxRetries);
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.MaxDelay = TimeSpan.FromSeconds(20);
                options.AttemptTimeout.Timeout = attemptTimeout;
                options.TotalRequestTimeout.Timeout = totalTimeout;
                // Circuit breaker sampling window must be >= 2× attempt timeout (library validation).
                var minSampling = TimeSpan.FromTicks(attemptTimeout.Ticks * 2);
                options.CircuitBreaker.SamplingDuration =
                    minSampling > TimeSpan.FromMinutes(2) ? minSampling : TimeSpan.FromMinutes(2);
            });
    }
}
