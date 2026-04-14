using System.Security.Claims;
using EBookDashboard.Interfaces;
using EBookDashboard.Models.DTO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers.Api;

/// <summary>
/// REST API for chapter plans and on-demand AI generation. Authenticate with the same session / auth cookie as the web app.
/// </summary>
[Authorize]
[ApiController]
[Route("api/v1/books/{bookId:int}")]
[Produces("application/json")]
public class BookChaptersApiController : ControllerBase
{
    private readonly IBookChapterPipelineService _chapters;
    private readonly ILogger<BookChaptersApiController> _logger;

    public BookChaptersApiController(IBookChapterPipelineService chapters, ILogger<BookChaptersApiController> logger)
    {
        _chapters = chapters;
        _logger = logger;
    }

    private int? ResolveUserId()
    {
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0)
            return id;
        var sid = HttpContext.Session.GetInt32("UserId");
        return sid is > 0 ? sid : null;
    }

    /// <summary>List all chapters for the book (outline, draft, or with body).</summary>
    [HttpGet("chapters")]
    [ProducesResponseType(typeof(IReadOnlyList<ChapterListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<ChapterListItemDto>>> ListChapters(int bookId, CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        if (userId == null) return Unauthorized(new { message = "Not authenticated." });
        try
        {
            var list = await _chapters.ListChaptersAsync(userId.Value, bookId, cancellationToken);
            return Ok(list);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>Create or replace a chapter plan (title, prompt, tone). ChapterNumber 0 = next available.</summary>
    [HttpPost("chapters")]
    [ProducesResponseType(typeof(ChapterOperationResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChapterOperationResult>> CreateChapter(int bookId, [FromBody] ChapterPlanCreateRequest body, CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        if (userId == null) return Unauthorized(new { message = "Not authenticated." });
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var result = await _chapters.UpsertChapterPlanAsync(userId.Value, bookId, body, cancellationToken);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>Update plan metadata for a chapter that is not locked.</summary>
    [HttpPut("chapters/{chapterNumber:int}")]
    public async Task<ActionResult<ChapterOperationResult>> UpdateChapter(int bookId, int chapterNumber, [FromBody] ChapterPlanUpdateRequest body, CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        if (userId == null) return Unauthorized(new { message = "Not authenticated." });
        try
        {
            var result = await _chapters.UpdateChapterPlanAsync(userId.Value, bookId, chapterNumber, body, cancellationToken);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>Remove a chapter row only if it is an outline / insubstantial draft.</summary>
    [HttpDelete("chapters/{chapterNumber:int}")]
    public async Task<ActionResult<ChapterOperationResult>> DeleteChapter(int bookId, int chapterNumber, CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        if (userId == null) return Unauthorized(new { message = "Not authenticated." });
        try
        {
            var result = await _chapters.DeleteChapterPlanAsync(userId.Value, bookId, chapterNumber, cancellationToken);
            if (!result.Success) return BadRequest(result);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>Call the external generate_chapter API with prior-chapter continuity. Default PreviewOnly=true (raw response logged; finalize via existing app flow to persist manuscript).</summary>
    [HttpPost("chapters/{chapterNumber:int}/generate")]
    [ProducesResponseType(typeof(ChapterGenerateResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<ChapterGenerateResultDto>> GenerateChapter(int bookId, int chapterNumber, [FromBody] ChapterGenerateApiRequest? body, CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        if (userId == null) return Unauthorized(new { message = "Not authenticated." });
        body ??= new ChapterGenerateApiRequest();
        try
        {
            var result = await _chapters.GenerateChapterAsync(userId.Value, bookId, chapterNumber, body, cancellationToken);
            if (!result.Success) return StatusCode(StatusCodes.Status502BadGateway, result);
            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateChapter API error");
            return StatusCode(500, new ChapterGenerateResultDto { Success = false, Message = ex.Message, ChapterNumber = chapterNumber });
        }
    }

    /// <summary>Generate multiple chapters. Parallel=true uses concurrent requests (same book still shares a server-side gate for stability).</summary>
    [HttpPost("chapters/generate-batch")]
    public async Task<ActionResult<IReadOnlyList<ChapterGenerateResultDto>>> GenerateBatch(int bookId, [FromBody] BatchChapterGenerateRequest body, CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        if (userId == null) return Unauthorized(new { message = "Not authenticated." });
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        try
        {
            var results = await _chapters.GenerateChaptersBatchAsync(userId.Value, bookId, body, cancellationToken);
            return Ok(results);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
