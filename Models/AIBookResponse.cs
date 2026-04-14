using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;
using System.Xml.Linq;

namespace EBookDashboard.Models
{
    [Table("aibookresponse")]
    public class AIBookResponse
    {
        [JsonProperty("title")]
        public string? Title { get; set; }=string.Empty;
        public string Status { get; set; } = "Raw";
		public AIData? Data { get; set; }  // Add this Data property

        [JsonProperty("chapters")]
        public List<AIChapterResponse> Chapters { get; set; } = new();


    }
}

