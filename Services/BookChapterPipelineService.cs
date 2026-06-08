using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.Options;
using EBookDashboard.Services.BookApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace EBookDashboard.Services;

public class BookChapterPipelineService : IBookChapterPipelineService
{
    private const int MaxContinuityChars = 14_000;

    /// <summary>Serializes concurrent generation for the same (book, chapter) slot only — different chapters on one book can run in parallel.</summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ChapterSlotLocks = new();

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IAPIRawResponseService _rawResponseService;
    private readonly IBookService _bookService;
    private readonly IChapterIterationService _chapterIterationService;
    private readonly IBookApiClient _bookApiClient;
    private readonly IOptionsSnapshot<ExternalApiOptions> _externalApiOptions;
    private readonly IOptions<ChapterGenerationOptions> _genOptions;
    private readonly ILogger<BookChapterPipelineService> _logger;

    public BookChapterPipelineService(
        ApplicationDbContext db,
        IConfiguration configuration,
        IAPIRawResponseService rawResponseService,
        IBookService bookService,
        IChapterIterationService chapterIterationService,
        IBookApiClient bookApiClient,
        IOptionsSnapshot<ExternalApiOptions> externalApiOptions,
        IOptions<ChapterGenerationOptions> genOptions,
        ILogger<BookChapterPipelineService> logger)
    {
        _db = db;
        _configuration = configuration;
        _rawResponseService = rawResponseService;
        _bookService = bookService;
        _chapterIterationService = chapterIterationService;
        _bookApiClient = bookApiClient;
        _externalApiOptions = externalApiOptions;
        _genOptions = genOptions;
        _logger = logger;
    }

    private static string LockKey(int bookId, int chapterNumber) => $"{bookId}:{chapterNumber}";

    public async Task<IReadOnlyList<ChapterListItemDto>> ListChaptersAsync(int userId, int bookId, CancellationToken cancellationToken = default)
    {
        await EnsureBookAsync(userId, bookId, cancellationToken);
        var rows = await _db.Chapters.AsNoTracking()
            .Where(c => c.BookId == bookId)
            .OrderBy(c => c.ChapterNumber)
            .ToListAsync(cancellationToken);

        return rows.Select(MapChapterRow).ToList();
    }

