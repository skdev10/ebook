using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Controllers
{
    [Route("BookProcessing")]
    public class BookProcessingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly BookProcessingService _processingService;

        // Constructor with dependency injection
        public BookProcessingController(ApplicationDbContext context)
        {
            _context = context;
            _processingService = new BookProcessingService(_context);
        }
        // Add this to your BookProcessingController
        [HttpGet]
        [Route("")] // Matches /BookProcessing
        [Route("Index")] // Matches /BookProcessing/Index
        public IActionResult Index()
        {
            return View();
        }

        // Process a specific raw response
        [HttpPost]
        [Route("ProcessResponse")]
        [IgnoreAntiforgeryToken] // Add this attribute
        public async Task<IActionResult> ProcessResponse(int responseId)
        {
            try
            {
                var result = await _processingService.ProcessRawResponse(responseId);

            if (result)
                return Ok(new { success = true, message = $"Response {responseId} processed successfully" });
            else
                return BadRequest(new { success = false, message = $"Failed to process response {responseId}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Process all unprocessed responses
        [HttpPost]
        [Route("ProcessAllResponses")]
        [IgnoreAntiforgeryToken] // Add this attribute
        public async Task<IActionResult> ProcessAllResponses()
        {
            try
            {
                var processedCount = await _processingService.ProcessAllUnprocessedResponses();

            return Ok(new
            {
                success = true,
                message = $"Processed {processedCount} responses"
            });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        // Get list of unprocessed responses (for UI)
        [HttpGet]
        [Route("GetUnprocessedResponses")]
        public async Task<IActionResult> GetUnprocessedResponses()
        {
            var unprocessed = await _context.APIRawResponse
            .Where(r => string.IsNullOrEmpty(r.ParsedBookId) && !string.IsNullOrEmpty(r.ResponseData))
            .Select(r => new {
                r.ResponseId,
                r.Endpoint,
                r.CreatedAt,
                UserId = r.UserId ?? 0
            })
            .ToListAsync();

            return Ok(unprocessed);
        }
        [HttpGet]
        public async Task<IActionResult> DebugResponse(int responseId)
        {
            try
            {
                var response = await _context.APIRawResponse
                    .FirstOrDefaultAsync(r => r.ResponseId == responseId);

                if (response == null)
                {
                    return NotFound($"Response {responseId} not found");
                }

                var debugInfo = new
                {
                    ResponseId = response.ResponseId,
                    Endpoint = response.Endpoint,
                    UserId = response.UserId,
                    BookId = response.BookId,
                    HasRequestData = !string.IsNullOrEmpty(response.RequestData),
                    HasResponseData = !string.IsNullOrEmpty(response.ResponseData),
                    ResponseDataLength = response.ResponseData?.Length ?? 0,
                    ResponseDataPreview = response.ResponseData?.Substring(0, Math.Min(200, response.ResponseData?.Length ?? 0)),
                    ParsedBookId = response.ParsedBookId,
                    CreatedAt = response.CreatedAt
                };

                return Ok(debugInfo);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
            }
        }
    }
}
