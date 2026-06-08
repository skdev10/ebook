using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services.BookApi;

/// <summary>Optional fail-fast when upstream queue looks stuck (off by default — queue counters are often stale).</summary>
public static class UpstreamQueueGuard
{
    public const string StuckMessage =
        "The book AI service queue is not processing requests right now (requests waiting but none running). " +
        "Please try again in a few minutes or ask your administrator to restart the upstream API workers on 162.229.248.26:8001.";

    public const string OverloadedMessage =
        "The book AI service is very busy ({0} requests waiting). Please wait a few minutes and try again.";

    /// <summary>Returns a user-facing block reason only when explicitly enabled via config (disabled by default).</summary>
    public static string? GetBlockReason(UpstreamQueueSnapshot? snapshot, IConfiguration configuration)
    {
        if (snapshot == null) return null;

        // Off by default: upstream waiting/running counters are cumulative and often show waiting>0, running=0
        // while generate/edit still succeed. Blocking caused false "queue stuck" errors on local and live.
        var failWhenStuck = configuration.GetValue("ExternalApi:QueueFailFastWhenStuck", false);
        var maxWaiting = configuration.GetValue("ExternalApi:QueueMaxWaitingBeforeFailFast", 0);
        if (maxWaiting > 0)
            maxWaiting = Math.Clamp(maxWaiting, 1, 5000);

        if (failWhenStuck && snapshot.IsStuck)
            return StuckMessage;

        if (maxWaiting > 0 && snapshot.Waiting >= maxWaiting)
            return string.Format(OverloadedMessage, snapshot.Waiting);

        return null;
    }
}
