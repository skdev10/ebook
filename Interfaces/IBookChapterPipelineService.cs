using EBookDashboard.Models.DTO;

namespace EBookDashboard.Interfaces;

/// <summary>Chapter plans, continuity context, and API-driven generation for a book project.</summary>
public interface IBookChapterPipelineService
{
    Task<IReadOnlyList<ChapterListItemDto>> ListChaptersAsync(int userId, int bookId, CancellationToken cancellationToken = default);

    Task<ChapterOperationResult> UpsertChapterPlanAsync(int userId, int bookId, ChapterPlanCreateRequest request, CancellationToken cancellationToken = default);

    Task<ChapterOperationResult> UpdateChapterPlanAsync(int userId, int bookId, int chapterNumber, ChapterPlanUpdateRequest request, CancellationToken cancellationToken = default);

    Task<ChapterOperationResult> DeleteChapterPlanAsync(int userId, int bookId, int chapterNumber, CancellationToken cancellationToken = default);

    Task<ChapterGenerateResultDto> GenerateChapterAsync(int userId, int bookId, int chapterNumber, ChapterGenerateApiRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChapterGenerateResultDto>> GenerateChaptersBatchAsync(int userId, int bookId, BatchChapterGenerateRequest request, CancellationToken cancellationToken = default);
}
