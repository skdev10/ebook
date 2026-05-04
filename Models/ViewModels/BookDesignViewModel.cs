namespace EBookDashboard.Models.ViewModels
{
    public class BookDesignViewModel
    {
        public List<BookDesign> Designs { get; set; } = new();
        public int SelectedDesignId { get; set; }

    }
}
