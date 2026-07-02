using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EBookDashboard.Services;

/// <summary>
/// Single orchestrator for wiping temporary eBook editor data (Settings, DB rows, uploads, session).
/// Chapters are preserved unless <see cref="EditorDraftResetScope.FullProjectWithChapters"/> is requested.
/// </summary>
public sealed class EditorDraftResetService : IEditorDraftResetService
{
    private readonly ApplicationDbContext _context;
    private readonly BookFlowStateService _bookFlow;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<EditorDraftResetService> _logger;

    private static readonly string[] CoverAssetSuffixes =
    [
        "aiCoverLastPreview",
        "aiCoverPrompt",
        "printReadyCoverFront",
        "printReadyCoverWrap",
        "printReadyCoverWrapApi",
        "printReadyCoverBack",
        "printReadyCoverSpine",
        "printReadySpineInches",
        "printReadyPageCount",
        "printReadyTrimSize",
        "printReadyCoverFrontSha256",
        "printReadyCoverFrontAssetRef",
        "printReadyCoverWrapStatus"
    ];

    public EditorDraftResetService(
        ApplicationDbContext context,
        BookFlowStateService bookFlow,
        IWebHostEnvironment env,
        ILogger<EditorDraftResetService> logger)
    {
        _context = context;
        _bookFlow = bookFlow;
        _env = env;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<EditorDraftResetResult> ResetAsync(
        int bookId,
        int userId,
        EditorDraftResetScope scope,
        string? currentStep,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (bookId <= 0 || userId <= 0)
            return Fail(bookId, "Invalid book or user.");

        var owns = await _context.Books.AsNoTracking()
            .AnyAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (!owns)
            return Fail(bookId, "Book not found.");

        try
        {
            return scope switch
            {
                EditorDraftResetScope.StepBack => await ResetStepBackAsync(bookId, userId, currentStep, httpContext, cancellationToken),
                EditorDraftResetScope.BackToWriter => await ResetBackToWriterAsync(bookId, userId, httpContext, cancellationToken),
                EditorDraftResetScope.FullProject => await ResetFullProjectAsync(bookId, userId, httpContext, deleteChapters: false, cancellationToken),
                EditorDraftResetScope.FullProjectWithChapters => await ResetFullProjectAsync(bookId, userId, httpContext, deleteChapters: true, cancellationToken),
                _ => Fail(bookId, "Unknown reset scope.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Editor draft reset failed for book {BookId}, scope {Scope}, user {UserId}", bookId, scope, userId);
            return Fail(bookId, "Reset failed. Please try again.");
        }
    }

    /// <inheritdoc />
    public async Task ClearCoverAssetsAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var keys = CoverAssetSuffixes.Select(s => $"book:{bookId}:{s}").ToList();
        var rows = await _context.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(cancellationToken);
        if (rows.Count == 0) return;
        _context.Settings.RemoveRange(rows);
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task<EditorDraftResetResult> ResetStepBackAsync(
        int bookId,
        int userId,
        string? currentStep,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var step = (currentStep ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(step))
            return Fail(bookId, "Current workflow step is required.");

        await _bookFlow.RegressAndResetAsync(bookId, step, cancellationToken);

        if (step == BookFlowStateService.StepCover)
        {
            await ClearCoverAssetsAsync(bookId, cancellationToken);
            await ClearBookCoverImagePathAsync(bookId, userId, cancellationToken);
            httpContext.Session.Remove("CoverFinalized");
        }

        if (step == BookFlowStateService.StepFormat)
        {
            await ClearBookFormattingRowAsync(bookId, userId, cancellationToken);
            httpContext.Session.SetString("FormattingDone", "0");
        }

        var (newStep, newPath) = await _bookFlow.GetStepAsync(bookId, cancellationToken);
        SyncSessionAfterReset(httpContext, bookId, newStep);

        return new EditorDraftResetResult
        {
            Success = true,
            BookId = bookId,
            Step = newStep,
            Wiped = true,
            ResumeUrl = _bookFlow.BuildResumeUrl(bookId, newStep, newPath)
        };
    }

    private async Task<EditorDraftResetResult> ResetBackToWriterAsync(
        int bookId,
        int userId,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        await _bookFlow.ResetStepAsync(bookId, BookFlowStateService.StepCover, cancellationToken);
        await _bookFlow.ResetStepAsync(bookId, BookFlowStateService.StepFormat, cancellationToken);
        await ClearCoverAssetsAsync(bookId, cancellationToken);
        await ClearBookCoverImagePathAsync(bookId, userId, cancellationToken);
        await ClearBookFormattingRowAsync(bookId, userId, cancellationToken);

        var (_, path) = await _bookFlow.GetStepAsync(bookId, cancellationToken);
        await _bookFlow.SaveStepAsync(bookId, BookFlowStateService.StepGenerate, path, cancellationToken);

        ClearWorkflowSession(httpContext);
        httpContext.Session.SetInt32("LastSelectedBookId", bookId);
        httpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);
        httpContext.Session.SetString("HasGeneratedBook", "1");

        _logger.LogInformation("Back-to-writer reset completed for book {BookId}, user {UserId}", bookId, userId);

        return new EditorDraftResetResult
        {
            Success = true,
            BookId = bookId,
            Step = BookFlowStateService.StepGenerate,
            Wiped = true,
            ResumeUrl = $"/Books/AIGenerateBook?bookId={bookId}&fresh=1"
        };
    }

    private async Task<EditorDraftResetResult> ResetFullProjectAsync(
        int bookId,
        int userId,
        HttpContext httpContext,
        bool deleteChapters,
        CancellationToken cancellationToken)
    {
        await PurgeAllBookSettingsAsync(bookId, cancellationToken);
        await ClearBookFormattingRowAsync(bookId, userId, cancellationToken);
        await ClearBookDraftFieldsAsync(bookId, userId, cancellationToken);
        await ClearUserResumePointersAsync(userId, bookId, cancellationToken);
        await DeleteUploadArtifactsAsync(userId, bookId, cancellationToken);

        if (deleteChapters)
            await DeleteChapterDraftsAsync(bookId, userId, cancellationToken);

        var (_, path) = await _bookFlow.GetStepAsync(bookId, cancellationToken);
        await _bookFlow.SaveStepAsync(bookId, BookFlowStateService.StepGenerate, path, cancellationToken);

        ClearWorkflowSession(httpContext);
        httpContext.Session.Remove($"ResumeUrl:{userId}");
        httpContext.Session.Remove($"ResumeBookId:{userId}");
        httpContext.Session.SetInt32("LastSelectedBookId", bookId);
        httpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);
        httpContext.Session.SetString("HasGeneratedBook", deleteChapters ? "0" : "1");

        _logger.LogInformation(
            "Full project reset for book {BookId}, user {UserId}, chaptersDeleted={ChaptersDeleted}",
            bookId, userId, deleteChapters);

        return new EditorDraftResetResult
        {
            Success = true,
            BookId = bookId,
            Step = BookFlowStateService.StepGenerate,
            Wiped = true,
            ResumeUrl = $"/Books/AIGenerateBook?bookId={bookId}&fresh=1"
        };
    }

