namespace EBookDashboard.Models
{
    public class APISaveBookRequest
    {
        public string? UserId { get; set; }
        public string? BookId { get; set; }
        public string ApiRaw { get; set; } = string.Empty;

    }
}
