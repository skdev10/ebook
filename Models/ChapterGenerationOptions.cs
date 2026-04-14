namespace EBookDashboard.Models;

/// <summary>Reliability settings for outbound chapter generation (API pipeline).</summary>
public class ChapterGenerationOptions
{
    public const string SectionName = "ChapterGeneration";

    /// <summary>Extra attempts after the first call (e.g. 4 = up to 5 total tries).</summary>
    public int MaxRetries { get; set; } = 4;

    public int RetryBaseDelaySeconds { get; set; } = 3;
    public int RetryMaxDelaySeconds { get; set; } = 120;

    /// <summary>Per-attempt HttpClient timeout (each try gets a fresh budget).</summary>
    public int HttpTimeoutMinutes { get; set; } = 30;

    /// <summary>Browser fetch() abort timeout for POST /Books/AIGenerateBook (ms budget so UI cannot spin forever if proxy/server stalls).</summary>
    public int BrowserFetchTimeoutMinutes { get; set; } = 12;

    /// <summary>When true, only one in-flight generation per (book, chapter); different chapters on the same book may run concurrently.</summary>
    public bool SerializeSameChapterOnly { get; set; } = true;
}
