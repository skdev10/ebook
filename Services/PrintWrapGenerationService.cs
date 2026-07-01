using System.Globalization;
using System.Text;
using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Interfaces;
using EBookDashboard.Infrastructure;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.Options;
using EBookDashboard.Services;
using EBookDashboard.Services.BookApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace EBookDashboard.Services;

/// <summary>
/// Generates full print wrap via upstream split API when a front cover exists (Paperback/Both only).
/// </summary>
public sealed class PrintWrapGenerationService : IPrintWrapGenerationService
{
    /// <summary>Upstream split API returns HTTP 500 above this page count (observed on live server).</summary>
    private const int UpstreamSplitApiSafeMaxPageCount = 100;

    private readonly ApplicationDbContext _context;
    private readonly IBookService _bookService;
    private readonly IBookPageMetricsService _pageMetrics;
    private readonly IBookApiClient _bookApiClient;
    private readonly IOptionsSnapshot<ExternalApiOptions> _externalApiOptions;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IKdpCoverDimensionService _kdpCoverDimensions;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PrintWrapGenerationService> _logger;

    public PrintWrapGenerationService(
        ApplicationDbContext context,
        IBookService bookService,
        IBookPageMetricsService pageMetrics,
        IBookApiClient bookApiClient,
        IOptionsSnapshot<ExternalApiOptions> externalApiOptions,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IKdpCoverDimensionService kdpCoverDimensions,
        IWebHostEnvironment env,
        ILogger<PrintWrapGenerationService> logger)
    {
        _context = context;
        _bookService = bookService;
        _pageMetrics = pageMetrics;
        _bookApiClient = bookApiClient;
        _externalApiOptions = externalApiOptions;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _kdpCoverDimensions = kdpCoverDimensions;
        _env = env;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> TryGenerateFromSavedFrontAsync(
        int userId,
        int bookId,
        int? pageCountOverride = null,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default)
    {
        var exportOpt = await LoadExportOptionsAsync(userId, bookId, cancellationToken);
        var fmt = (exportOpt.Format ?? "Ebook").Trim();
        if (fmt.Equals("Print", StringComparison.OrdinalIgnoreCase))
            fmt = "Paperback";
        if (!fmt.Equals("Paperback", StringComparison.OrdinalIgnoreCase)
            && !fmt.Equals("Both", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Skip print wrap pregeneration for book {BookId}: format={Format}", bookId, fmt);
            return false;
        }

        var wrapKey = $"book:{bookId}:printReadyCoverWrap";
        var wrapApiKey = $"book:{bookId}:printReadyCoverWrapApi";
        if (!forceRegenerate)
        {
            var existingWrap = await _context.Settings.AsNoTracking()
                .Where(s => s.Key == wrapKey || s.Key == wrapApiKey)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(existingWrap))
                return true;
        }
        else
        {
            await ClearWrapSettingsAsync(bookId, cancellationToken);
        }

        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (book == null) return false;

        var assetKeys = new[]
        {
            $"book:{bookId}:printReadyCoverFront",
            $"book:{bookId}:aiCoverLastPreview"
        };
        var assetRows = await _context.Settings.AsNoTracking()
            .Where(s => assetKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);
        var savedFront = BookCoverRefResolver.ResolveEbookFrontCoverRef(
            assetRows.GetValueOrDefault($"book:{bookId}:printReadyCoverFront"),
            assetRows.GetValueOrDefault($"book:{bookId}:aiCoverLastPreview"),
            book.CoverImagePath,
            null);
        if (string.IsNullOrWhiteSpace(savedFront))
        {
            _logger.LogDebug("Skip print wrap pregeneration for book {BookId}: no front cover", bookId);
            return false;
        }

        var details = await _bookService.GetBookDetailsForPreviewAsync(userId, bookId);
        if (details == null || !details.Success) return false;

        var metrics = _pageMetrics.Estimate(details, exportOpt);
        var pageCount = pageCountOverride is > 0 ? pageCountOverride.Value : await ResolvePrintReadyPageCountAsync(bookId, cancellationToken);
        pageCount = pageCount > 0 ? pageCount : (metrics.PageCount > 0 ? metrics.PageCount : KdpPaperbackConstants.MinPageCount);
        pageCount = Math.Clamp(pageCount, 1, KdpPaperbackConstants.MaxPageCount);
        if (pageCount > UpstreamSplitApiSafeMaxPageCount)
        {
            _logger.LogWarning(
                "Capping wrap page_count from {Original} to {Cap} for upstream split API (book {BookId})",
                pageCount, UpstreamSplitApiSafeMaxPageCount, bookId);
            pageCount = UpstreamSplitApiSafeMaxPageCount;
        }

        var trimSize = NormalizeTrimSizeForApi(null, exportOpt);
        var kdp = CalculatePrintReadyKdp(pageCount, trimSize);
        var title = (details.BookTitle ?? book.Title ?? "My Book").Trim();
        if (string.IsNullOrWhiteSpace(title)) title = "My Book";

        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        var authorName = (user?.FullName ?? user?.UserEmail ?? userId.ToString(CultureInfo.InvariantCulture)).Trim();

        var quality = BookApiInputValidation.NormalizeQuality(
            (_externalApiOptions.Value.PrintReadyCoverQuality ?? "high").Trim(), "high");
        var size = BookApiInputValidation.NormalizeSize(
            (_externalApiOptions.Value.PrintReadyCoverSize ?? "1536x1024").Trim(), "1536x1024");
        var splitUrl = _bookApiClient.ResolveUrl(
            _externalApiOptions.Value.GenerateSpineBookCoverSplitUrl,
            "/api/generate-spine-book-cover-split").Trim();
        var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Skip print wrap pregeneration for book {BookId}: no API key", bookId);
            return false;
        }

        var payloadObj = new JObject
        {
            ["title"] = title,
            ["author_name"] = authorName,
            ["size"] = size,
            ["quality"] = quality,
            ["Interior_trim_size"] = trimSize,
            ["page_count"] = pageCount,
            ["paper_type"] = NormalizePaperTypeForExternalApi(kdp.PaperType)
        };

        var frontB64 = await CoverImageRefLoader.TryReadAsRawBase64Async(
            savedFront, _env.WebRootPath, _httpClientFactory, cancellationToken);
        if (string.IsNullOrEmpty(frontB64))
        {
            _logger.LogWarning("Skip print wrap pregeneration for book {BookId}: could not read front cover bytes", bookId);
            return false;
        }

        payloadObj["encoded_image"] = frontB64;

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, splitUrl);
            httpRequest.Content = new StringContent(
                payloadObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

            using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
            var response = await _bookApiClient.SendAsync(httpRequest, BookApiCallTimeoutKind.LongRunning, upstreamCts.Token);
            var responseData = await response.Content.ReadAsStringAsync(upstreamCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Print wrap pregeneration HTTP {Code} for book {BookId}: {Body}",
                    (int)response.StatusCode, bookId, responseData);
                return false;
            }

