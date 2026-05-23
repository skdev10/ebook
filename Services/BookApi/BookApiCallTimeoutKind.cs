namespace EBookDashboard.Services.BookApi;

public enum BookApiCallTimeoutKind
{
    /// <summary>~30s attempt budget (queue, approve, audio metadata, etc.).</summary>
    Standard,

    /// <summary>LLM-heavy endpoints (generate/edit chapter, cover generate/edit). Uses ChapterGeneration:HttpTimeoutMinutes.</summary>
    LongRunning
}
