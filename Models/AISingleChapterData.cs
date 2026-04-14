using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("aisinglechapterdata")]
    public class AISingleChapterData
    {
        [JsonProperty("style")]
        public string? Style { get; set; } = string.Empty;


        [JsonProperty("suggest_chapter_name")]
        public string? Title { get; set; } = string.Empty;


        [JsonProperty("content")]
        public string? Content { get; set; } = string.Empty;


        [JsonProperty("highlights")]
        public string? Highlights { get; set; } = string.Empty;

    }
}
