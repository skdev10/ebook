using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EBookDashboard.Models
{
    [Table("aibookrequestedit")]
    public class AIBookRequestEdit
    {
        [JsonProperty("response_id")]
        [JsonPropertyName("response_id")]
        public string? ResponseId { get; set; } = string.Empty;

        [JsonProperty("user_id")]
        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }

        [JsonProperty("book_id")]
        [JsonPropertyName("book_id")]
        public string? BookId { get; set; }

        [JsonProperty("title")]
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("chapter")]
        [JsonPropertyName("chapter")]
        public int Chapter { get; set; }

        [JsonProperty("changes")]
        [JsonPropertyName("changes")]
        public string Changes { get; set; } = string.Empty;
    }
}
