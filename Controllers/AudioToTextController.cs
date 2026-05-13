using EBookDashboard.Services.BookApi;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AudioToTextController : ControllerBase
    {
        private readonly OpenAIService2 _openAIService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AudioToTextController> _logger;
        private readonly IWebHostEnvironment _env;

        public AudioToTextController(
            OpenAIService2 openAIService,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AudioToTextController> logger,
            IWebHostEnvironment env)
        {
            _openAIService = openAIService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _env = env;
        }

        /// <summary>Resolved OpenAI key (config + optional env binding via OpenAI__ApiKey).</summary>
        private string? ResolvedOpenAiKey =>
            (_configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OpenAI__ApiKey"))
            ?.Trim();

        private static bool HasConfiguredOpenAi(OpenAIService2 svc, string? keyFromConfig)
            => !string.IsNullOrEmpty(keyFromConfig) || svc.IsConfigured;

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

            if (!BookApiInputValidation.IsAllowedAudioExtension(audioFile.FileName))
            {
                return BadRequest(new
                {
                    success = false,
                    text = "",
                    message = "Unsupported audio type. Allowed: " + string.Join(", ", BookApiConstants.ValidAudioExtensions)
                });
            }

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "audio");
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(audioFile.FileName)}";
            var savedFilePath = Path.Combine(uploadsFolder, fileName);
            var tempFilePath = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid()}{Path.GetExtension(audioFile.FileName)}");

            try
            {
                await using (var fs = new FileStream(savedFilePath, FileMode.Create))
                {
                    await audioFile.CopyToAsync(fs);
                }

                System.IO.File.Copy(savedFilePath, tempFilePath, true);

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

                // 1) External service first — if it fails (e.g. 500), Whisper is the fallback.
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
                        _logger.LogWarning(
                            ex,
                            "External audio API failed (non-success or parse error); will try OpenAI whisper-1 if configured.");
                    }
                }
                else
                {
                    _logger.LogWarning("External API key empty (ExternalApi:ApiKey or OpenAI:ApiKey) — skipping external transcription.");
                }

                // 2) OpenAI Whisper (whisper-1) fallback when external is missing/unavailable returned no text.
                var openAiKey = ResolvedOpenAiKey;
                if (string.IsNullOrWhiteSpace(text) && HasConfiguredOpenAi(_openAIService, openAiKey))
                {
                    try
                    {
                        text = await _openAIService.TranscribeAudioAsync(
                            userId ?? "unknown",
                            bookId ?? "unknown",
                            chap,
                            tempFilePath);

                        _logger.LogInformation(
                            "OpenAI whisper transcription length after external: {Len} chars.",
                            string.IsNullOrEmpty(text) ? 0 : text.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "OpenAI Whisper transcription failed after external attempt.");
                        text = string.Empty;
                    }
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

                var openAiAbsent = string.IsNullOrEmpty(openAiKey);
                string failMessage;

                if (externalAttempted && externalError != null && openAiAbsent)
                {
                    failMessage =
                        "Voice transcription servers are unavailable, and cloud fallback is not configured. You can try browser speech recognition or type your topic.";
                    return StatusCode(503, new
                    {
                        success = false,
                        text = "",
                        message = failMessage,
                        suggestBrowserSpeech = true
                    });
                }

                if (!externalAttempted && openAiAbsent)
                {
                    failMessage =
                        "Voice input is unavailable: set ExternalApi:ApiKey or OpenAI:ApiKey (environment: ExternalApi__ApiKey or OpenAI__ApiKey).";
                    return StatusCode(503, new
                    {
                        success = false,
                        text = "",
                        message = failMessage,
                        suggestBrowserSpeech = true
                    });
                }

                failMessage =
                    externalAttempted && externalError != null
                        ? $"Audio transcription failed after external API and Whisper: {externalError.Message}"
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
            finally
            {
                if (System.IO.File.Exists(tempFilePath))
                {
                    try
                    {
                        System.IO.File.Delete(tempFilePath);
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogWarning(deleteEx, "Failed to delete temp file");
                    }
                }
            }
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "Healthy",
                service = "Audio transcription (external + Whisper)",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
