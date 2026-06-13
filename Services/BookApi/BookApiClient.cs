using EBookDashboard.Models.Options;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services.BookApi;

public sealed class BookApiClient : IBookApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ExternalApiOptions> _options;

    public BookApiClient(IHttpClientFactory httpClientFactory, IOptions<ExternalApiOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public string ResolveUrl(string? configuredFullUrl, string relativePath) =>
        _options.Value.ResolveUrl(configuredFullUrl, relativePath);

    public Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        BookApiCallTimeoutKind timeoutKind,
        CancellationToken cancellationToken = default)
    {
        var name = timeoutKind switch
        {
            BookApiCallTimeoutKind.LongRunning => BookApiConstants.HttpClientNameLong,
            BookApiCallTimeoutKind.QueueProbe => BookApiConstants.HttpClientNameQueue,
            _ => BookApiConstants.HttpClientNameShort
        };
        var client = _httpClientFactory.CreateClient(name);
        return client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}
