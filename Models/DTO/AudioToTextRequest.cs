namespace EBookDashboard.Models.DTO
{
    public class AudioToTextRequest
    {
        public IFormFile AudioFile { get; set; } = null!;
        public string Language { get; set; } = "en-US";
    }

}