    public async Task<ChapterOperationResult> UpsertChapterPlanAsync(int userId, int bookId, ChapterPlanCreateRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureBookAsync(userId, bookId, cancellationToken);
        var chapterNumber = request.ChapterNumber;
        if (chapterNumber <= 0)
            chapterNumber = await _bookService.GetNextChapterNumberAsync(userId, bookId);

        if (chapterNumber < 1 || chapterNumber > 999)
            return new ChapterOperationResult { Success = false, Message = "Invalid chapter number." };

        var title = string.IsNullOrWhiteSpace(request.Title) ? $"Chapter {chapterNumber}" : request.Title.Trim();
        if (title.Length > 200)
            return new ChapterOperationResult { Success = false, Message = "Title too long (max 200)." };

        var subtitle = EncodeSubtitleMeta(request.Tone, request.TargetWordCount, request.Description, request.Prompt);

        var existing = await _db.Chapters.FirstOrDefaultAsync(
            c => c.BookId == bookId && c.ChapterNumber == chapterNumber, cancellationToken);

        if (existing != null && IsChapterLocked(existing))
            return new ChapterOperationResult { Success = false, Message = "Chapter already has substantial or finalized content; cannot replace plan here." };

        if (existing != null)
        {
            existing.Title = title;
            existing.SubTitle = subtitle;
            existing.OrderIndex = chapterNumber;
            existing.SrNo = chapterNumber;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedByUserId = userId;
            if (string.IsNullOrWhiteSpace(existing.Content))
                existing.Status = string.IsNullOrWhiteSpace(existing.Status) ? "Outline" : existing.Status;
        }
        else
        {
            _db.Chapters.Add(new Chapters
            {
                BookId = bookId,
                ChapterNumber = chapterNumber,
                SrNo = chapterNumber,
                OrderIndex = chapterNumber,
                Title = title,
                SubTitle = subtitle,
                Content = "",
                LanguageId = 1,
                WordCount = 0,
                Status = "Outline",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                UpdatedByUserId = userId
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new ChapterOperationResult { Success = true, ChapterNumber = chapterNumber };
    }

    public async Task<ChapterOperationResult> UpdateChapterPlanAsync(int userId, int bookId, int chapterNumber, ChapterPlanUpdateRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureBookAsync(userId, bookId, cancellationToken);
        var existing = await _db.Chapters.FirstOrDefaultAsync(
            c => c.BookId == bookId && c.ChapterNumber == chapterNumber, cancellationToken);
        if (existing == null)
            return new ChapterOperationResult { Success = false, Message = "Chapter not found." };

        if (IsChapterLocked(existing))
            return new ChapterOperationResult { Success = false, Message = "Chapter is finalized or has substantial content; plan cannot be changed via this endpoint." };

        if (request.Title != null) existing.Title = request.Title.Trim().Length > 200 ? request.Title[..200] : request.Title.Trim();
        var (tone, words, desc, prompt) = DecodeSubtitleMeta(existing.SubTitle);
        if (request.Tone != null) tone = request.Tone;
        if (request.TargetWordCount != null) words = request.TargetWordCount;
        if (request.Description != null) desc = request.Description;
        if (request.Prompt != null) prompt = request.Prompt;
        existing.SubTitle = EncodeSubtitleMeta(tone, words, desc, prompt);
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedByUserId = userId;
        await _db.SaveChangesAsync(cancellationToken);
        return new ChapterOperationResult { Success = true, ChapterNumber = chapterNumber };
    }

    public async Task<ChapterOperationResult> DeleteChapterPlanAsync(int userId, int bookId, int chapterNumber, CancellationToken cancellationToken = default)
    {
        await EnsureBookAsync(userId, bookId, cancellationToken);
        var existing = await _db.Chapters.FirstOrDefaultAsync(
            c => c.BookId == bookId && c.ChapterNumber == chapterNumber, cancellationToken);
        if (existing == null)
            return new ChapterOperationResult { Success = false, Message = "Chapter not found." };

        if (IsChapterLocked(existing))
            return new ChapterOperationResult { Success = false, Message = "Cannot delete a finalized or substantial chapter." };

        _db.Chapters.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return new ChapterOperationResult { Success = true, ChapterNumber = chapterNumber };
    }

    public async Task<ChapterGenerateResultDto> GenerateChapterAsync(int userId, int bookId, int chapterNumber, ChapterGenerateApiRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureBookAsync(userId, bookId, cancellationToken);
        if (chapterNumber < 1)
            return new ChapterGenerateResultDto { Success = false, Message = "Invalid chapter number.", ChapterNumber = chapterNumber };

        var book = await _db.Books.AsNoTracking().FirstAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        var chapter = await _db.Chapters.FirstOrDefaultAsync(
            c => c.BookId == bookId && c.ChapterNumber == chapterNumber, cancellationToken);

        var (_, _, _, storedPrompt) = chapter != null ? DecodeSubtitleMeta(chapter.SubTitle) : (null, null, null, null);
        var userPrompt = !string.IsNullOrWhiteSpace(request.OverridePrompt)
            ? request.OverridePrompt!.Trim()
            : storedPrompt;
        if (string.IsNullOrWhiteSpace(userPrompt))
            userPrompt = chapter?.Title != null
                ? $"Write chapter {chapterNumber}: {chapter.Title}. Maintain continuity with prior chapters."
                : $"Write chapter {chapterNumber} for the book \"{book.Title}\". Maintain continuity with prior chapters.";

        var continuity = await BuildContinuityPrefixAsync(bookId, chapterNumber, cancellationToken);
        var (_, _, descHint, _) = chapter != null ? DecodeSubtitleMeta(chapter.SubTitle) : (null, null, null, null);
        var toneLine = GetToneLineFromChapter(chapter);
        var lengthLine = GetLengthLineFromChapter(chapter);
        var descLine = string.IsNullOrWhiteSpace(descHint) ? "" : $"Author notes: {descHint}\n\n";
        var fullUserInput = continuity + toneLine + lengthLine + descLine + userPrompt;

        var aiRequest = new AIBookRequest
        {
            UserId = userId.ToString(),
            BookId = bookId.ToString(),
            Title = chapter?.Title ?? $"Chapter {chapterNumber}",
            Chapter = chapterNumber,
            UserInput = fullUserInput,
            PreviewOnly = request.PreviewOnly
        };

        var apiPayload = GenerateChapterPayloadBuilder.CloneForExternalGenerateApi(aiRequest);
        var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.GenerateUrl, "/api/generate_chapter");
        var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogError("External API key not configured (ExternalApi:ApiKey or OpenAI:ApiKey); chapter generation cannot run.");
            return new ChapterGenerateResultDto
            {
                Success = false,
                Message = ExternalApiKeyResolver.MissingKeyUserMessage,
                ChapterNumber = chapterNumber,
                LastHttpStatus = 0
            };
        }

        var opts = _genOptions.Value;
        var maxRetries = Math.Clamp(opts.MaxRetries, 0, 20);
        var serializeSlot = opts.SerializeSameChapterOnly;
        SemaphoreSlim? slot = null;
        if (serializeSlot)
        {
            slot = ChapterSlotLocks.GetOrAdd(LockKey(bookId, chapterNumber), _ => new SemaphoreSlim(1, 1));
            await slot.WaitAsync(cancellationToken);
        }

        try
        {
            return await CallExternalGenerateWithRetriesAsync(
                aiRequest, apiUrl, apiPayload, bookId, chapterNumber, maxRetries, cancellationToken);
        }
        finally
        {
            if (slot != null)
                slot.Release();
        }
    }

    private async Task<ChapterGenerateResultDto> CallExternalGenerateWithRetriesAsync(
        AIBookRequest aiRequest,
        string apiUrl,
        object apiPayload,
        int bookId,
        int chapterNumber,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var client = _bookApiClient;
        var json = JsonConvert.SerializeObject(apiPayload);
        var totalAttempts = maxRetries + 1;
        string? lastBody = "";
        var lastStatus = 0;

        for (var attempt = 0; attempt < totalAttempts; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, apiUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };

                using var response = await client.SendAsync(req, BookApiCallTimeoutKind.LongRunning, cancellationToken);
                lastBody = await response.Content.ReadAsStringAsync(cancellationToken);
                lastStatus = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var rawId = await _rawResponseService.SaveRawResponseAsync(
                        aiRequest,
                        lastBody,
                        apiUrl,
                        response.StatusCode.ToString());
                    try
                    {
                        await _chapterIterationService.RecordSuccessfulGenerationAsync(rawId, cancellationToken);
                    }
                    catch (Exception itEx)
                    {
                        _logger.LogWarning(itEx, "Chapter iteration not recorded for raw {RawId}", rawId);
                    }

                    return new ChapterGenerateResultDto
                    {
                        Success = true,
                        ChapterNumber = chapterNumber,
                        RawResponseId = rawId,
                        RawJson = lastBody,
                        AttemptsUsed = attempt + 1,
                        LastHttpStatus = lastStatus
                    };
                }

                var canRetry = attempt < totalAttempts - 1 && IsTransientHttpStatus(lastStatus);
                if (canRetry)
                {
                    _logger.LogWarning(
                        "Chapter gen transient failure book {BookId} ch {Chapter}: HTTP {Status}, attempt {Attempt}/{Total}",
                        bookId, chapterNumber, lastStatus, attempt + 1, totalAttempts);
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                var failRawId = await _rawResponseService.SaveRawResponseAsync(
                    aiRequest,
                    lastBody,
                    apiUrl,
                    response.StatusCode.ToString());
                return new ChapterGenerateResultDto
                {
                    Success = false,
                    Message = $"Upstream API error: {(HttpStatusCode)lastStatus}",
                    ChapterNumber = chapterNumber,
                    RawResponseId = failRawId,
                    RawJson = lastBody,
                    AttemptsUsed = attempt + 1,
                    LastHttpStatus = lastStatus
                };
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or IOException)
            {
                var isTimeout = ex is TaskCanceledException tc && !cancellationToken.IsCancellationRequested;
                var msg = isTimeout ? "Generation timed out (HTTP)." : ex.Message;
                _logger.LogWarning(ex,
                    "Chapter gen transport error book {BookId} ch {Chapter}, attempt {Attempt}/{Total}",
                    bookId, chapterNumber, attempt + 1, totalAttempts);

                if (attempt < totalAttempts - 1)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken);
                    continue;
                }

