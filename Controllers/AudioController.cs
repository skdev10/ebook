using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http.Headers;
using System.Text;

namespace EBookDashboard.Controllers
{
    [Route("Audio")]
    public class AudioController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public AudioController(IWebHostEnvironment env, IHttpClientFactory factory, IConfiguration configuration)
        {
            _env = env;
            _httpClient = factory.CreateClient();
            _configuration = configuration;
        }

        [HttpPost("Upload")]
        public async Task<IActionResult> Upload(
            IFormFile audio,
            int userId,
            int bookId,
            int chapterId)
        {
            if (audio == null || audio.Length == 0)
                return BadRequest("Audio file is required.");

            var folder = Path.Combine(_env.WebRootPath, "uploads/audio");
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(audio.FileName)}";
            var filePath = Path.Combine(folder, fileName);

            using (var fs = new FileStream(filePath, FileMode.Create))
                await audio.CopyToAsync(fs);

            var text = "";
            try
            {
                text = await SendAudioToTranscriptionAPI(filePath, userId, bookId, chapterId);
            }
            catch (Exception)
            {
                text = "";
            }

            return Json(new { success = true, text });
        }

        private async Task<string> SendAudioToTranscriptionAPI(string audioPath, int userId, int bookId, int chapterId)
        {
            var apiUrl = _configuration["ExternalApi:AudioUrl"] ?? "http://162.229.248.26:8001/api/audio";
            var apiKey = (_configuration["ExternalApi:ApiKey"] ?? "").Trim();
            var extension = Path.GetExtension(audioPath).ToLowerInvariant();
            var mime = extension switch
            {
                ".mp3" => "audio/mpeg",
                ".mp4" => "audio/mp4",
                ".mpeg" => "audio/mpeg",
                ".mpga" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                ".wav" => "audio/wav",
                ".webm" => "audio/webm",
                _ => "audio/mpeg"
            };

            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(userId.ToString()), "user_id");
            form.Add(new StringContent(bookId.ToString()), "book_id");
            form.Add(new StringContent(chapterId.ToString()), "chapter");
            form.Add(new StringContent(audioPath), "audio_file_path");

            var fileBytes = await System.IO.File.ReadAllBytesAsync(audioPath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mime);
            form.Add(fileContent, "audio_file", Path.GetFileName(audioPath));

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            if (!string.IsNullOrEmpty(apiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", apiKey);

            var response = await client.PostAsync(apiUrl, form);
            var json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception("Audio transcription API failed: " + json);

            var obj = JsonConvert.DeserializeObject<dynamic>(json);
            return obj?.text?.ToString() ?? obj?.transcription?.ToString() ?? json ?? "";
        }
    }
}