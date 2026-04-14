using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models.DTO;

public class ChapterPlanCreateRequest
{
    /// <summary>0 = assign next available chapter number for this book.</summary>
    [Range(0, 999)]
    public int ChapterNumber { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = "";

    /// <summary>What the AI should write (brief, outline, or full instructions).</summary>
    [MaxLength(100_000)]
    public string? Prompt { get; set; }

    [MaxLength(500)]
    public string? Tone { get; set; }

    [Range(100, 200_000)]
    public int? TargetWordCount { get; set; }

    /// <summary>Stored in chapter SubTitle (short description).</summary>
    [MaxLength(2000)]
    public string? Description { get; set; }
}

public class ChapterPlanUpdateRequest
{
    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(100_000)]
    public string? Prompt { get; set; }

    [MaxLength(500)]
    public string? Tone { get; set; }

    [Range(100, 200_000)]
    public int? TargetWordCount { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }
}

public class ChapterGenerateApiRequest
{
    /// <summary>When true (default), response is not persisted as finalized manuscript — same as AI Writer preview.</summary>
    public bool PreviewOnly { get; set; } = true;

    /// <summary>Optional: replace stored prompt for this generation only.</summary>
    [MaxLength(100_000)]
    public string? OverridePrompt { get; set; }
}

public class BatchChapterGenerateRequest
{
    [Required]
    [MinLength(1)]
    public List<int> ChapterNumbers { get; set; } = new();

    /// <summary>When false (default), chapters are generated one after another to reduce load on the AI service.</summary>
    public bool Parallel { get; set; }

    public bool PreviewOnly { get; set; } = true;
}

public class ChapterListItemDto
{
    public int ChapterNumber { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public bool HasContent { get; set; }
    public int WordCount { get; set; }
    public string? Description { get; set; }
    public string? Tone { get; set; }
    public int? TargetWordCount { get; set; }
    public string? BriefPreview { get; set; }
}

public class ChapterOperationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int? ChapterNumber { get; set; }
}

public class ChapterGenerateResultDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int ChapterNumber { get; set; }
    public int? RawResponseId { get; set; }
    public string? RawJson { get; set; }
    /// <summary>How many HTTP attempts were made (1 = no retries).</summary>
    public int AttemptsUsed { get; set; }
    /// <summary>Final upstream HTTP status when available (0 if connection failed before response).</summary>
    public int LastHttpStatus { get; set; }
}
