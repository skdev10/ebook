namespace EBookDashboard.Models
{
    public class AudioToTextResponse
    {
        public bool Success { get; set; }
        public string Text { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public double Duration { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<WordDetail> WordDetails { get; set; } = new();

    }
}
