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

        /// <summary>Book title from Writer (not the chapter title in <see cref="Title"/>).</summary>
        [JsonProperty("book_title")]
        [NotMapped]
        public string? BookTitle { get; set; }


        [JsonProperty("chapter")]
        public int Chapter { get; set; }   // 👈 Should be INT (not string)

        [JsonProperty("user_input")]
        public string UserInput { get; set; } = string.Empty;

        /// <summary>Short user brief only (UI). When set, takes precedence over <see cref="UserInput"/> for storage.</summary>
        [JsonProperty("chapter_topic")]
        public string ChapterTopic { get; set; } = string.Empty;

        /// <summary>When true, generate API returns content only and does not save to database. Use with Finalize to save.</summary>
        [JsonProperty("preview_only")]
        public bool PreviewOnly { get; set; }
    }
}
