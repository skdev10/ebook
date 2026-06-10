namespace EBookDashboard.Services;

/// <summary>Outcome of an editor draft reset operation.</summary>
public sealed class EditorDraftResetResult
{
    public bool Success { get; init; } = true;
    public string? Message { get; init; }
    public int BookId { get; init; }
    public string Step { get; init; } = BookFlowStateService.StepGenerate;
    public string ResumeUrl { get; init; } = "/Dashboard";
    public bool Wiped { get; init; }
}
