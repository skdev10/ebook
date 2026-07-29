using Microsoft.AspNetCore.Http;

namespace EBookDashboard.Services;

/// <summary>Safe resume URLs for book workflow navigation (dashboard pick, login, middleware).</summary>
public static class BookResumeUrlHelper
{
    public static string PerBookSettingsKey(int bookId) => $"book:{bookId}:lastWorkUrl";
    public static string UserLastWorkKey(int userId) => $"user:{userId}:lastBookWorkUrl";

    public static bool IsSafeResumePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        path = path.Trim();
        if (path.Length > 600) return false;
        if (!path.StartsWith('/')) return false;
        if (path.StartsWith("//", StringComparison.Ordinal)) return false;
        if (path.Contains("://", StringComparison.Ordinal) || path.Contains('\\')) return false;
        return true;
    }

    /// <summary>Paths allowed when resuming after login.</summary>
    public static bool IsAllowedLoginResumePath(string path)
    {
        if (!IsSafeResumePath(path)) return false;
        var pathOnly = path.Split('?', 2)[0].TrimEnd('/');
        return pathOnly.StartsWith("/Dashboard", StringComparison.OrdinalIgnoreCase)
               || pathOnly.StartsWith("/Books/AIGenerateBook", StringComparison.OrdinalIgnoreCase)
               || pathOnly.StartsWith("/Books/Writer", StringComparison.OrdinalIgnoreCase)
               || pathOnly.StartsWith("/Books/Formatting", StringComparison.OrdinalIgnoreCase)
               || pathOnly.StartsWith("/Books/Cover", StringComparison.OrdinalIgnoreCase)
               || pathOnly.StartsWith("/BookDesign/CoverDesignCalculatorFixing", StringComparison.OrdinalIgnoreCase);
    }

    public static int TryParseBookIdFromWorkUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return 0;

        // /Books/Formatting/12 or /Books/Cover/12
        var pathOnly = url.Split('?', 2)[0].TrimEnd('/');
        foreach (var prefix in new[] { "/Books/Formatting/", "/Books/Cover/" })
        {
            if (pathOnly.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var tail = pathOnly[prefix.Length..];
                if (int.TryParse(tail, out var pathBid) && pathBid > 0)
                    return pathBid;
            }
        }

        var qIndex = url.IndexOf('?');
        if (qIndex < 0) return 0;
        var query = url[(qIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("bookId", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(Uri.UnescapeDataString(kv[1]), out var bid) && bid > 0)
                return bid;
        }
        return 0;
    }

    /// <summary>Restores session flags from persisted flow step so gated pages open after dashboard pick.</summary>
    public static void SyncFlowSessionFlags(HttpContext context, string? flowStep)
    {
        context.Session.SetString("HasGeneratedBook", "1");
        if (BookFlowStateService.IsStepAtLeast(flowStep, BookFlowStateService.StepFormat))
            context.Session.SetString("FormattingDone", "1");
    }

    public static void HydrateSessionFromFlowStep(HttpContext context, string? flowStep, ref bool hasGeneratedBook, ref bool formattingDone)
    {
        if (!BookFlowStateService.IsStepAtLeast(flowStep, BookFlowStateService.StepFormat)) return;
        SyncFlowSessionFlags(context, flowStep);
        hasGeneratedBook = true;
        formattingDone = true;
    }

    /// <summary>Deep links with ?bookId= must not require Dashboard pick first.</summary>
    public static void BootstrapOwnedBookSession(HttpContext context, int bookId, string? bookStatus, string? flowStep)
    {
        if (bookId <= 0) return;
        context.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);
        context.Session.SetInt32("LastSelectedBookId", bookId);
        if (BookPublishReadinessService.IsPublishReadyBookStatus(bookStatus))
            SyncFlowSessionFlags(context, BookFlowStateService.StepPublish);
        else if (!string.IsNullOrWhiteSpace(flowStep))
            SyncFlowSessionFlags(context, flowStep);
        else
            context.Session.SetString("HasGeneratedBook", "1");
    }

    /// <summary>True when the URL is the Publish / export screen (not an editing module).</summary>
    public static bool IsPublishScreenUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var pathOnly = path.Split('?', 2)[0].TrimEnd('/');
        return pathOnly.StartsWith("/Dashboard/Publish", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resume where the author last edited (Writer / Formatting / Cover).
    /// Visiting Publish must not force Continue Editing / Edit back to Publish.
    /// </summary>
    public static string ResolveResumeUrl(int bookId, string? lastWorkUrl, string flowStep, string flowPath, BookFlowStateService bookFlow)
    {
        if (bookId <= 0) return "/Dashboard";
        if (IsSafeResumePath(lastWorkUrl)
            && TryParseBookIdFromWorkUrl(lastWorkUrl!) == bookId
            && !IsPublishScreenUrl(lastWorkUrl))
        {
            return lastWorkUrl!.Trim();
        }

        // If flow is already on publish (or last URL was Publish), open Cover — last authoring screen.
        var step = flowStep;
        if (string.Equals(step, BookFlowStateService.StepPublish, StringComparison.OrdinalIgnoreCase)
            || IsPublishScreenUrl(lastWorkUrl))
        {
            step = BookFlowStateService.PreviousStep(BookFlowStateService.StepPublish)
                   ?? BookFlowStateService.StepCover;
        }

        return bookFlow.BuildResumeUrl(bookId, step, flowPath);
    }
}
