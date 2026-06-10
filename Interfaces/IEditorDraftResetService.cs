using EBookDashboard.Services;
using Microsoft.AspNetCore.Http;

namespace EBookDashboard.Interfaces;

/// <summary>Clears temporary editor drafts (session, Settings, uploads, optional chapters).</summary>
public interface IEditorDraftResetService
{
    /// <summary>Runs a scoped reset for the given book and syncs HTTP session flags.</summary>
    Task<EditorDraftResetResult> ResetAsync(
        int bookId,
        int userId,
        EditorDraftResetScope scope,
        string? currentStep,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>Removes stored cover asset Settings rows (used before cover regeneration).</summary>
    Task ClearCoverAssetsAsync(int bookId, CancellationToken cancellationToken = default);
}
