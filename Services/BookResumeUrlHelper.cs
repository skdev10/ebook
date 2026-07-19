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
               || pathOnly.StartsWith("/BookDesign/CoverDesignCalculatorFixing", StringComparison.OrdinalIgnoreCase);
    }

    public static int TryParseBookIdFromWorkUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return 0;
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

    /// <summary>
    /// Resume where the author actually last worked — not the furthest step ever reached.
    /// Visiting Publish once must not force every later "Continue Editing" back to Publish.
    /// </summary>
    public static string ResolveResumeUrl(int bookId, string? lastWorkUrl, string flowStep, string flowPath, BookFlowStateService bookFlow)
    {
        if (bookId <= 0) return "/Dashboard";
        if (IsSafeResumePath(lastWorkUrl)
            && TryParseBookIdFromWorkUrl(lastWorkUrl!) == bookId)
        {
            return lastWorkUrl!.Trim();
        }
        return bookFlow.BuildResumeUrl(bookId, flowStep, flowPath);
    }
}
