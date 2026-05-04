namespace EBookDashboard.Models.DTO
{
    public class AutoSaveDto
    {
        public int BookId { get; set; }
        public string? Content { get; set; }=string.Empty;
        public string? Title { get; set; } = string.Empty;

    }
}
