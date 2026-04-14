using EBookDashboard.Models.DTO;

namespace EBookDashboard.Interfaces
{
    /// <summary>
    /// Tracks per-generation chapter rows, finalization, and PDF-ready views of finalized content.
    /// </summary>
    public interface IChapterIterationService
    {
        /// <summary>
        /// After a successful generate API call, records a new iteration (serialized transaction for iteration number).
        /// Idempotent per <paramref name="responseId"/> (skips if already recorded).
        /// </summary>
        Task RecordSuccessfulGenerationAsync(int responseId, CancellationToken cancellationToken = default);

        /// <summary>All iterations for one chapter slot — from <c>chapter_iterations</c>, or synthesized from <c>apirawresponse</c> if none yet.</summary>
        Task<IReadOnlyList<ChapterIterationListItemDto>> ListIterationsAsync(int userId, int bookId, int chapterNumber, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks the iteration matching <paramref name="responseId"/> as the sole finalized version for its series; clears sibling finalized flags on other iterations in the same series.
        /// Also upserts the corresponding row in <c>chapters</c> with that version’s title and body (official library chapter).
        /// </summary>
        Task<bool> FinalizeByResponseIdAsync(int userId, int bookId, int chapterNumber, int responseId, CancellationToken cancellationToken = default);

        /// <summary>Builds book + chapter list for PDF export from official <c>chapters</c> rows (Status ReadOnly/Final after Finalize). Falls back to finalized <c>chapter_iterations</c> if none.</summary>
        Task<BookDetailsResponseDto?> BuildPdfReadyFromFinalizedAsync(int userId, int bookId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Inserts <c>apirawresponse</c> + a new <c>chapter_iterations</c> row (next sequence) for manual saves, imports, or inline edits — never overwrites prior versions.
        /// </summary>
        Task<int> RecordUserContentVersionAsync(int userId, int bookId, int chapterNumber, string chapterTitle, string bodyContent, string? topic, string endpointTag, CancellationToken cancellationToken = default);
    }
}
