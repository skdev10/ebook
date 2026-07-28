using EBookDashboard.Models;

namespace EBookDashboard.Interfaces;

/// <summary>CRUD + activate for per-book cover projects (Ebook / Paperback / Hardcover).</summary>
public interface IBookCoverDesignService
{
    /// <summary>Lists non-deleted cover projects for a book owned by the user.</summary>
    Task<IReadOnlyList<BookCoverDesign>> ListAsync(int userId, int bookId, CancellationToken ct = default);

    /// <summary>Ensures at least one cover project exists; returns the active (or newly created) row.</summary>
    Task<BookCoverDesign> EnsureDefaultAsync(int userId, int bookId, string? preferredType = null, CancellationToken ct = default);

    /// <summary>Creates a new cover project of the given type.</summary>
    Task<BookCoverDesign> CreateAsync(int userId, int bookId, string coverType, CancellationToken ct = default);

    /// <summary>Duplicates a cover project (assets + sizing metadata).</summary>
    Task<BookCoverDesign?> DuplicateAsync(int userId, int coverId, CancellationToken ct = default);

    /// <summary>Soft-deletes a cover project. Refuses if it is the last remaining cover.</summary>
    Task<(bool Ok, string? Message)> SoftDeleteAsync(int userId, int coverId, CancellationToken ct = default);

    /// <summary>Marks one cover as active and syncs assets into the Settings keys used by wrap/export.</summary>
    Task<BookCoverDesign?> ActivateAsync(int userId, int coverId, CancellationToken ct = default);

    /// <summary>Persists paths/status/dims onto the active cover after generate or upload.</summary>
    Task SyncActiveAssetsAsync(int userId, int bookId, CancellationToken ct = default);
}
