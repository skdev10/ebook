using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services.BookApi;

/// <summary>Adds <c>X-API-Key</c> to every outbound upstream request (never logs the value).</summary>
public sealed class BookApiAuthenticationHandler : DelegatingHandler
{
    private readonly IConfiguration _configuration;

    public BookApiAuthenticationHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = ExternalApiKeyResolver.Resolve(_configuration);
        if (!string.IsNullOrEmpty(key))
            request.Headers.TryAddWithoutValidation("X-API-Key", key);
        return base.SendAsync(request, cancellationToken);
    }
}
