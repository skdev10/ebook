using System.Diagnostics;

namespace EBookDashboard.Services.BookApi;

/// <summary>Structured logging for upstream calls: method, path, status, elapsed — no API key or body.</summary>
public sealed class BookApiLoggingHandler : DelegatingHandler
{
    private readonly ILogger<BookApiLoggingHandler> _logger;

    public BookApiLoggingHandler(ILogger<BookApiLoggingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var path = request.RequestUri?.PathAndQuery ?? request.RequestUri?.ToString() ?? "";
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "BookApi {Method} {Path} -> {Status} in {ElapsedMs}ms",
                request.Method,
                path,
                (int)response.StatusCode,
                sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BookApi {Method} {Path} failed after {ElapsedMs}ms", request.Method, path, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