                var rawId = await _rawResponseService.SaveRawResponseAsync(
                    aiRequest,
                    lastBody ?? "",
                    apiUrl,
                    isTimeout ? "504" : "0",
                    msg);

                return new ChapterGenerateResultDto
                {
                    Success = false,
                    Message = msg,
                    ChapterNumber = chapterNumber,
                    RawResponseId = rawId,
                    RawJson = lastBody,
                    AttemptsUsed = attempt + 1,
                    LastHttpStatus = lastStatus
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chapter generation failed book {BookId} ch {Chapter}", bookId, chapterNumber);
                var rawId = await _rawResponseService.SaveRawResponseAsync(
                    aiRequest,
                    lastBody ?? "",
                    apiUrl,
                    "500",
                    ex.Message);
                return new ChapterGenerateResultDto
                {
                    Success = false,
                    Message = ex.Message,
                    ChapterNumber = chapterNumber,
                    RawResponseId = rawId,
                    RawJson = lastBody,
                    AttemptsUsed = attempt + 1,
                    LastHttpStatus = lastStatus
                };
            }
        }

        return new ChapterGenerateResultDto
        {
            Success = false,
            Message = "Generation failed after retries.",
            ChapterNumber = chapterNumber,
            AttemptsUsed = totalAttempts,
            LastHttpStatus = lastStatus,
            RawJson = lastBody
        };
    }

    private static bool IsTransientHttpStatus(int code) =>
        code == (int)HttpStatusCode.RequestTimeout
        || code == 429
        || code == (int)HttpStatusCode.InternalServerError
        || code == (int)HttpStatusCode.BadGateway
        || code == (int)HttpStatusCode.ServiceUnavailable
        || code == (int)HttpStatusCode.GatewayTimeout;

    private async Task DelayBeforeRetryAsync(int attemptIndex, CancellationToken cancellationToken)
    {
        var opts = _genOptions.Value;
        var baseSec = Math.Max(1, opts.RetryBaseDelaySeconds);
        var maxSec = Math.Max(baseSec, opts.RetryMaxDelaySeconds);
        var exp = Math.Min(maxSec, baseSec * Math.Pow(2, attemptIndex));
        var jitterMs = Random.Shared.Next(0, 500);
        await Task.Delay(TimeSpan.FromSeconds(exp) + TimeSpan.FromMilliseconds(jitterMs), cancellationToken);
    }

    public async Task<IReadOnlyList<ChapterGenerateResultDto>> GenerateChaptersBatchAsync(int userId, int bookId, BatchChapterGenerateRequest request, CancellationToken cancellationToken = default)
    {
        var distinct = request.ChapterNumbers.Distinct().OrderBy(n => n).ToList();
        var results = new List<ChapterGenerateResultDto>();
        if (request.Parallel)
        {
            var tasks = distinct.Select(n =>
                GenerateChapterAsync(userId, bookId, n, new ChapterGenerateApiRequest { PreviewOnly = request.PreviewOnly }, cancellationToken));
            results.AddRange(await Task.WhenAll(tasks));
            return results;
        }

        foreach (var n in distinct)
        {
            var one = await GenerateChapterAsync(userId, bookId, n, new ChapterGenerateApiRequest { PreviewOnly = request.PreviewOnly }, cancellationToken);
            results.Add(one);
            if (!one.Success)
                _logger.LogWarning("Batch generation stopped after failure at chapter {N}", n);
        }

        return results;
    }

    private async Task EnsureBookAsync(int userId, int bookId, CancellationToken cancellationToken)
    {
        var ok = await _db.Books.AsNoTracking().AnyAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (!ok)
            throw new UnauthorizedAccessException("Book not found or access denied.");
    }

    private static bool IsChapterLocked(Chapters c)
    {
        if (string.Equals(c.Status, "ReadOnly", StringComparison.OrdinalIgnoreCase))
            return true;
        return c.Content != null && c.Content.Trim().Length > 500;
    }

    private static ChapterListItemDto MapChapterRow(Chapters c)
    {
        var (tone, words, desc, prompt) = DecodeSubtitleMeta(c.SubTitle);
        var hasContent = !string.IsNullOrWhiteSpace(c.Content) && c.Content.Trim().Length > 20;
        return new ChapterListItemDto
        {
            ChapterNumber = c.ChapterNumber,
            Title = c.Title,
            Status = string.IsNullOrWhiteSpace(c.Status) ? (hasContent ? "Draft" : "Outline") : c.Status,
            HasContent = hasContent,
            WordCount = c.WordCount,
            Description = desc,
            Tone = tone,
            TargetWordCount = words,
            BriefPreview = hasContent ? TrimPreview(c.Content!, 240) : TrimPreview(prompt ?? "", 240)
        };
    }

    private static string TrimPreview(string htmlOrText, int max)
    {
        var plain = Regex.Replace(htmlOrText, "<[^>]+>", " ");
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        if (plain.Length <= max) return plain;
        return plain[..max] + "…";
    }

    /// <summary>Subtitle stores: optional meta line, blank line, then prompt/outline text.</summary>
    private static string EncodeSubtitleMeta(string? tone, int? targetWords, string? description, string? prompt)
    {
        var metaParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(tone)) metaParts.Add($"tone={tone.Trim()}");
        if (targetWords is > 0) metaParts.Add($"words={targetWords}");
        if (!string.IsNullOrWhiteSpace(description)) metaParts.Add($"desc={description.Trim()}");
        var metaLine = metaParts.Count == 0 ? "" : "[chmeta:" + string.Join("|", metaParts) + "]";
        var body = (prompt ?? "").Trim();
        if (string.IsNullOrEmpty(metaLine)) return body;
        if (string.IsNullOrEmpty(body)) return metaLine;
        return metaLine + "\n\n" + body;
    }

    private static (string? tone, int? words, string? desc, string? prompt) DecodeSubtitleMeta(string? subTitle)
    {
        if (string.IsNullOrWhiteSpace(subTitle))
            return (null, null, null, null);
        var lines = subTitle.Replace("\r\n", "\n").Split('\n', 2);
        var first = lines[0].Trim();
        string? rest = lines.Length > 1 ? lines[1].Trim() : null;
        if (first.StartsWith("[chmeta:", StringComparison.Ordinal) && first.EndsWith(']'))
        {
            var inner = first[8..^1];
            string? tone = null;
            int? words = null;
            string? desc = null;
            foreach (var part in inner.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                var k = kv[0].Trim().ToLowerInvariant();
                var v = kv[1].Trim();
                if (k == "tone") tone = v;
                else if (k == "words" && int.TryParse(v, out var w)) words = w;
                else if (k == "desc") desc = v;
            }

            return (tone, words, desc, rest);
        }

        return (null, null, null, subTitle.Trim());
    }

    private static string? GetToneLineFromChapter(Chapters? c)
    {
        if (c == null) return null;
        var (tone, _, _, _) = DecodeSubtitleMeta(c.SubTitle);
        return string.IsNullOrWhiteSpace(tone) ? null : $"Narrative tone: {tone}\n\n";
    }

    private static string? GetLengthLineFromChapter(Chapters? c)
    {
        if (c == null) return null;
        var (_, words, _, _) = DecodeSubtitleMeta(c.SubTitle);
        return words is null or <= 0 ? null : $"Target length (approximate words): {words}\n\n";
    }

    private async Task<string> BuildContinuityPrefixAsync(int bookId, int beforeChapter, CancellationToken cancellationToken)
    {
        var prior = await _db.Chapters.AsNoTracking()
            .Where(c => c.BookId == bookId && c.ChapterNumber < beforeChapter)
            .OrderBy(c => c.ChapterNumber)
            .Select(c => new { c.ChapterNumber, c.Title, c.Content })
            .ToListAsync(cancellationToken);

        var sb = new StringBuilder();
        foreach (var p in prior)
        {
            if (string.IsNullOrWhiteSpace(p.Content)) continue;
            var plain = Regex.Replace(p.Content, "<[^>]+>", " ");
            plain = Regex.Replace(plain, @"\s+", " ").Trim();
            if (plain.Length < 40) continue;
            if (plain.Length > 6000) plain = plain[..6000] + "…";
            var block = $"[Prior chapter {p.ChapterNumber} — {p.Title} (continuity only; do not repeat in output):]\n{plain}\n\n";
            if (sb.Length + block.Length > MaxContinuityChars) break;
            sb.Append(block);
        }

        if (sb.Length == 0) return "";
        return sb + "\n---\n\n[Write ONLY the new chapter from the brief below — pure narrative prose:]\n\n";
    }
}
