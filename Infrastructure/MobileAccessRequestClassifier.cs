namespace EBookDashboard.Infrastructure;

/// <summary>Decides whether a request targets user-facing HTML vs API/static assets.</summary>
public static class MobileAccessRequestClassifier
{
    private static readonly string[] ExcludedPathPrefixes =
    [
        "/api/",
        "/swagger",
        "/health",
        "/hubs/",
        "/book-covers/",
        "/css/",
        "/js/",
        "/lib/",
        "/images/",
        "/fonts/",
        "/favicon.ico"
    ];

    private static readonly string[] StaticFileExtensions =
    [
        ".css", ".js", ".map", ".ico", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg",
        ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip"
    ];

    /// <summary>Returns true when mobile blocking should run for this request.</summary>
    public static bool ShouldBlockMobileBrowser(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        if (IsExcludedPath(path))
            return false;

        if (IsApiOrNonHtmlRequest(request))
            return false;

        return HttpMethods.IsGet(request.Method)
               || HttpMethods.IsHead(request.Method)
               || HttpMethods.IsPost(request.Method)
               || HttpMethods.IsPut(request.Method)
               || HttpMethods.IsPatch(request.Method)
               || HttpMethods.IsDelete(request.Method);
    }

    private static bool IsExcludedPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        foreach (var prefix in ExcludedPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var ext in StaticFileExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsApiOrNonHtmlRequest(HttpRequest request)
    {
        if (request.Headers.TryGetValue("X-Requested-With", out var xhr)
            && string.Equals(xhr.ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
            return true;

        var contentType = request.ContentType ?? string.Empty;
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            return true;

        if (request.Headers.TryGetValue("Accept", out var acceptValues))
        {
            foreach (var accept in acceptValues)
            {
                if (string.IsNullOrWhiteSpace(accept))
                    continue;

                var prefersJson = accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);
                var acceptsHtml = accept.Contains("text/html", StringComparison.OrdinalIgnoreCase)
                                  || accept.Contains("*/*", StringComparison.OrdinalIgnoreCase);

                if (prefersJson && !acceptsHtml)
                    return true;
            }
        }

        return false;
    }
}
