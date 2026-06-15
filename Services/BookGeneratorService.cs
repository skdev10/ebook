using System.Globalization;
using System.Text;
using System.Text.Json;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.Options;
using EBookDashboard.Services.BookApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EBookDashboard.Services;

/// <summary>Whole-book HTML generation via upstream FastAPI (<c>/api/generate_chapter</c> with full prompt).</summary>
public sealed class BookGeneratorService : IBookGeneratorService
{
    private readonly ApplicationDbContext _context;
    private readonly IBookApiClient _bookApiClient;
    private readonly IOptionsSnapshot<ExternalApiOptions> _externalApiOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BookGeneratorService> _logger;

    public BookGeneratorService(
        ApplicationDbContext context,
        IBookApiClient bookApiClient,
        IOptionsSnapshot<ExternalApiOptions> externalApiOptions,
        IConfiguration configuration,
        ILogger<BookGeneratorService> logger)
    {
        _context = context;
        _bookApiClient = bookApiClient;
        _externalApiOptions = externalApiOptions;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BookGenerationResult> GenerateAndSaveHtmlAsync(
        int userId,
        int bookId,
        BookRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
            return Fail("Please sign in.");
        if (bookId <= 0)
            return Fail("BookId is required.");
        if (string.IsNullOrWhiteSpace(request.BookTitle))
            return Fail("Book title is required.");

        var book = await _context.Books.FirstOrDefaultAsync(
            b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (book == null)
            return Fail("Book not found.");

        var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
        if (string.IsNullOrEmpty(apiKey))
            return Fail(ExternalApiKeyResolver.MissingKeyUserMessage);

        var opts = _externalApiOptions.Value;
        var apiUrl = _bookApiClient.ResolveUrl(
            string.IsNullOrWhiteSpace(opts.GenerateBookUrl) ? opts.GenerateUrl : opts.GenerateBookUrl,
            "/api/generate_chapter");

        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out _))
            return Fail("Server misconfiguration: generate URL is invalid.");

        var prompt = PromptTemplates.BuildBookPrompt(request);
        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["user_id"] = userId.ToString(CultureInfo.InvariantCulture),
            ["book_id"] = bookId.ToString(CultureInfo.InvariantCulture),
            ["chapter"] = request.ChaptersCount.ToString(CultureInfo.InvariantCulture),
            ["user_input"] = prompt
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl) { Content = content };

        string responseData;
        try
        {
            using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, upstreamCts.Token);
            using var response = await _bookApiClient.SendAsync(httpRequest, BookApiCallTimeoutKind.LongRunning, linked.Token);
            responseData = await response.Content.ReadAsStringAsync(linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                var shortDetail = responseData.Length > 240 ? responseData[..240] + "…" : responseData;
                _logger.LogWarning("Book generation upstream {Status}: {Detail}", (int)response.StatusCode, shortDetail);
                return Fail($"AI service returned {(int)response.StatusCode}. Try again in a few minutes.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail("Generation was cancelled.");
        }
        catch (TaskCanceledException)
        {
            return Fail("The AI service did not respond in time. Wait a few minutes and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Book generation failed for book {BookId}", bookId);
            return Fail("Generation failed. Please try again.");
        }

        var extracted = UpstreamResponseParser.ExtractContent(responseData);
        if (string.IsNullOrWhiteSpace(extracted))
            return Fail("AI returned empty content.");

        var sanitized = BookGenerationHtmlSanitizer.Sanitize(extracted);
        if (string.IsNullOrWhiteSpace(sanitized))
            return Fail("AI content could not be sanitized to valid HTML.");

        var chapters = BookContentHtmlParser.ToChapterDtos(sanitized);
        book.BookContentHtml = sanitized;
        book.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(request.BookTitle))
            book.Title = request.BookTitle.Trim();

        await BookTitleResolver.SyncBookTitleAsync(_context, userId, bookId, request.BookTitle);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Saved BookContentHtml for book {BookId}: {Chars} chars, {Chapters} chapters.",
            bookId, sanitized.Length, chapters.Count);

        return new BookGenerationResult
        {
            Success = true,
            Message = "Book HTML generated and saved.",
            Html = sanitized,
            ChapterCount = chapters.Count
        };
    }

    private static BookGenerationResult Fail(string message) =>
        new() { Success = false, Message = message };
}