    private async Task PurgeAllBookSettingsAsync(int bookId, CancellationToken cancellationToken)
    {
        var prefix = $"book:{bookId}:";
        await _context.Settings
            .Where(s => s.Key.StartsWith(prefix))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task ClearBookFormattingRowAsync(int bookId, int userId, CancellationToken cancellationToken)
    {
        await _context.BookFormatting
            .Where(f => f.BookId == bookId && f.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task ClearBookCoverImagePathAsync(int bookId, int userId, CancellationToken cancellationToken)
    {
        var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (book == null) return;
        book.CoverImagePath = "";
        book.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearBookDraftFieldsAsync(int bookId, int userId, CancellationToken cancellationToken)
    {
        var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId && b.UserId == userId, cancellationToken);
        if (book == null) return;
        book.CoverImagePath = "";
        book.ManuscriptPath = "";
        book.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task ClearUserResumePointersAsync(int userId, int bookId, CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            $"user:{userId}:lastBookWorkUrl",
            $"user:{userId}:lastBookId"
        };
        var rows = await _context.Settings.Where(s => keys.Contains(s.Key)).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            if (row.Key.EndsWith(":lastBookId", StringComparison.Ordinal) && row.Value != bookId.ToString())
                continue;
            if (row.Key.EndsWith(":lastBookWorkUrl", StringComparison.Ordinal)
                && BookResumeUrlHelper.TryParseBookIdFromWorkUrl(row.Value) != bookId)
                continue;
            _context.Settings.Remove(row);
        }
        if (rows.Count > 0)
            await _context.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteChapterDraftsAsync(int bookId, int userId, CancellationToken cancellationToken)
    {
        await _context.ChapterIterations
            .Where(c => c.BookId == bookId)
            .ExecuteDeleteAsync(cancellationToken);
        await _context.Chapters
            .Where(c => c.BookId == bookId)
            .ExecuteDeleteAsync(cancellationToken);
        await _context.APIRawResponse
            .Where(r => r.BookId == bookId && r.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task DeleteUploadArtifactsAsync(int userId, int bookId, CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var webRoot = _env.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot)) return;

        var bookDir = Path.Combine(webRoot, "uploads", userId.ToString(), "books", bookId.ToString());
        if (Directory.Exists(bookDir))
        {
            try
            {
                Directory.Delete(bookDir, recursive: true);
                _logger.LogDebug("Deleted upload folder {Path}", bookDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete upload folder {Path}", bookDir);
            }
        }

        var tempDir = Path.Combine(webRoot, "uploads", userId.ToString(), "books", "temp");
        if (!Directory.Exists(tempDir)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(tempDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { File.Delete(file); } catch { /* best-effort */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean temp upload folder for user {UserId}", userId);
        }
    }

    private static void ClearWorkflowSession(HttpContext httpContext)
    {
        httpContext.Session.SetString("FormattingDone", "0");
        httpContext.Session.Remove("CoverFinalized");
        httpContext.Session.Remove("CoverDesignHasGenerated");
        httpContext.Session.Remove("CoverDesignLastBookId");
        httpContext.Session.Remove("LastSelectedFormat");
        httpContext.Session.Remove("BookFormatPremiumBoth");
    }

    private static void SyncSessionAfterReset(HttpContext httpContext, int bookId, string flowStep)
    {
        httpContext.Session.SetInt32("LastSelectedBookId", bookId);
        httpContext.Session.SetInt32(BookFlowStateService.SessionEntryBookIdKey, bookId);
        BookResumeUrlHelper.SyncFlowSessionFlags(httpContext, flowStep);
    }

    private static EditorDraftResetResult Fail(int bookId, string message) =>
        new() { Success = false, BookId = bookId, Message = message };
}
