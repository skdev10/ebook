namespace EBookDashboard.Interfaces;

/// <summary>Builds and persists full print wrap (spine + back + front) from a saved front cover.</summary>
public interface IPrintWrapGenerationService
{
    /// <summary>
    /// Builds a full KDP wrap from the saved front cover (local ImageSharp compositor).
    /// Returns true when a wrap was generated or a matching wrap already exists.
    /// </summary>
    /// <param name="allowNonPrintFormat">
    /// When true, generate even if book format is still Ebook (explicit Cover Design / Publish request).
    /// </param>
    /// <param name="localOnly">
    /// When true, only run the local ImageSharp compositor (no spine AI APIs). Use for interactive UI.
    /// </param>
    Task<bool> TryGenerateFromSavedFrontAsync(
        int userId,
        int bookId,
        int? pageCountOverride = null,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default,
        bool allowNonPrintFormat = false,
        bool localOnly = false);
}
