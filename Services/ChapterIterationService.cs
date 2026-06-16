using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Newtonsoft.Json;

namespace EBookDashboard.Services
{
    public class ChapterIterationService : IChapterIterationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<ChapterIterationService> _logger;

        public ChapterIterationService(ApplicationDbContext db, ILogger<ChapterIterationService> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <inheritdoc />
        public async Task<int> RecordUserContentVersionAsync(int userId, int bookId, int chapterNumber, string chapterTitle, string bodyContent, string? topic, string endpointTag, CancellationToken cancellationToken = default)
        {
            if (userId <= 0 || bookId <= 0 || chapterNumber <= 0)
                return 0;

            var body = bodyContent ?? string.Empty;
            if (body.Length > 2_000_000)
                body = body.Substring(0, 2_000_000) + "\n...[truncated for iteration storage]";

            var title = string.IsNullOrWhiteSpace(chapterTitle) ? $"Chapter {chapterNumber}" : chapterTitle.Trim();
            if (title.Length > 500)
                title = title[..500];

            var reqJson = string.IsNullOrWhiteSpace(topic)
                ? "{}"
                : JsonConvert.SerializeObject(new { chapter_topic = topic.Trim() });

            var wrappedResponse = JsonConvert.SerializeObject(new { data = new { content = body } });

            var raw = new APIRawResponse
            {
                Endpoint = string.IsNullOrWhiteSpace(endpointTag) ? "user-content" : endpointTag.Trim(),
                Chapter = chapterNumber,
                Title = title,
                RequestData = reqJson,
                ResponseData = wrappedResponse,
                UserId = userId,
                BookId = bookId,
                StatusCode = "OK",
                Content = body,
                CreatedAt = DateTime.UtcNow
            };

            _db.APIRawResponse.Add(raw);
            await _db.SaveChangesAsync(cancellationToken);

            await RecordSuccessfulGenerationAsync(raw.ResponseId, cancellationToken);
            return raw.ResponseId;
        }

        /// <inheritdoc />
        public async Task RecordSuccessfulGenerationAsync(int responseId, CancellationToken cancellationToken = default)
        {
            if (responseId <= 0) return;

            if (await _db.Set<ChapterIteration>().AnyAsync(i => i.ResponseId == responseId, cancellationToken))
                return;

            var raw = await _db.APIRawResponse.AsNoTracking().FirstOrDefaultAsync(r => r.ResponseId == responseId, cancellationToken);
            if (raw == null || raw.UserId is null or <= 0)
                return;

            var userId = raw.UserId.Value;
            var bookId = raw.BookId ?? 0;
            if (bookId <= 0 && !string.IsNullOrWhiteSpace(raw.ParsedBookId) && int.TryParse(raw.ParsedBookId.Trim(), out var parsedBid) && parsedBid > 0)
                bookId = parsedBid;
            if (bookId <= 0)
                return;
            var chapterNumber = raw.Chapter <= 0 ? 1 : raw.Chapter;

            var content = !string.IsNullOrWhiteSpace(raw.Content)
                ? raw.Content!
                : APIRawResponseService.ExtractContentFromResponse(raw.ResponseData ?? "");
            if (string.IsNullOrEmpty(content))
                content = raw.ResponseData ?? "";
            if (content.Length > 2_000_000)
                content = content.Substring(0, 2_000_000) + "\n...[truncated for iteration storage]";

            // Serializable transaction: stable next IterationNumber under concurrent generates for the same chapter.
            IExecutionStrategy strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                try
                {
                    var seriesGuid = await _db.Set<ChapterIteration>()
                        .Where(i => i.UserId == userId && i.BookId == bookId && i.ChapterNumber == chapterNumber)
                        .Select(i => i.ChapterSeriesGuid)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (seriesGuid == Guid.Empty)
                        seriesGuid = Guid.NewGuid();

                    var maxIt = await _db.Set<ChapterIteration>()
                        .Where(i => i.ChapterSeriesGuid == seriesGuid)
                        .MaxAsync(i => (int?)i.IterationNumber, cancellationToken) ?? 0;

                    var utc = DateTime.UtcNow;
                    var row = new ChapterIteration
                    {
                        ChapterSeriesGuid = seriesGuid,
                        BookId = bookId,
                        UserId = userId,
                        ChapterNumber = chapterNumber,
                        IterationNumber = maxIt + 1,
                        ResponseId = responseId,
                        Title = raw.Title,
                        Content = content,
                        GenerationDate = utc.Date,
                        GenerationTime = utc.TimeOfDay,
                        IsFinalized = false,
                        IsLocked = false
                    };

                    _db.Set<ChapterIteration>().Add(row);
                    await _db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                }
                catch
                {
                    await tx.RollbackAsync(cancellationToken);
                    throw;
                }
            });
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ChapterIterationListItemDto>> ListIterationsAsync(int userId, int bookId, int chapterNumber, CancellationToken cancellationToken = default)
        {
            var stored = await _db.Set<ChapterIteration>()
                .AsNoTracking()
                .Where(i => i.UserId == userId && i.BookId == bookId && i.ChapterNumber == chapterNumber)
                .OrderBy(i => i.IterationNumber)
                .ToListAsync(cancellationToken);

            if (stored.Count > 0)
            {
                var rawById = await _db.APIRawResponse.AsNoTracking()
                    .Where(r => r.UserId == userId && r.BookId == bookId && r.Chapter == chapterNumber && r.ResponseId > 0)
                    .ToDictionaryAsync(r => r.ResponseId, cancellationToken);

                return stored.Select(i =>
                {
                    string? rawStatus = null;
                    if (i.ResponseId.HasValue && rawById.TryGetValue(i.ResponseId.Value, out var raw))
                        rawStatus = raw.StatusCode;
                    return MapStored(i, rawStatus);
                }).ToList();
            }

            // Fallback: no iteration rows yet — expose each apirawresponse as a logical "version" (chronological order).
            var raws = await _db.APIRawResponse
                .AsNoTracking()
                .Where(r => r.UserId == userId && r.BookId == bookId && r.Chapter == chapterNumber)
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.ResponseId)
                .ToListAsync(cancellationToken);