            var urls = CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse(responseData);
            var assets = CoverExternalApiHelper.ExtractNamedCoverAssetsFromApiResponse(responseData);
            var wrapRef = !string.IsNullOrWhiteSpace(assets.Wrap) ? assets.Wrap : (urls.FirstOrDefault() ?? "");
            if (string.IsNullOrWhiteSpace(wrapRef))
            {
                _logger.LogWarning("Print wrap pregeneration: no wrap image in response for book {BookId}", bookId);
                return false;
            }

            var persistedWrap = await TryPersistCoverReferenceAsync(userId, bookId, wrapRef, cancellationToken) ?? wrapRef;
            var persistedBack = string.IsNullOrWhiteSpace(assets.Back)
                ? ""
                : (await TryPersistCoverReferenceAsync(userId, bookId, assets.Back, cancellationToken)
                   ?? CoverExternalApiHelper.NormalizeImageRef(assets.Back));
            var persistedSpine = string.IsNullOrWhiteSpace(assets.Spine)
                ? ""
                : (await TryPersistCoverReferenceAsync(userId, bookId, assets.Spine, cancellationToken)
                   ?? CoverExternalApiHelper.NormalizeImageRef(assets.Spine));

            if (persistedWrap.Length <= Settings.DbCompatMaxValueLength)
            {
                await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapApi", persistedWrap, cancellationToken);
                await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrap", persistedWrap, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(persistedBack))
                await UpsertSettingAsync($"book:{bookId}:printReadyCoverBack", persistedBack, cancellationToken);
            if (!string.IsNullOrWhiteSpace(persistedSpine))
                await UpsertSettingAsync($"book:{bookId}:printReadyCoverSpine", persistedSpine, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyPageCount", pageCount.ToString(CultureInfo.InvariantCulture), cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyTrimSize", trimSize, cancellationToken);

            _logger.LogInformation("Print wrap pregenerated for book {BookId} ({Pages} pages)", bookId, pageCount);
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Print wrap pregeneration timed out for book {BookId}", bookId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Print wrap pregeneration failed for book {BookId}", bookId);
            return false;
        }
    }

    private async Task<BookPdfExportOptions> LoadExportOptionsAsync(int userId, int bookId, CancellationToken cancellationToken)
    {
        var draftRow = await _context.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:formattingDraft", cancellationToken);
        var fmtRow = await _context.BookFormatting.AsNoTracking()
            .FirstOrDefaultAsync(f => f.BookId == bookId && f.UserId == userId, cancellationToken);
        return BookPdfExportOptions.LoadFromPersistence(fmtRow, draftRow?.Value);
    }

    private async Task<int> ResolvePrintReadyPageCountAsync(int bookId, CancellationToken cancellationToken)
    {
        var saved = await _context.Settings.AsNoTracking()
            .Where(s => s.Key == $"book:{bookId}:printReadyPageCount")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (int.TryParse(saved, out var fromSaved) && fromSaved > 0 && fromSaved <= KdpPaperbackConstants.MaxPageCount)
            return fromSaved;

        var draft = await _context.Settings.AsNoTracking()
            .Where(s => s.Key == $"book:{bookId}:formattingDraft")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(draft)) return 0;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(draft);
            if (doc.RootElement.TryGetProperty("previewPageCount", out var pp)
                && pp.TryGetInt32(out var n) && n >= 1 && n <= KdpPaperbackConstants.MaxPageCount)
                return n;
        }
        catch (System.Text.Json.JsonException) { /* ignore */ }
        return 0;
    }

    private static string NormalizeTrimSizeForApi(string? trimFromRequest, BookPdfExportOptions exportOptions)
    {
        var src = (trimFromRequest ?? "").Trim();
        if (string.IsNullOrWhiteSpace(src))
            src = (exportOptions.Format ?? "").Trim();
        var compact = src.Replace(" ", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if (compact is "5.5x8.5" or "5.5xin8.5in") return "5.5 x 8.5 in";
        if (compact is "8.5x11" or "8.5xin11in") return "8.5 x 11 in";
        return "6 x 9 in";
    }

    private KdpCalculateResponse CalculatePrintReadyKdp(int pageCount, string trimSizeLabel)
    {
        var trim = ParseTrimInches(trimSizeLabel);
        return _kdpCoverDimensions.Calculate(new KdpCalculateRequest
        {
            PageCount = pageCount,
            TrimWidth = trim.W,
            TrimHeight = trim.H,
            Dpi = KdpPaperbackConstants.DefaultDpi,
            Bleed = true,
            InteriorType = KdpPaperbackConstants.InteriorTypeStandardColor,
            PaperType = KdpPaperbackConstants.PaperTypeWhite
        });
    }

    private static (decimal W, decimal H) ParseTrimInches(string? trimSize)
    {
        var src = (trimSize ?? "").Trim().ToLowerInvariant().Replace(" ", "");
        if (src.Contains("5.5") && src.Contains("8.5")) return (5.5m, 8.5m);
        if (src.Contains("8.5") && src.Contains("11")) return (8.5m, 11m);
        return (6m, 9m);
    }

    private static string NormalizePaperTypeForExternalApi(string? paperType) =>
        (paperType ?? "").Contains("cream", StringComparison.OrdinalIgnoreCase) ? "cream" : "white";

    private async Task ClearWrapSettingsAsync(int bookId, CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            $"book:{bookId}:printReadyCoverWrap",
            $"book:{bookId}:printReadyCoverWrapApi",
            $"book:{bookId}:printReadyCoverBack",
            $"book:{bookId}:printReadyCoverSpine"
        };
        var rows = await _context.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(cancellationToken);
        if (rows.Count == 0) return;
        _context.Settings.RemoveRange(rows);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        value = Settings.ClampValueLength(value, Settings.MaxShortValueLength) ?? "";
        var setting = await _context.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (setting == null)
        {
            _context.Settings.Add(new Settings
            {
                SettingId = await _context.NextSettingIdAsync(cancellationToken),
                Key = key,
                Value = value,
                Category = "Book",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = DateTime.UtcNow;
        }

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

    private async Task<string> SaveDataUrlCoverToUploadsAsync(int userId, int bookId, string dataUrl, CancellationToken cancellationToken)
    {
        var comma = dataUrl.IndexOf(',', StringComparison.Ordinal);
        if (comma <= 0) throw new InvalidOperationException("Invalid data URL.");
        var header = dataUrl[..comma];
        var b64 = dataUrl[(comma + 1)..].Trim();
        var ext = ".png";
        if (header.Contains("jpeg", StringComparison.OrdinalIgnoreCase) || header.Contains("jpg", StringComparison.OrdinalIgnoreCase))
            ext = ".jpg";
        return await SaveCoverBytesToUploadsAsync(userId, bookId, Convert.FromBase64String(b64), ext, cancellationToken);
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
        return await SaveCoverBytesToUploadsAsync(userId, bookId, bytes, ext, cancellationToken);
    }

    private async Task<string> SaveCoverBytesToUploadsAsync(int userId, int bookId, byte[] bytes, string ext, CancellationToken cancellationToken)
    {
        var relDir = Path.Combine("uploads", userId.ToString(CultureInfo.InvariantCulture), "books", bookId.ToString(CultureInfo.InvariantCulture), "covers");
        var absDir = Path.Combine(_env.WebRootPath, relDir);
        Directory.CreateDirectory(absDir);
        var fileName = $"wrap-pregen-{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
        var absPath = Path.Combine(absDir, fileName);
        await File.WriteAllBytesAsync(absPath, bytes, cancellationToken);
        return "/" + Path.Combine(relDir, fileName).Replace('\\', '/');
    }
}
