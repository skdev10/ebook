using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations.Schema;

namespace EBookDashboard.Models
{
    [Table("apibookresponse")]
    public class APIBookResponse
    {
        [JsonProperty("status")]
        public string? Status { get; set; } = string.Empty;


        [JsonProperty("title")]
        public string? Title { get; set; } = string.Empty;

        [JsonProperty("data")]
        public AIData? Data { get; set; }  // Add this Data property
    }
}