            var n = 0;
            return raws.Select(r => new ChapterIterationListItemDto
            {
                ChapterIterationId = 0,
                ChapterSeriesGuid = Guid.Empty,
                IterationNumber = ++n,
                ResponseId = r.ResponseId,
                Title = r.Title,
                IsFinalized = false,
                IsLocked = string.Equals(r.StatusCode, "ReadOnly", StringComparison.OrdinalIgnoreCase),
                GenerationDate = r.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                GenerationTime = r.CreatedAt.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture),
                FinalizedDate = null,
                FinalizedTime = null,
                StatusCode = r.StatusCode
            }).ToList();
        }

        /// <inheritdoc />
        public Task<bool> FinalizeByResponseIdAsync(int userId, int bookId, int chapterNumber, int responseId, CancellationToken cancellationToken = default)
            => PromoteIterationCoreAsync(userId, bookId, chapterNumber, responseId, upsertLibraryChapter: true, cancellationToken);

        /// <inheritdoc />
        public Task<bool> PromoteAsCurrentVersionAsync(int userId, int bookId, int chapterNumber, int responseId, CancellationToken cancellationToken = default)
            => PromoteIterationCoreAsync(userId, bookId, chapterNumber, responseId, upsertLibraryChapter: false, cancellationToken);

        private async Task<bool> PromoteIterationCoreAsync(
            int userId,
            int bookId,
            int chapterNumber,
            int responseId,
            bool upsertLibraryChapter,
            CancellationToken cancellationToken)
        {
            if (responseId <= 0 || userId <= 0 || bookId <= 0) return false;

            var strategy = _db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                try
                {
                    var iter = await EnsureIterationRowAsync(userId, bookId, chapterNumber, responseId, cancellationToken);
                    if (iter == null)
                    {
                        await tx.RollbackAsync(cancellationToken);
                        return false;
                    }

                    var siblings = await _db.Set<ChapterIteration>()
                        .Where(i => i.ChapterSeriesGuid == iter.ChapterSeriesGuid && i.ChapterIterationId != iter.ChapterIterationId)
                        .ToListAsync(cancellationToken);
                    foreach (var s in siblings)
                    {
                        s.IsFinalized = false;
                        s.IsLocked = false;
                        s.FinalizedDate = null;
                        s.FinalizedTime = null;
                    }

                    var now = DateTime.UtcNow;
                    iter.IsFinalized = true;
                    iter.IsLocked = true;
                    iter.FinalizedDate = now.Date;
                    iter.FinalizedTime = now.TimeOfDay;

                    if (upsertLibraryChapter)
                        UpsertMainChapterFromIteration(userId, iter);

                    await _db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Promote iteration failed for response {ResponseId}", responseId);
                    await tx.RollbackAsync(cancellationToken);
                    return false;
                }
            });
        }

        private async Task<ChapterIteration?> EnsureIterationRowAsync(
            int userId,
            int bookId,
            int chapterNumber,
            int responseId,
            CancellationToken cancellationToken)
        {
            var iter = await _db.Set<ChapterIteration>()
                .FirstOrDefaultAsync(i => i.ResponseId == responseId && i.UserId == userId && i.BookId == bookId, cancellationToken);

            if (iter != null)
                return iter;

            var raw = await _db.APIRawResponse.FirstOrDefaultAsync(
                r => r.ResponseId == responseId && r.UserId == userId && r.BookId == bookId && r.Chapter == chapterNumber,
                cancellationToken);
            if (raw == null)
                return null;

            var seriesGuid = await _db.Set<ChapterIteration>()
                .Where(i => i.UserId == userId && i.BookId == bookId && i.ChapterNumber == chapterNumber)
                .Select(i => i.ChapterSeriesGuid)
                .FirstOrDefaultAsync(cancellationToken);
            if (seriesGuid == Guid.Empty)
                seriesGuid = Guid.NewGuid();

            var maxIt = await _db.Set<ChapterIteration>()
                .Where(i => i.ChapterSeriesGuid == seriesGuid)
                .MaxAsync(i => (int?)i.IterationNumber, cancellationToken) ?? 0;

            var content = !string.IsNullOrWhiteSpace(raw.Content)
                ? raw.Content!
                : APIRawResponseService.ExtractContentFromResponse(raw.ResponseData ?? "");
            if (string.IsNullOrEmpty(content))
                content = raw.ResponseData ?? "";

            var utc = raw.CreatedAt;
            iter = new ChapterIteration
            {
                ChapterSeriesGuid = seriesGuid,
                BookId = bookId,
                UserId = userId,
                ChapterNumber = chapterNumber,
                IterationNumber = maxIt + 1,
                ResponseId = responseId,
                Title = raw.Title,
                Content = content,
                GenerationDate = utc.Date,
                GenerationTime = utc.TimeOfDay,
                IsFinalized = false,
                IsLocked = false
            };
            _db.Set<ChapterIteration>().Add(iter);
            await _db.SaveChangesAsync(cancellationToken);
            return iter;
        }

        /// <inheritdoc />
        public async Task<BookDetailsResponseDto?> BuildPdfReadyFromFinalizedAsync(int userId, int bookId, CancellationToken cancellationToken = default)
        {
            var book = await _db.Books.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
            if (book == null) return null;

            var authorName = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => u.FullName != null && u.FullName != "" ? u.FullName : u.UserEmail)
                .FirstOrDefaultAsync(cancellationToken);

            // Primary: official manuscript rows in `chapters` (filled when user taps Finalize).
            var officialRows = await _db.Chapters
                .AsNoTracking()
                .Where(c => c.BookId == bookId &&
                            (string.Equals(c.Status, "ReadOnly", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(c.Status, "Final", StringComparison.OrdinalIgnoreCase)))
                .Where(c => c.Content != null && c.Content.Trim().Length > 0)
                .OrderBy(c => c.ChapterNumber)
                .ToListAsync(cancellationToken);

            IReadOnlyList<ChapterDto> chapters;
            if (officialRows.Count > 0)
            {
                chapters = officialRows.Select(c => new ChapterDto
                {
                    ResponseId = 0,
                    ChapterNumber = c.ChapterNumber,
                    Title = string.IsNullOrWhiteSpace(c.Title) ? $"Chapter {c.ChapterNumber}" : c.Title,
                    Content = c.Content ?? "",
                    StatusCode = c.Status,
                    CreatedAt = c.UpdatedAt != default ? c.UpdatedAt : c.CreatedAt,
                    ExportMetaHtml =
                        $"<p class=\"export-meta\"><strong>Library chapter (finalized)</strong><br/><strong>Updated (UTC):</strong> {(c.UpdatedAt != default ? c.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "—")}</p>"
                }).ToList();
            }
            else
            {
                // Fallback: legacy / edge case — rows marked finalized only in chapter_iterations.
                var finalized = await _db.Set<ChapterIteration>()
                    .AsNoTracking()
                    .Where(i => i.UserId == userId && i.BookId == bookId && i.IsFinalized)
                    .OrderBy(i => i.ChapterNumber)
                    .ThenBy(i => i.IterationNumber)
                    .ToListAsync(cancellationToken);

                if (finalized.Count == 0)
                    return null;

                chapters = finalized.Select(i => new ChapterDto
                {
                    ResponseId = i.ResponseId ?? 0,
                    ChapterNumber = i.ChapterNumber,
                    Title = i.Title ?? $"Chapter {i.ChapterNumber}",
                    Content = i.Content,
                    StatusCode = "Finalized",
                    CreatedAt = CombineUtc(i.GenerationDate, i.GenerationTime),
                    ExportMetaHtml = BuildMetaHtml(i)
                }).ToList();
            }

            return new BookDetailsResponseDto
            {
                Success = true,
                BookId = book.BookId,
                BookTitle = book.Title,
                Subtitle = book.Subtitle,
                Description = book.Description,
                Genre = book.Genre,
                AuthorName = authorName?.Trim(),
                CoverImagePath = book.CoverImagePath,
                TotalChapters = chapters.Count,
                Chapters = chapters.ToList()
            };
        }

        private static string BuildMetaHtml(ChapterIteration i)
        {
            var genD = i.GenerationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var genT = i.GenerationTime.ToString(@"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture);
            var fin = i.FinalizedDate.HasValue && i.FinalizedTime.HasValue
                ? $"{i.FinalizedDate.Value:yyyy-MM-dd} {i.FinalizedTime.Value.ToString(@"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture)} UTC"
                : "—";
            return $"<p class=\"export-meta\"><strong>Generated (UTC):</strong> {genD} {genT}<br/><strong>Finalized (UTC):</strong> {fin}</p>";
        }

        private static DateTime CombineUtc(DateTime date, TimeSpan time)
        {
            return date.Date.Add(time);
        }

        private static ChapterIterationListItemDto MapStored(ChapterIteration i, string? rawStatus)
        {
            return new ChapterIterationListItemDto
            {
                ChapterIterationId = i.ChapterIterationId,
                ChapterSeriesGuid = i.ChapterSeriesGuid,
                IterationNumber = i.IterationNumber,
                ResponseId = i.ResponseId ?? 0,
                Title = i.Title,
                IsFinalized = i.IsFinalized,
                IsLocked = i.IsLocked,
                GenerationDate = i.GenerationDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                GenerationTime = i.GenerationTime.ToString(@"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture),
                FinalizedDate = i.FinalizedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FinalizedTime = i.FinalizedTime?.ToString(@"hh\:mm\:ss\.ffffff", CultureInfo.InvariantCulture),
                StatusCode = rawStatus
            };
        }

        /// <summary>
        /// Official library chapter row: only the finalized iteration is copied into <c>chapters</c>.
        /// </summary>
        private void UpsertMainChapterFromIteration(int userId, ChapterIteration iter)
        {
            var title = string.IsNullOrWhiteSpace(iter.Title)
                ? $"Chapter {iter.ChapterNumber}"
                : iter.Title!.Trim();
            if (title.Length > 200)
                title = title[..200];

            var content = iter.Content ?? string.Empty;
            var wc = ApproximateWordCount(content);
            var now = DateTime.UtcNow;

            var row = _db.Chapters.FirstOrDefault(c => c.BookId == iter.BookId && c.ChapterNumber == iter.ChapterNumber);
            if (row != null)
            {
                row.Title = title;
                row.Content = content;
                row.Status = "ReadOnly";
                row.WordCount = wc;
                row.UpdatedAt = now;
                row.UpdatedByUserId = userId;
                return;
            }

            _db.Chapters.Add(new Chapters
            {
                BookId = iter.BookId,
                ChapterNumber = iter.ChapterNumber,
                SrNo = iter.ChapterNumber,
                OrderIndex = iter.ChapterNumber,
                Title = title,
                SubTitle = string.Empty,
                Content = content,
                LanguageId = 1,
                WordCount = wc,
                Status = "ReadOnly",
                CreatedAt = now,
                UpdatedAt = now,
                UpdatedByUserId = userId,
                IsPublished = false
            });
        }

        private static int ApproximateWordCount(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return 0;
            var plain = Regex.Replace(html, "<.*?>", string.Empty, RegexOptions.Singleline);
            return Regex.Matches(plain, @"\b\w+\b").Count;
        }
    }
}
