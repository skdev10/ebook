using Microsoft.Extensions.Configuration;

namespace EBookDashboard.Services.BookApi;

/// <summary>Fail fast when upstream queue is stuck or overloaded (avoids multi-minute hangs).</summary>
public static class UpstreamQueueGuard
{
    public const string StuckMessage =
        "The book AI service queue is not processing requests right now (requests waiting but none running). " +
        "Please try again in a few minutes or ask your administrator to restart the upstream API workers on 162.229.248.26:8001.";

    public const string OverloadedMessage =
        "The book AI service is very busy ({0} requests waiting). Please wait a few minutes and try again.";

    public static string? GetBlockReason(UpstreamQueueSnapshot? snapshot, IConfiguration configuration)
    {
        if (snapshot == null) return null;

        var failWhenStuck = configuration.GetValue("ExternalApi:QueueFailFastWhenStuck", true);
        var maxWaiting = configuration.GetValue("ExternalApi:QueueMaxWaitingBeforeFailFast", 25);
        maxWaiting = Math.Clamp(maxWaiting, 1, 500);

        if (failWhenStuck && snapshot.IsStuck)
            return StuckMessage;

        if (snapshot.Waiting >= maxWaiting)
            return string.Format(OverloadedMessage, snapshot.Waiting);

        return null;
    }
}
