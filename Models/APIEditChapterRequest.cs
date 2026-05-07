using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace EBookDashboard.Models
{
    public class APIEditChapterRequest
    {
        [JsonProperty("user_id")]
        [JsonPropertyName("user_id")]
        public string? UserId { get; set; } = string.Empty;

        [JsonProperty("book_id")]
        [JsonPropertyName("book_id")]
        public string BookId { get; set; } = string.Empty;

        [JsonProperty("chapter")]
        [JsonPropertyName("chapter")]
        public string? Chapter { get; set; } = string.Empty;

        [JsonProperty("changes")]
        [JsonPropertyName("changes")]
        public string? Changes { get; set; } = string.Empty;
    }
}
