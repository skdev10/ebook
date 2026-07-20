using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Interfaces;
using EBookDashboard.Infrastructure;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using EBookDashboard.Models.Options;
using EBookDashboard.Services.BookApi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace EBookDashboard.Services;

/// <summary>
/// Builds full print wrap locally from the saved front cover (ImageSharp).
/// Front panel pixels match generate-cover exactly; back and spine extend that same artwork.
/// </summary>
public sealed class PrintWrapGenerationService : IPrintWrapGenerationService
{
    public const string StatusReady = "Ready";
    public const string StatusGenerating = "Generating";
    public const string StatusFailed = "Failed";
    public const string StatusNeedsDescription = "NeedsDescription";
    public const string StatusMissingFront = "MissingFront";

    private readonly ApplicationDbContext _context;
    private readonly IBookService _bookService;
    private readonly IBookPageMetricsService _pageMetrics;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBookApiClient _bookApiClient;
    private readonly IOptions<ExternalApiOptions> _externalApiOptions;
    private readonly IConfiguration _configuration;
    private readonly IKdpCoverDimensionService _kdpCoverDimensions;
    private readonly IPrintWrapCompositor _wrapCompositor;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PrintWrapGenerationService> _logger;

    public PrintWrapGenerationService(
        ApplicationDbContext context,
        IBookService bookService,
        IBookPageMetricsService pageMetrics,
        IHttpClientFactory httpClientFactory,
        IBookApiClient bookApiClient,
        IOptions<ExternalApiOptions> externalApiOptions,
        IConfiguration configuration,
        IKdpCoverDimensionService kdpCoverDimensions,
        IPrintWrapCompositor wrapCompositor,
        IWebHostEnvironment env,
        ILogger<PrintWrapGenerationService> logger)
    {
        _context = context;
        _bookService = bookService;
        _pageMetrics = pageMetrics;
        _httpClientFactory = httpClientFactory;
        _bookApiClient = bookApiClient;
        _externalApiOptions = externalApiOptions;
        _configuration = configuration;
        _kdpCoverDimensions = kdpCoverDimensions;
        _wrapCompositor = wrapCompositor;
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

        var book = await _context.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (book == null) return false;

        // Resolve front the same way Cover Design / Publish do — not only printReadyCoverFront.
        // Race: wrap was often queued before SetActiveCover finished writing printReadyCoverFront,
        // which left status MissingFront / Generating forever with a broken preview image.
        var frontKeys = new[]
        {
            $"book:{bookId}:printReadyCoverFront",
            $"book:{bookId}:aiCoverLastPreview"
        };
        var frontRows = await _context.Settings.AsNoTracking()
            .Where(s => frontKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);
        var frontAssetRef = BookCoverRefResolver.ResolveEbookFrontCoverRef(
            frontRows.GetValueOrDefault($"book:{bookId}:printReadyCoverFront"),
            frontRows.GetValueOrDefault($"book:{bookId}:aiCoverLastPreview"),
            book.CoverImagePath,
            null);
        frontAssetRef = (frontAssetRef ?? "").Trim();
        if (string.IsNullOrWhiteSpace(frontAssetRef))
        {
            _logger.LogWarning(
                "Print wrap requires a saved front cover for book {BookId} — generate front cover in Cover Design first.",
                bookId);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusMissingFront, cancellationToken);
            return false;
        }

        // Ensure the canonical front key exists so later downloads / match checks stay consistent.
        var storedPrintFront = (frontRows.GetValueOrDefault($"book:{bookId}:printReadyCoverFront") ?? "").Trim();
        if (!string.Equals(storedPrintFront, frontAssetRef, StringComparison.OrdinalIgnoreCase))
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFront", frontAssetRef, cancellationToken);

