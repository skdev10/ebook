namespace EBookDashboard.Models.DTO
{
    public class SavedBookDto
    {
        public int ResponseId { get; set; }
        public int UserId { get; set; }
        public int BookId { get; set; }
        public string BookTitle { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }
}
