namespace EBookDashboard.Models.ViewModels
{
    public class BookDesignVM
    {
        public int BookId { get; set; }
        public int TotalPages { get; set; }
        public List<BookDesign> Designs { get; set; } = new();

    }
}
