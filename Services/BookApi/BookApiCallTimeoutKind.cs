namespace EBookDashboard.Services.BookApi;

public enum BookApiCallTimeoutKind
{
    /// <summary>~30s attempt budget (queue, approve, edit, audio metadata, etc.).</summary>
    Standard,

    /// <summary>~120s for LLM-heavy endpoints (generate chapter, cover generate/edit).</summary>
    LongRunning
}
