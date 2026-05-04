namespace EBookDashboard.Models.DTO
{
    public class SavedBookCoverDesignDto
    {
         public int Id { get; set; }
            //public int UserId { get; set; }
            //public int BookId { get; set; }
         public string Title { get; set; } = string.Empty;
         public string Description { get; set; } = string.Empty;
         public string BookCoverPagePath { get; set; } = string.Empty;
     }

}