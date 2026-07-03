using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.Options;
using EBookDashboard.Services.BookApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using System.Text;

namespace EBookDashboard.Services;

/// <summary>Calls upstream /api/generate-cover and persists front-cover assets for Cover Design.</summary>
public interface ICoverFrontGenerationService
{
    Task<CoverFrontGenerationResult> GenerateAsync(int userId, DashboardGenerateCoverRequest req, CancellationToken cancellationToken);
}

public sealed class CoverFrontGenerationResult
{
    public bool Success { get; init; }
    public string Status { get; init; } = "error";
    public string Message { get; init; } = "";
    public string? CoverUrl { get; init; }
    public string[] Options { get; init; } = Array.Empty<string>();
    public string? ImageBase64 { get; init; }
    public string? ImageDataUrl { get; init; }
}

public sealed class CoverFrontGenerationService : ICoverFrontGenerationService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IBookApiClient _bookApiClient;
    private readonly IOptionsSnapshot<ExternalApiOptions> _externalApiOptions;
    private readonly IEditorDraftResetService _draftReset;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoverFrontGenerationService> _logger;

    public CoverFrontGenerationService(
        ApplicationDbContext context,
        IConfiguration configuration,
        IBookApiClient bookApiClient,
        IOptionsSnapshot<ExternalApiOptions> externalApiOptions,
        IEditorDraftResetService draftReset,
        IHttpClientFactory httpClientFactory,
        ILogger<CoverFrontGenerationService> logger)
    {
        _context = context;
        _configuration = configuration;
        _bookApiClient = bookApiClient;
        _externalApiOptions = externalApiOptions;
        _draftReset = draftReset;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CoverFrontGenerationResult> GenerateAsync(int userId, DashboardGenerateCoverRequest req, CancellationToken cancellationToken)
    {
        if (req == null || req.BookId <= 0)
            return Fail("BookId is required.");

        var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == req.BookId && b.UserId == userId, cancellationToken);
        if (book == null)
            return Fail("Book not found.");

        await _draftReset.ClearCoverAssetsAsync(req.BookId, cancellationToken);

        var description = (req.Description ?? "").Trim();
        await UpsertSettingAsync($"book:{req.BookId}:aiCoverPrompt",
            Settings.ClampValueLength(description, Settings.DbCompatMaxValueLength) ?? "", cancellationToken);

        var title = string.IsNullOrWhiteSpace(req.Title) ? (book.Title ?? "").Trim() : req.Title!.Trim();
        if (string.IsNullOrEmpty(title)) title = "My Book";

        var authorName = (req.Author ?? "").Trim();
        if (string.IsNullOrEmpty(authorName))
        {
            var u = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == book.UserId, cancellationToken);
            authorName = u?.FullName ?? u?.UserEmail ?? book.UserId.ToString();
        }

        var category = string.IsNullOrWhiteSpace(req.Genre) ? (book.Genre ?? "General").Trim() : req.Genre!.Trim();
        var styleKey = string.IsNullOrWhiteSpace(req.Style) ? "modern" : req.Style.Trim();
        var coverStyleLabel = CoverExternalApiHelper.MapCoverStyleForExternalApi(styleKey, description);

        var size = BookApiInputValidation.NormalizeSize(
            (_configuration["ExternalApi:CoverGenerateSize"] ?? "1024x1536").Trim(),
            "1024x1536");
        var quality = BookApiInputValidation.NormalizeQuality(
            (_configuration["ExternalApi:CoverGenerateQuality"] ?? "medium").Trim(),
            "medium");
        var apiUrl = _bookApiClient.ResolveUrl(_externalApiOptions.Value.GenerateCoverUrl, "/api/generate-cover").Trim();
        var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
        if (string.IsNullOrEmpty(apiKey))
            return Fail(ExternalApiKeyResolver.MissingKeyUserMessage);

        var variationCount = Math.Clamp(
            int.TryParse(_configuration["ExternalApi:CoverGenerateVariations"], out var vc) ? vc : 1,
            1, 4);

        var styleVariants = new[]
        {
            coverStyleLabel,
            coverStyleLabel + ", minimalist composition, bold modern typography",
            coverStyleLabel + ", dramatic cinematic lighting, rich photographic detail",
            coverStyleLabel + ", elegant classic layout, refined color palette"
        };

        _logger.LogInformation("[CoverFrontGeneration] bookId={BookId} url={Url} variations={N}",
            req.BookId, apiUrl, variationCount);

        var lastUpstreamError = "";

        async Task<string?> GenerateOneCoverAsync(int variantIndex)
        {
            var styleForVariant = styleVariants[variantIndex % styleVariants.Length];
            var variantPayload = new JObject
            {
                ["title"] = title,
                ["author_name"] = authorName,
                ["category"] = category,
                ["cover_style"] = styleForVariant,
                ["size"] = size,
                ["quality"] = quality
            };
            var variantJson = variantPayload.ToString(Newtonsoft.Json.Formatting.None);
            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                httpRequest.Content = new StringContent(variantJson, Encoding.UTF8, "application/json");
                using var oneCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
                var resp = await _bookApiClient.SendAsync(httpRequest, BookApiCallTimeoutKind.LongRunning, oneCts.Token);
                var body = await resp.Content.ReadAsStringAsync(oneCts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    var detail = CoverExternalApiHelper.TryExtractErrorMessage(body);
                    lastUpstreamError = string.IsNullOrWhiteSpace(detail)
                        ? $"Cover API returned HTTP {(int)resp.StatusCode}."
                        : $"Cover API: {detail}";
                    return null;
                }
                var got = CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse(body);
                if (got.Count == 0)
                {
                    var detail = CoverExternalApiHelper.TryExtractErrorMessage(body);
                    lastUpstreamError = string.IsNullOrWhiteSpace(detail)
                        ? "Cover API responded without an image (queue may be busy)."
                        : $"Cover API: {detail}";
                    return null;
                }
                return got[0];
            }
            catch (OperationCanceledException)
            {
                lastUpstreamError = "Cover API timed out — the AI queue is busy. Please try again in a minute.";
                return null;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "CoverFrontGeneration: cover API unreachable");
                lastUpstreamError = "Cover API is unreachable right now. Please try again shortly.";
                return null;
            }
        }

        var genTasks = Enumerable.Range(0, variationCount).Select(i => GenerateOneCoverAsync(i)).ToArray();
        var genResults = await Task.WhenAll(genTasks);

        var rawUrls = new List<string>();
        var seenRaw = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in genResults)
            if (!string.IsNullOrWhiteSpace(r) && seenRaw.Add(r!)) rawUrls.Add(r!);

        if (rawUrls.Count == 0)
        {
            var retry = await GenerateOneCoverAsync(0);
            if (!string.IsNullOrWhiteSpace(retry)) rawUrls.Add(retry!);
        }

        if (rawUrls.Count == 0)
        {
            var msg = string.IsNullOrWhiteSpace(lastUpstreamError)
                ? "The AI could not produce a cover image. Please try again in a minute."
                : lastUpstreamError;
            return Fail(msg);
        }

        var optionUrls = new List<string>();
        foreach (var raw in rawUrls)
        {
            var urlForClient = raw;
            try
            {
                var persisted = await TryPersistCoverReferenceAsync(userId, req.BookId, raw, cancellationToken);
                if (!string.IsNullOrEmpty(persisted)) urlForClient = persisted!;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not persist AI cover variation for book {BookId}", req.BookId);
            }
            optionUrls.Add(urlForClient);
        }

        var firstRaw = rawUrls[0];
        var firstOption = optionUrls[0];

        try
        {
            if (firstOption.StartsWith("/", StringComparison.Ordinal) || firstOption.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                await SaveFrontCoverPreviewAsync(userId, req.BookId, firstOption, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not persist default AI cover for book {BookId}", req.BookId);
        }

        string? imageBase64 = null;
        if (firstRaw.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
        {
            var idx = firstRaw.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                imageBase64 = firstRaw[(idx + "base64,".Length)..];
        }

        return new CoverFrontGenerationResult
        {
            Success = true,
            Status = "success",
            CoverUrl = firstOption,
            Options = optionUrls.ToArray(),
            ImageBase64 = imageBase64,
            ImageDataUrl = firstRaw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? firstRaw : null
        };
    }

    private static CoverFrontGenerationResult Fail(string message) =>
        new() { Success = false, Status = "error", Message = message };

    private async Task UpsertSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        value ??= "";
        if (key.Contains("aiCoverPrompt", StringComparison.OrdinalIgnoreCase))
            value = Settings.ClampValueLength(value, Settings.DbCompatMaxValueLength) ?? "";
        else
            value = Settings.ClampValueLength(value, Settings.MaxShortValueLength) ?? "";

        var row = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row == null)
        {
            row = new Settings
            {
                SettingId = await _context.NextSettingIdAsync(cancellationToken),
                Key = key,
                Category = "Book",
                CreatedAt = DateTime.UtcNow
            };
            _context.Settings.Add(row);
        }
        row.Value = value;
        row.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<string?> TryPersistCoverReferenceAsync(int userId, int bookId, string imageRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imageRef)) return null;
        var t = imageRef.Trim();
        if (t.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
            return await SaveDataUrlCoverToUploadsAsync(userId, bookId, t, cancellationToken);
        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return await DownloadRemoteCoverToUploadsAsync(userId, bookId, t, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not download remote cover for book {BookId}", bookId);
                return t;
            }
        }
        return t.StartsWith("/", StringComparison.Ordinal) ? t : null;
    }

    private async Task SaveFrontCoverPreviewAsync(int userId, int bookId, string persisted, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(persisted) || persisted.Length > Settings.DbCompatMaxValueLength) return;

        await UpsertSettingAsync($"book:{bookId}:aiCoverLastPreview", persisted, cancellationToken);
        await UpsertSettingAsync($"book:{bookId}:printReadyCoverFront", persisted, cancellationToken);
        await UpsertSettingAsync($"book:{bookId}:printReadyCoverFrontAssetRef", persisted, cancellationToken);

        var frontBytes = await CoverImageRefLoader.TryReadAsBytesAsync(
            persisted, webRootPath: null, _httpClientFactory, cancellationToken);
        if (frontBytes is { Length: > 0 })
        {
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(frontBytes));
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFrontSha256", hash, cancellationToken);
        }

        if (persisted.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
                if (book != null)
                {
                    book.CoverImagePath = persisted.Trim();
                    book.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not update Books.CoverImagePath for book {BookId} (cover file was saved)", bookId);
            }
        }
    }

    private static async Task<string> SaveDataUrlCoverToUploadsAsync(int userId, int bookId, string dataUrl, CancellationToken cancellationToken)
    {
        var comma = dataUrl.IndexOf(',', StringComparison.Ordinal);
        if (comma <= 0) throw new InvalidOperationException("Invalid data URL.");
        var header = dataUrl.Substring(0, comma);
        var b64 = dataUrl[(comma + 1)..].Trim();
        var ext = ".png";
        if (header.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || header.Contains("jpg", StringComparison.OrdinalIgnoreCase))
            ext = ".jpg";
        else if (header.Contains("webp", StringComparison.OrdinalIgnoreCase))
            ext = ".webp";
        var bytes = Convert.FromBase64String(b64);
        return await SaveCoverBytesToUploadsAsync(userId, bookId, bytes, ext, "cover_ai", cancellationToken);
    }

    private async Task<string> DownloadRemoteCoverToUploadsAsync(int userId, int bookId, string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(2);
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0) throw new InvalidOperationException("Remote cover image was empty.");
        var ext = ".png";
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        if (contentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || contentType.Contains("jpg", StringComparison.OrdinalIgnoreCase))
            ext = ".jpg";
        else if (contentType.Contains("webp", StringComparison.OrdinalIgnoreCase))
            ext = ".webp";
        return await SaveCoverBytesToUploadsAsync(userId, bookId, bytes, ext, "cover_ai", cancellationToken);
    }

    private static async Task<string> SaveCoverBytesToUploadsAsync(
        int userId, int bookId, byte[] bytes, string ext, string namePrefix, CancellationToken cancellationToken)
    {
        var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", userId.ToString(), "books", bookId.ToString());
        Directory.CreateDirectory(uploadsRoot);
        var fileName = $"{namePrefix}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
        var fullPath = Path.Combine(uploadsRoot, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
        return $"/uploads/{userId}/books/{bookId}/{fileName}";
    }
}
