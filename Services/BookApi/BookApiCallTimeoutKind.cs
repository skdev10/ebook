namespace EBookDashboard.Services.BookApi;

public enum BookApiCallTimeoutKind
{
    /// <summary>~30s attempt budget (approve, audio metadata, etc.).</summary>
    Standard,

    /// <summary>~90s for GET /api/queue-data when upstream or network is slow.</summary>
    QueueProbe,

    /// <summary>LLM-heavy endpoints (generate/edit chapter, cover generate/edit). Uses ChapterGeneration:HttpTimeoutMinutes.</summary>
    LongRunning
}
