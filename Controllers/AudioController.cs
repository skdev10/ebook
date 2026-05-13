using EBookDashboard.Services;
using EBookDashboard.Services.BookApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace EBookDashboard.Controllers
{
    [Route("Audio")]
    public class AudioController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public AudioController(IWebHostEnvironment env, IHttpClientFactory factory, IConfiguration configuration)
        {
            _env = env;
            _httpClientFactory = factory;
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

            if (!BookApiInputValidation.IsAllowedAudioExtension(audio.FileName))
                return BadRequest("Unsupported audio type. Allowed: " + string.Join(", ", BookApiConstants.ValidAudioExtensions));

            var folder = Path.Combine(_env.WebRootPath, "uploads/audio");
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(audio.FileName)}";
            var filePath = Path.Combine(folder, fileName);

            using (var fs = new FileStream(filePath, FileMode.Create))
                await audio.CopyToAsync(fs);

            var text = "";
            try
            {
                text = await ExternalBookApiAudio.TranscribeFileAsync(_httpClientFactory, _configuration, filePath, userId, bookId, chapterId);
            }
            catch (Exception)
            {
                text = "";
            }

            return Json(new { success = true, text });
        }
    }
}