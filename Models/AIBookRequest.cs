using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("aibookrequest")]
    public class AIBookRequest
    {
        [JsonProperty("response_id")]
        public string? ResponseId { get; set; } = string.Empty;

        [JsonProperty("user_id")]
        public string? UserId { get; set; }

        [JsonProperty("book_id")]
        public string? BookId { get; set; } 

        [JsonProperty("title")]
        public string Title { get; set; }= string.Empty;


        [JsonProperty("chapter")]
        public int Chapter { get; set; }   // 👈 Should be INT (not string)

        [JsonProperty("user_input")]
        public string UserInput { get; set; } = string.Empty;

        /// <summary>When true, generate API returns content only and does not save to database. Use with Finalize to save.</summary>
        [JsonProperty("preview_only")]
        public bool PreviewOnly { get; set; }
    }
}
