using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.Options;
using EBookDashboard.Services.BookApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EBookDashboard.Services;

/// <summary>Result of applying AI chapter edits for the Writer UI.</summary>
public sealed class ChapterEditResult
{
    public bool Success { get; init; }
    public int HttpStatus { get; init; } = 200;
    public string Message { get; init; } = "";
    public string? Detail { get; init; }
    public string Content { get; init; } = "";
    public int? ResponseId { get; init; }
}

/// <summary>
/// Applies natural-language chapter edits. Tries upstream <c>/api/edit</c>, then
/// rewrites via <c>/api/generate_chapter</c> using the chapter text we already have in MySQL.
/// </summary>
public sealed class ChapterEditService
{
    private readonly IBookApiClient _bookApiClient;
    private readonly IOptionsSnapshot<ExternalApiOptions> _externalApiOptions;
    private readonly IAPIRawResponseService _rawResponseService;
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ChapterEditService> _logger;

    public ChapterEditService(
        IBookApiClient bookApiClient,
        IOptionsSnapshot<ExternalApiOptions> externalApiOptions,
        IAPIRawResponseService rawResponseService,
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<ChapterEditService> logger)
    {
        _bookApiClient = bookApiClient;
        _externalApiOptions = externalApiOptions;
        _rawResponseService = rawResponseService;
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Edit one chapter and persist the new draft when content is returned.</summary>
    public async Task<ChapterEditResult> EditAsync(
        int userId,
        string bookId,
        string chapter,
        string changes,
        string? originalContent,
        string? title,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            return Fail(401, "Please sign in again, then retry.");
        if (string.IsNullOrWhiteSpace(changes))
            return Fail(400, "Enter editing instructions, then click Apply changes.");

        var bookIdTrim = (bookId ?? "").Trim();
        var chapterNum = int.TryParse(chapter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCh) && parsedCh > 0
            ? parsedCh
            : 1;
        var chapterStr = chapterNum.ToString(CultureInfo.InvariantCulture);
        var userIdStr = userId.ToString(CultureInfo.InvariantCulture);
        var changesTrim = changes.Trim();

        if (!int.TryParse(bookIdTrim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bookIdInt) || bookIdInt <= 0)
            return Fail(400, "Select a book first, then edit the chapter.");

        var owns = await _context.Books.AsNoTracking()
            .AnyAsync(b => b.BookId == bookIdInt && b.UserId == userId, cancellationToken);
        if (!owns)
            return Fail(404, "Book not found. Open your project from the Dashboard.");

        var original = await ResolveOriginalChapterAsync(userId, bookIdInt, chapterNum, originalContent, cancellationToken);
        var editUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.EditUrl, "/api/edit").Trim();
        if (!Uri.TryCreate(editUrl, UriKind.Absolute, out _))
            return Fail(500, "Server misconfiguration: ExternalApi edit URL is not a valid absolute URL.", editUrl);

        var editPayload = new JObject
        {
            ["user_id"] = userIdStr,
            ["book_id"] = bookIdTrim,
            ["chapter"] = chapterStr,
            ["changes"] = changesTrim,
            ["user_input"] = changesTrim
        };
        if (!string.IsNullOrWhiteSpace(title))
            editPayload["title"] = title.Trim();
        if (!string.IsNullOrWhiteSpace(original))
            editPayload["content"] = original;

        var editJson = editPayload.ToString(Formatting.None);
        string? lastDetail = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var editCall = await CallUpstreamAsync(editUrl, editJson, cancellationToken);
            lastDetail = editCall.Body;
            if (editCall.Ok)
            {
                var extracted = UpstreamResponseParser.ExtractContent(editCall.Body);
                if (!string.IsNullOrWhiteSpace(extracted))
                    return await SucceedAsync(userIdStr, bookIdTrim, chapterNum, title, changesTrim, editUrl, editCall.Body, extracted, cancellationToken);
            }

            _logger.LogWarning(
                "Chapter edit upstream failed attempt {Attempt} status={Status} book={BookId} chapter={Chapter}: {Detail}",
                attempt, editCall.Status, bookIdTrim, chapterStr, Truncate(editCall.Body, 240));

            if (attempt == 1 && editCall.Status >= 500)
                await Task.Delay(800, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(original))
        {
            return Fail(
                422,
                "This chapter has no saved text to edit. Generate the chapter first, then Apply changes.",
                lastDetail);
        }

        var generateUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.GenerateUrl, "/api/generate_chapter").Trim();
        if (!Uri.TryCreate(generateUrl, UriKind.Absolute, out _))
            return Fail(502, "Edit API failed and generate fallback is not configured.", lastDetail);

