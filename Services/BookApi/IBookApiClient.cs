namespace EBookDashboard.Services.BookApi;

public interface IBookApiClient
{
    string ResolveUrl(string? configuredFullUrl, string relativePath);

    Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        BookApiCallTimeoutKind timeoutKind,
        CancellationToken cancellationToken = default);
}
