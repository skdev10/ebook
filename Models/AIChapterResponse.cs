using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("aichapterresponse")]
    public class AIChapterResponse
    {
        [JsonProperty("title")]
        public string? Title { get; set; } = string.Empty;

        [JsonProperty("content")]
        public string? Content { get; set; }= string.Empty;

    }
}