        var rewritePrompt = ChapterPromptComposer.BuildEditRewritePrompt(changesTrim, original, title);
        var generatePayload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["user_id"] = userIdStr,
            ["book_id"] = bookIdTrim,
            ["chapter"] = chapterStr,
            ["user_input"] = rewritePrompt
        };
        var generateJson = JsonConvert.SerializeObject(generatePayload);
        var genCall = await CallUpstreamAsync(generateUrl, generateJson, cancellationToken);
        if (!genCall.Ok)
        {
            _logger.LogWarning(
                "Chapter edit generate-fallback failed status={Status} book={BookId}: {Detail}",
                genCall.Status, bookIdTrim, Truncate(genCall.Body, 240));
            return Fail(
                genCall.Status >= 400 ? genCall.Status : 502,
                "Could not apply edits. Try again in a moment, or shorten the instructions.",
                genCall.Body);
        }

        var rewritten = UpstreamResponseParser.ExtractContent(genCall.Body);
        if (string.IsNullOrWhiteSpace(rewritten))
            return Fail(502, "AI returned empty edited text. Try again with clearer instructions.", genCall.Body);

        return await SucceedAsync(userIdStr, bookIdTrim, chapterNum, title, changesTrim, generateUrl, genCall.Body, rewritten, cancellationToken);
    }

    private async Task<string> ResolveOriginalChapterAsync(
        int userId,
        int bookId,
        int chapterNum,
        string? fromClient,
        CancellationToken cancellationToken)
    {
        var fromUi = StripToPlainText(original: fromClient);
        if (!string.IsNullOrWhiteSpace(fromUi))
            return fromUi;

        try
        {
            var raw = await _rawResponseService.GetRawResponseByChapterAsync(userId, bookId, chapterNum);
            if (raw != null)
            {
                var fromRow = !string.IsNullOrWhiteSpace(raw.Content)
                    ? raw.Content
                    : UpstreamResponseParser.ExtractContent(raw.ResponseData);
                var plain = StripToPlainText(fromRow);
                if (!string.IsNullOrWhiteSpace(plain))
                    return plain;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load APIRawResponse for edit fallback book {BookId} chapter {Chapter}.", bookId, chapterNum);
        }

        try
        {
            var chapter = await _context.Chapters.AsNoTracking()
                .FirstOrDefaultAsync(c => c.BookId == bookId && c.ChapterNumber == chapterNum, cancellationToken);
            var plain = StripToPlainText(chapter?.Content);
            if (!string.IsNullOrWhiteSpace(plain))
                return plain;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load Chapters row for edit fallback book {BookId} chapter {Chapter}.", bookId, chapterNum);
        }

        return string.Empty;
    }

    private async Task<ChapterEditResult> SucceedAsync(
        string userId,
        string bookId,
        int chapterNum,
        string? title,
        string changes,
        string endpoint,
        string responseData,
        string content,
        CancellationToken cancellationToken)
    {
        int? responseId = null;
        try
        {
            responseId = await _rawResponseService.SaveRawResponseAsync(
                new AIBookRequest
                {
                    UserId = userId,
                    BookId = bookId,
                    Title = title ?? "",
                    Chapter = chapterNum,
                    UserInput = changes
                },
                responseData,
                endpoint,
                "OK");
        }
        catch (Exception saveEx)
        {
            _logger.LogWarning(saveEx, "Edit: SaveRawResponseAsync failed; returning edited content anyway.");
        }

        if (int.TryParse(bookId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bookIdInt) && bookIdInt > 0)
        {
            try
            {
                var chapter = await _context.Chapters
                    .FirstOrDefaultAsync(c => c.BookId == bookIdInt && c.ChapterNumber == chapterNum, cancellationToken);
                if (chapter != null)
                {
                    chapter.Content = WebUtility.HtmlDecode(content);
                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        var t = title.Trim();
                        chapter.Title = t.Length <= 200 ? t : t[..200];
                    }
                    chapter.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception persistEx)
            {
                _logger.LogWarning(persistEx, "Edit: could not persist Chapters row for book {BookId}.", bookId);
            }
        }

        return new ChapterEditResult
        {
            Success = true,
            HttpStatus = 200,
            Content = content,
            ResponseId = responseId
        };
    }

    private async Task<(bool Ok, int Status, string Body)> CallUpstreamAsync(
        string url,
        string json,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, upstreamCts.Token);
        using var response = await _bookApiClient.SendAsync(req, BookApiCallTimeoutKind.LongRunning, linked.Token);
        var body = await response.Content.ReadAsStringAsync(linked.Token);
        return (response.IsSuccessStatusCode, (int)response.StatusCode, body ?? string.Empty);
    }

    private static ChapterEditResult Fail(int status, string message, string? detail = null) =>
        new()
        {
            Success = false,
            HttpStatus = status,
            Message = message,
            Detail = Truncate(detail, 400)
        };

    private static string StripToPlainText(string? original)
    {
        if (string.IsNullOrWhiteSpace(original))
            return string.Empty;
        var text = Regex.Replace(original, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..max] + "...";
    }
}
