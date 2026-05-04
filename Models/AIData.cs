using Newtonsoft.Json;

namespace EBookDashboard.Models
{
    public class AIData
    {
        [JsonProperty("style")]
        public string? Style { get; set; } = string.Empty;

        [JsonProperty("suggest_chapter_name")]
        public string? SuggestChapterName { get; set; } = string.Empty;


        [JsonProperty("content")]
        public string? Content { get; set; }=string.Empty;

        [JsonProperty("highlights")]
        public string? Highlights { get; set; } = string.Empty;

    }
}