        var frontBytes = await CoverImageRefLoader.TryReadAsBytesAsync(
            frontAssetRef, _env.WebRootPath, _httpClientFactory, cancellationToken);
        if (frontBytes == null || frontBytes.Length == 0)
        {
            _logger.LogWarning(
                "Print wrap could not read front cover bytes for book {BookId} ref={Ref}",
                bookId, frontAssetRef);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusFailed, cancellationToken);
            return false;
        }

        var wrapKey = $"book:{bookId}:printReadyCoverWrap";
        if (!forceRegenerate)
        {
            var cacheKeys = new[]
            {
                wrapKey,
                $"book:{bookId}:printReadyCoverFrontSha256",
                $"book:{bookId}:printReadyCoverFrontAssetRef",
                $"book:{bookId}:printReadyPageCount"
            };
            var cacheRows = await _context.Settings.AsNoTracking()
                .Where(s => cacheKeys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);
            var existingWrap = (cacheRows.GetValueOrDefault(wrapKey) ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(existingWrap))
            {
                var storedSha = (cacheRows.GetValueOrDefault($"book:{bookId}:printReadyCoverFrontSha256") ?? "").Trim();
                var storedFrontRef = (cacheRows.GetValueOrDefault($"book:{bookId}:printReadyCoverFrontAssetRef") ?? "").Trim();
                var frontHash = Convert.ToHexString(SHA256.HashData(frontBytes));
                var frontMatches = string.Equals(storedFrontRef, frontAssetRef, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(storedSha) || string.Equals(storedSha, frontHash, StringComparison.OrdinalIgnoreCase));
                if (frontMatches)
                {
                    await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusReady, cancellationToken);
                    return true;
                }

                _logger.LogInformation("Stale print wrap for book {BookId} — front changed; rebuilding.", bookId);
            }
        }
        else
        {
            await ClearWrapSettingsAsync(bookId, cancellationToken);
        }

        await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusGenerating, cancellationToken);

        var details = await _bookService.GetBookDetailsForPreviewAsync(userId, bookId);
        if (details == null || !details.Success)
        {
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusFailed, cancellationToken);
            return false;
        }

        var metrics = _pageMetrics.Estimate(details, exportOpt);
        var pageCount = pageCountOverride is > 0 ? pageCountOverride.Value : await ResolvePrintReadyPageCountAsync(bookId, cancellationToken);
        pageCount = pageCount > 0 ? pageCount : (metrics.PageCount > 0 ? metrics.PageCount : KdpPaperbackConstants.MinPageCount);
        pageCount = Math.Clamp(pageCount, KdpPaperbackConstants.MinPageCount, KdpPaperbackConstants.MaxPageCount);

        var savedTrim = await _context.Settings.AsNoTracking()
            .Where(s => s.Key == $"book:{bookId}:printReadyTrimSize")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
        var trimSize = NormalizeTrimSizeForApi((savedTrim ?? "").Trim(), exportOpt);
        var kdp = CalculatePrintReadyKdp(pageCount, trimSize);
        var title = (details.BookTitle ?? book.Title ?? "My Book").Trim();
        if (string.IsNullOrWhiteSpace(title)) title = "My Book";

        var user = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        var authorName = (user?.FullName ?? user?.UserEmail ?? userId.ToString(CultureInfo.InvariantCulture)).Trim();
        var frontHashFinal = Convert.ToHexString(SHA256.HashData(frontBytes));
        var description = ResolveBackCoverDescription(book, details);

        // Local compositor ONLY for wrap-from-front.
        // Upstream AI APIs use a 30+ minute LongRunning budget and were leaving
        // printReadyCoverWrapStatus stuck on "Generating" for 15 minutes while the
        // Cover Design banner spun. Local compose is seconds and keeps the exact
        // approved front panel (preview ≡ export).
        var localOk = await TryGenerateViaLocalCompositorAsync(
                userId, bookId, frontBytes, frontAssetRef, frontHashFinal,
                title, authorName, description,
                pageCount, trimSize, kdp, exportOpt, cancellationToken);
        if (localOk)
            return true;

        await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusFailed, cancellationToken);
        _logger.LogWarning(
            "Local print wrap compositing failed for book {BookId} — not falling back to long-running AI wrap APIs.",
            bookId);
        return false;
    }

    /// <summary>
    /// Calls the documented <c>/api/generate-spine-book-cover</c> endpoint (title/author/trim/pages —
    /// no encoded_image). Used only by full AI print-cover flows, not wrap-from-front.
    /// </summary>
    private async Task<bool> TryGenerateViaFullSpineApiAsync(
        int userId,
        int bookId,
        string title,
        string authorName,
        string category,
        string imageDirection,
        string trimSize,
        int pageCount,
        KdpCalculateResponse kdp,
        CancellationToken cancellationToken)
    {
        var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
        if (string.IsNullOrEmpty(apiKey)) return false;

        var opt = _externalApiOptions.Value;
        var apiUrl = _bookApiClient.ResolveUrl(opt.GenerateSpineBookCoverUrl, "/api/generate-spine-book-cover").Trim();
        var size = BookApiInputValidation.NormalizeSize((opt.PrintReadyCoverSize ?? "1536x1024").Trim(), "1536x1024");
        var quality = BookApiInputValidation.NormalizeQuality((opt.PrintReadyCoverQuality ?? "high").Trim(), "high");

        // page_count above ~100 is known to 500 on the live API.
        var payload = new JObject
        {
            ["title"] = title,
            ["author_name"] = authorName,
            ["category"] = string.IsNullOrWhiteSpace(category) ? "General" : category,
            ["cover_style"] = BuildWrapCoverStyleDirective(imageDirection, kdp),
            ["size"] = size,
            ["quality"] = quality,
            ["Interior_trim_size"] = trimSize,
            ["page_count"] = Math.Clamp(pageCount, KdpPaperbackConstants.MinPageCount, 100),
            ["paper_type"] = NormalizePaperTypeForExternalApi(kdp.PaperType)
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            httpRequest.Content = new StringContent(
                payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

            using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, upstreamCts.Token);
            var response = await _bookApiClient.SendAsync(httpRequest, BookApiCallTimeoutKind.LongRunning, linkedCts.Token);
            var responseData = await response.Content.ReadAsStringAsync(linkedCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Full spine wrap API HTTP {Code} for book {BookId}: {Body}",
                    (int)response.StatusCode, bookId,
                    responseData.Length > 300 ? responseData[..300] + "…" : responseData);
                return false;
            }

            var assets = CoverExternalApiHelper.ExtractNamedCoverAssetsFromApiResponse(responseData);
            var urls = CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse(responseData);
            var wrapRef = !string.IsNullOrWhiteSpace(assets.Wrap) ? assets.Wrap : (urls.FirstOrDefault() ?? "");
            if (string.IsNullOrWhiteSpace(wrapRef)) return false;

            var persistedWrap = await PersistCoverRefAsync(userId, bookId, wrapRef, "wrap-api", cancellationToken);
            if (string.IsNullOrWhiteSpace(persistedWrap)) return false;

            var wrapBytes = await CoverImageRefLoader.TryReadAsBytesAsync(
                persistedWrap, _env.WebRootPath, _httpClientFactory, cancellationToken);
            if (wrapBytes == null || wrapBytes.Length == 0) return false;

            // Re-sync the front cover from the wrap's front panel so preview/exports match the wrap.
            var newFrontBytes = CoverWrapPanelExtractor.EnsureFrontPanelBytes(wrapBytes, pageCount, trimSize, assumeWrap: true);
            var persistedFront = await SaveCoverBytesToUploadsAsync(
                userId, bookId, newFrontBytes, ".png", "front-from-wrap", cancellationToken);
            var newFrontHash = Convert.ToHexString(SHA256.HashData(newFrontBytes));

            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapApi", persistedWrap, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrap", persistedWrap, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFront", persistedFront, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:aiCoverLastPreview", persistedFront, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFrontAssetRef", persistedFront, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFrontSha256", newFrontHash, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyPageCount", pageCount.ToString(CultureInfo.InvariantCulture), cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyTrimSize", trimSize, cancellationToken);

            if (!string.IsNullOrWhiteSpace(assets.Back))
            {
                var back = await PersistCoverRefAsync(userId, bookId, assets.Back, "back-api", cancellationToken);
                if (!string.IsNullOrWhiteSpace(back))
                    await UpsertSettingAsync($"book:{bookId}:printReadyCoverBack", back, cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(assets.Spine))
            {
                var spine = await PersistCoverRefAsync(userId, bookId, assets.Spine, "spine-api", cancellationToken);
                if (!string.IsNullOrWhiteSpace(spine))
                    await UpsertSettingAsync($"book:{bookId}:printReadyCoverSpine", spine, cancellationToken);
            }

            var trackedBook = await _context.Books
                .FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
            if (trackedBook != null)
            {
                trackedBook.CoverImagePath = persistedFront;
                await _context.SaveChangesAsync(cancellationToken);
            }

            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusReady, cancellationToken);
            _logger.LogInformation(
                "Full spine wrap API accepted for book {BookId}; front cover re-synced from wrap panel.",
                bookId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Full spine wrap API failed for book {BookId}.", bookId);
            return false;
        }
    }

    /// <summary>
    /// Calls upstream split API; accepts the wrap only when its front panel matches the saved front cover.
    /// </summary>
    private async Task<bool> TryGenerateViaSplitApiAsync(
        int userId,
        int bookId,
        byte[] frontBytes,
        string frontAssetRef,
        string frontHash,
        string title,
        string authorName,
        string trimSize,
        int pageCount,
        KdpCalculateResponse kdp,
        CancellationToken cancellationToken)
    {
        var apiKey = ExternalApiKeyResolver.Resolve(_configuration);
        if (string.IsNullOrEmpty(apiKey)) return false;

        var opt = _externalApiOptions.Value;
        var apiUrl = _bookApiClient.ResolveUrl(opt.GenerateSpineBookCoverSplitUrl, "/api/generate-spine-book-cover-split").Trim();
        var size = BookApiInputValidation.NormalizeSize((opt.PrintReadyCoverSize ?? "1536x1024").Trim(), "1536x1024");
        var quality = BookApiInputValidation.NormalizeQuality((opt.PrintReadyCoverQuality ?? "high").Trim(), "high");

        var payload = new JObject
        {
            ["title"] = title,
            ["author_name"] = authorName,
            ["encoded_image"] = Convert.ToBase64String(frontBytes),
            ["size"] = size,
            ["quality"] = quality,
            ["Interior_trim_size"] = trimSize,
            ["paper_type"] = NormalizePaperTypeForExternalApi(kdp.PaperType),
            ["page_count"] = pageCount
        };

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            httpRequest.Content = new StringContent(
                payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

            using var upstreamCts = BookApiUpstreamCancellation.CreateLongRunning(_configuration);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, upstreamCts.Token);
            var response = await _bookApiClient.SendAsync(httpRequest, BookApiCallTimeoutKind.LongRunning, linkedCts.Token);
            var responseData = await response.Content.ReadAsStringAsync(linkedCts.Token);
            if (!response.IsSuccessStatusCode) return false;

            var assets = CoverExternalApiHelper.ExtractNamedCoverAssetsFromApiResponse(responseData);
            var urls = CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse(responseData);
            var wrapRef = !string.IsNullOrWhiteSpace(assets.Wrap) ? assets.Wrap : (urls.FirstOrDefault() ?? "");
            if (string.IsNullOrWhiteSpace(wrapRef)) return false;

            var persistedWrap = await PersistCoverRefAsync(userId, bookId, wrapRef, "wrap-api", cancellationToken);
            if (string.IsNullOrWhiteSpace(persistedWrap)) return false;

            var wrapBytes = await CoverImageRefLoader.TryReadAsBytesAsync(
                persistedWrap, _env.WebRootPath, _httpClientFactory, cancellationToken);
            if (wrapBytes == null || !CoverWrapPanelExtractor.FrontPanelMatchesSavedFront(wrapBytes, frontBytes, pageCount, trimSize))
            {
                _logger.LogWarning(
                    "Split wrap API front panel mismatch for book {BookId} — using local compositor.",
                    bookId);
                await ClearWrapOnlyAsync(bookId, cancellationToken);
                return false;
            }

            if (persistedWrap.Length <= Settings.DbCompatMaxValueLength)
            {
                await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapApi", persistedWrap, cancellationToken);
                await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrap", persistedWrap, cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(assets.Back))
            {
                var back = await PersistCoverRefAsync(userId, bookId, assets.Back, "back-api", cancellationToken);
                if (!string.IsNullOrWhiteSpace(back))
                    await UpsertSettingAsync($"book:{bookId}:printReadyCoverBack", back, cancellationToken);
            }
            if (!string.IsNullOrWhiteSpace(assets.Spine))
            {
                var spine = await PersistCoverRefAsync(userId, bookId, assets.Spine, "spine-api", cancellationToken);
                if (!string.IsNullOrWhiteSpace(spine))
                    await UpsertSettingAsync($"book:{bookId}:printReadyCoverSpine", spine, cancellationToken);
            }

            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFrontAssetRef", frontAssetRef, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFrontSha256", frontHash, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyPageCount", pageCount.ToString(CultureInfo.InvariantCulture), cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyTrimSize", trimSize, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusReady, cancellationToken);
            _logger.LogInformation("Split wrap API accepted for book {BookId} (front panel verified).", bookId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Split wrap API failed for book {BookId}.", bookId);
            return false;
        }
    }

    private async Task ClearWrapOnlyAsync(int bookId, CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            $"book:{bookId}:printReadyCoverWrap",
            $"book:{bookId}:printReadyCoverWrapApi",
            $"book:{bookId}:printReadyCoverBack",
            $"book:{bookId}:printReadyCoverSpine"
        };
        var rows = await _context.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(cancellationToken);
        if (rows.Count > 0)
        {
            _context.Settings.RemoveRange(rows);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> TryGenerateViaLocalCompositorAsync(
        int userId,
        int bookId,
        byte[] frontBytes,
        string frontAssetRef,
        string frontHash,
        string title,
        string authorName,
        string description,
        int pageCount,
        string trimSize,
        KdpCalculateResponse kdp,
        BookPdfExportOptions exportOpt,
        CancellationToken cancellationToken)
    {
        var theme = BookTheme.FromExportOptions(exportOpt);

        try
        {
            var composed = _wrapCompositor.Compose(new PrintWrapComposeRequest
            {
                FrontCoverBytes = frontBytes,
                FrontCoverAssetRef = frontAssetRef,
                Layout = kdp,
                Title = title,
                Author = authorName,
                Description = description,
                Theme = theme
            });

            var persistedPath = await SaveCoverBytesToUploadsAsync(
                userId, bookId, composed.PngBytes, ".png", cancellationToken);

            // Drop any older API wrap so preview/download never fall back to mismatched art.
            var staleApi = await _context.Settings
                .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:printReadyCoverWrapApi", cancellationToken);
            if (staleApi != null)
            {
                _context.Settings.Remove(staleApi);
                await _context.SaveChangesAsync(cancellationToken);
            }

            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrap", persistedPath, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFrontAssetRef", frontAssetRef, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverFrontSha256", frontHash, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyPageCount", pageCount.ToString(CultureInfo.InvariantCulture), cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyTrimSize", trimSize, cancellationToken);
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusReady, cancellationToken);

            _logger.LogInformation(
                "Print wrap composed locally for book {BookId} ({Pages} pages).",
                bookId, pageCount);

            return true;
        }
        catch (Exception ex)
        {
            await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusFailed, cancellationToken);
            _logger.LogError(ex, "Local print wrap compositing failed for book {BookId}", bookId);
            return false;
        }
    }

    private async Task<string?> PersistCoverRefAsync(
        int userId,
        int bookId,
        string imageRef,
        string filePrefix,
        CancellationToken cancellationToken)
    {
        var normalized = CoverExternalApiHelper.NormalizeImageRef(imageRef);
        if (string.IsNullOrWhiteSpace(normalized)) return null;

        if (normalized.StartsWith("/", StringComparison.Ordinal))
            return normalized;

        var bytes = await CoverImageRefLoader.TryReadAsBytesAsync(
            normalized, _env.WebRootPath, _httpClientFactory, cancellationToken);
        if (bytes == null || bytes.Length == 0) return null;

        var ext = normalized.Contains("jpeg", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("jpg", StringComparison.OrdinalIgnoreCase)
            ? ".jpg"
            : ".png";
        return await SaveCoverBytesToUploadsAsync(userId, bookId, bytes, ext, filePrefix, cancellationToken);
    }

    /// <summary>
    /// Back-cover blurb for print wrap — uses saved description when present; otherwise a short excerpt or title-based fallback.
    /// Never blocks wrap generation.
    /// </summary>
    public static string ResolveBackCoverDescription(Books book, BookDetailsResponseDto? details)
    {
        var desc = (book.Description ?? details?.Description ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(desc)) return desc;

        foreach (var ch in (details?.Chapters ?? []).OrderBy(c => c.ChapterNumber))
        {
            if (ch.ChapterNumber <= 0 || string.IsNullOrWhiteSpace(ch.Content)) continue;
            var plain = HtmlTagRegex.Replace(ch.Content, " ").Trim();
            plain = System.Net.WebUtility.HtmlDecode(plain);
            plain = WhitespaceRegex.Replace(plain, " ").Trim();
            if (plain.Length < 80) continue;
            return plain.Length > 600 ? plain[..597] + "…" : plain;
        }

        var title = (details?.BookTitle ?? book.Title ?? "this book").Trim();
        if (string.IsNullOrWhiteSpace(title)) title = "this book";
        var genre = (book.Genre ?? details?.Genre ?? "").Trim();
        return string.IsNullOrWhiteSpace(genre)
            ? $"Discover {title} — an unforgettable read from the first page to the last."
            : $"In {title}, experience a compelling {genre.ToLowerInvariant()} story that keeps you turning the pages.";
    }

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static string NormalizePaperTypeForExternalApi(string? paperType)
    {
        if ((paperType ?? "").Contains("cream", StringComparison.OrdinalIgnoreCase))
            return "cream";
        return "white";
    }

    /// <summary>
    /// cover_style for the full-spine wrap API: user's Image Direction (realistic-biased) plus
    /// mandatory one-continuous-design wrap rules and exact KDP dimensions.
    /// </summary>
    private static string BuildWrapCoverStyleDirective(string imageDirection, KdpCalculateResponse kdp)
    {
        var style = CoverExternalApiHelper.MapCoverStyleForExternalApi("modern", imageDirection);

        var cohesion = string.Join(" ",
            "MANDATORY PRINT WRAP RULES:",
            "Create ONE continuous wraparound design (back + spine + front) with identical palette, textures, gradients, and ornamental language on all three panels.",
            "The back cover MUST repeat the same background colors and decorative frame system as the front — never a different color scheme on the back.",
            "The spine strip must visually continue the front/back background seamlessly across the exact spine width.",
            $"Paperback layout: {kdp.Bleed:F3} in bleed, spine {kdp.SpineWidth:F3} in for {kdp.PageCount} pages.",
            $"Full cover canvas: {kdp.FullCoverWidth:F3} in x {kdp.FullCoverHeight:F3} in.",
            "Panel order left-to-right: back cover, spine, front cover.",
            "No unrelated artwork on the back; back continues the front design with synopsis-safe space.");

        return style + " " + cohesion;
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
        var compact = src.Replace(" ", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if (compact is "5.5x8.5" or "5.5xin8.5in") return "5.5 x 8.5 in";
        if (compact is "8.5x11" or "8.5xin11in") return "8.5 x 11 in";
        if (compact is "6x9" or "6xin9in") return "6 x 9 in";
        if (src.Contains("5.5", StringComparison.Ordinal) && src.Contains("8.5", StringComparison.Ordinal))
            return "5.5 x 8.5 in";
        if (src.Contains("8.5", StringComparison.Ordinal) && src.Contains("11", StringComparison.Ordinal))
            return "8.5 x 11 in";
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
            Dpi = KdpConstants.Dpi,
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
        if (rows.Count > 0)
        {
            _context.Settings.RemoveRange(rows);
            await _context.SaveChangesAsync(cancellationToken);
        }

        await UpsertSettingAsync($"book:{bookId}:printReadyCoverWrapStatus", StatusGenerating, cancellationToken);
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

    private async Task<string> SaveCoverBytesToUploadsAsync(
        int userId,
        int bookId,
        byte[] bytes,
        string ext,
        CancellationToken cancellationToken)
        => await SaveCoverBytesToUploadsAsync(userId, bookId, bytes, ext, "wrap-composed", cancellationToken);

    private async Task<string> SaveCoverBytesToUploadsAsync(
        int userId,
        int bookId,
        byte[] bytes,
        string ext,
        string filePrefix,
        CancellationToken cancellationToken)
    {
        var relDir = Path.Combine("uploads", userId.ToString(CultureInfo.InvariantCulture), "books", bookId.ToString(CultureInfo.InvariantCulture), "covers");
        var absDir = Path.Combine(_env.WebRootPath, relDir);
        Directory.CreateDirectory(absDir);
        var fileName = $"{filePrefix}-{DateTime.UtcNow:yyyyMMddHHmmss}{ext}";
        var absPath = Path.Combine(absDir, fileName);
        await File.WriteAllBytesAsync(absPath, bytes, cancellationToken);
        return "/" + Path.Combine(relDir, fileName).Replace('\\', '/');
    }
}
