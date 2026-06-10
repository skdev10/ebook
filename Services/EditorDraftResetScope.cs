namespace EBookDashboard.Services;

/// <summary>How aggressively to wipe temporary editor / workflow draft data.</summary>
public enum EditorDraftResetScope
{
    /// <summary>Destructive back one workflow step (cover → format, etc.).</summary>
    StepBack,

    /// <summary>Return to AI Writer — wipe formatting + cover assets; keep chapter text.</summary>
    BackToWriter,

    /// <summary>Full project reset — all draft settings, formatting, cover, uploads; keep chapters.</summary>
    FullProject,

    /// <summary>Nuclear reset — includes chapter content and API draft rows.</summary>
    FullProjectWithChapters
}
