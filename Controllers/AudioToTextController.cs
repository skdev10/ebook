using EBookDashboard.Services.BookApi;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AudioToTextController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AudioToTextController> _logger;
        private readonly IWebHostEnvironment _env;

        public AudioToTextController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AudioToTextController> logger,
            IWebHostEnvironment env)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _env = env;
        }

        [HttpPost("convert")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(20 * 1024 * 1024)]
        public async Task<IActionResult> Convert(
            [FromForm] IFormFile? audio,
            [FromForm] IFormFile? AudioFile,
            [FromForm] string? userId,
            [FromForm] string? bookId,
            [FromForm] int? chapter)
        {
            var audioFile = audio ?? AudioFile;

            if (audioFile == null || audioFile.Length == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    text = "",
                    message = "No audio file uploaded"
                });
            }

            var resolvedExt = BookApiInputValidation.ResolveAllowedAudioExtension(
                audioFile.FileName,
                audioFile.ContentType);
            if (string.IsNullOrEmpty(resolvedExt))
            {
                return BadRequest(new
                {
                    success = false,
                    text = "",
                    message = "Unsupported audio type. Allowed: " + string.Join(", ", BookApiConstants.ValidAudioExtensions)
                        + " (or record via the mic — browsers may send webm/ogg)."
                });
            }

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "audio");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}{resolvedExt}";
            var savedFilePath = Path.Combine(uploadsFolder, fileName);

            try
            {
                await using (var fs = new FileStream(savedFilePath, FileMode.Create))
                {
                    await audioFile.CopyToAsync(fs);
                }

                _logger.LogInformation(
                    "Audio received: {File}, Size: {Size}, Saved to: {Path}",
                    audioFile.FileName,
                    audioFile.Length,
                    savedFilePath);

                int uid = int.TryParse(userId, out var u) ? u : 0;
                int bid = int.TryParse(bookId, out var b) ? b : 0;
                var chapRaw = chapter ?? 0;
                int chap = chapRaw < 1 ? 1 : chapRaw;

                var extKey = ExternalApiKeyResolver.Resolve(_configuration);

                string text = string.Empty;
                var externalAttempted = false;
                Exception? externalError = null;

                if (!string.IsNullOrEmpty(extKey))
                {
                    externalAttempted = true;
                    try
                    {
                        text = await ExternalBookApiAudio.TranscribeFileAsync(
                            _httpClientFactory,
                            _configuration,
                            savedFilePath,
                            uid,
                            bid,
                            chap,
                            HttpContext.RequestAborted);

                        _logger.LogInformation(
                            "External audio transcription length: {Len} chars (path {Path}).",
                            string.IsNullOrEmpty(text) ? 0 : text.Length,
                            savedFilePath);
                    }
                    catch (Exception ex)
                    {
                        externalError = ex;
                        text = string.Empty;
                        _logger.LogWarning(ex, "External audio API failed.");
                    }
                }
                else
                {
                    _logger.LogWarning("External API key empty (ExternalApi:ApiKey) — skipping external transcription.");
                }
                 
                if (!string.IsNullOrWhiteSpace(text))
                {   
                    return Ok(new
                    {
                        success = true,
                        text,
                        message = ""
                    });
                }
                
                string failMessage;

                if (externalAttempted && externalError != null)
                {
                    failMessage =
                        "Voice transcription servers are unavailable. You can try browser speech recognition or type your topic.";
                    return StatusCode(503, new
                    {
                        success = false,
                        text = "",
                        message = failMessage,
                        suggestBrowserSpeech = true
                    });
                }

                if (!externalAttempted)
                {
                    failMessage =
                        "Voice input is unavailable: set ExternalApi:ApiKey (environment: ExternalApi__ApiKey).";
                    return StatusCode(503, new
                    {
                        success = false,
                        text = "",
                        message = failMessage,
                        suggestBrowserSpeech = true
                    });
                }

                failMessage =
                    externalError != null
                        ? $"Audio transcription failed: {externalError.Message}"
                        : "Audio transcription returned empty result.";

                return StatusCode(503, new
                {
                    success = false,
                    text = "",
                    message = failMessage,
                    suggestBrowserSpeech = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio transcription failed unexpectedly");

                if (System.IO.File.Exists(savedFilePath))
                {
                    try
                    {
                        System.IO.File.Delete(savedFilePath);
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogWarning(deleteEx, "Failed to delete saved audio file on error");
                    }
                }

                return StatusCode(503, new
                {
                    success = false,
                    text = "",
                    message = "Audio transcription failed. Please try again or type your topic manually.",
                    suggestBrowserSpeech = true
                });
            }
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "Healthy",
                service = "Audio transcription (external API)",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
