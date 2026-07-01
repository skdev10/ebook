namespace EBookDashboard.Interfaces;

/// <summary>Builds and persists full print wrap (spine + back + front) from a saved front cover.</summary>
public interface IPrintWrapGenerationService
{
    /// <summary>
    /// When format is Paperback/Both and no wrap exists, calls upstream split API and saves wrap assets.
    /// Returns true when a wrap was generated or already present.
    /// </summary>
    Task<bool> TryGenerateFromSavedFrontAsync(
        int userId,
        int bookId,
        int? pageCountOverride = null,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default);
}
