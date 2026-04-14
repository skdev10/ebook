using Newtonsoft.Json;

namespace EBookDashboard.Models
{
    public class APIFinalizeChapterRequest
    {
        [JsonProperty("user_id")]
        public string? UserId { get; set; } = string.Empty;

        [JsonProperty("book_id")]
        public string? BookId { get; set; } = string.Empty;

        [JsonProperty("chapter")]
        public string? Chapter { get; set; } = string.Empty;

        [JsonProperty("approve")]
        public bool Approve { get; set; } = true;


    }
}
