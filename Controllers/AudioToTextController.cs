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

        // ===============================
        // Audio Upload (MediaRecorder)
        // ===============================
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
            // Accept both "audio" and "AudioFile" field names
            var audioFile = audio ?? AudioFile;

            if (audioFile == null || audioFile.Length == 0)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "No audio file uploaded"
                });
            }

            // 🔹 Create uploads/audio directory if it doesn't exist
            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "audio");
            Directory.CreateDirectory(uploadsFolder);

            // 🔹 Save audio file to wwwroot/uploads/audio
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(audioFile.FileName)}";
            var savedFilePath = Path.Combine(uploadsFolder, fileName);

            // 🔹 Also create temp file for OpenAI processing
            var tempFilePath = Path.Combine(
                Path.GetTempPath(),
                $"{Guid.NewGuid()}{Path.GetExtension(audioFile.FileName)}");

            try
            {
                // Save to permanent location
                await using (var fs = new FileStream(savedFilePath, FileMode.Create))
                {
                    await audioFile.CopyToAsync(fs);
                }

                // Copy to temp location for OpenAI processing
                System.IO.File.Copy(savedFilePath, tempFilePath, true);

                _logger.LogInformation(
                    "Audio received: {File}, Size: {Size}, Saved to: {Path}",
                    audioFile.FileName,
                    audioFile.Length,
                    savedFilePath);

                int uid = int.TryParse(userId, out var u) ? u : 0;
                int bid = int.TryParse(bookId, out var b) ? b : 0;
                int chap = chapter ?? 0;

                string text;
                if (_openAIService.IsConfigured)
                {
                    text = await _openAIService.TranscribeAudioAsync(
                        userId ?? "unknown",
                        bookId ?? "unknown",
                        chap,
                        tempFilePath);
                }
                else
                {
                    var extKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
                    if (string.IsNullOrEmpty(extKey))
                    {
                        return StatusCode(500, new
                        {
                            success = false,
                            message = "Voice input is unavailable: neither OpenAI:ApiKey nor ExternalApi:ApiKey is configured on the server."
                        });
                    }

                    var http = _httpClientFactory.CreateClient();
                    http.Timeout = TimeSpan.FromMinutes(5);
                    text = await ExternalBookApiAudio.TranscribeFileAsync(
                        http,
                        _configuration,
                        savedFilePath,
                        uid,
                        bid,
                        chap,
                        HttpContext.RequestAborted);
                }

                if (string.IsNullOrEmpty(text))
                {
                    _logger.LogWarning("Audio transcription returned empty text");
                    return StatusCode(500, new
                    {
                        success = false,
                        message = "Audio transcription returned empty result"
                    });
                }

                return Ok(new
                {
                    success = true,
                    text,
                    audioPath = $"/uploads/audio/{fileName}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio transcription failed");

                // Clean up saved file on error
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

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Audio transcription failed: {ex.Message}"
                });
            }
            finally
            {
                // 🔹 Cleanup temp file
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

        // ===============================
        // Health Check
        // ===============================
        [HttpGet("health")]
        public IActionResult Health()
        {
            return Ok(new
            {
                status = "Healthy",
                service = "OpenAI Audio Transcription",
                timestamp = DateTime.UtcNow
            });
        }
    }
}
