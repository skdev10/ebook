namespace EBookDashboard.Models.ViewModels
{
    public class AIGenerateBookViewModel
    {
        public AIBookRequest BookRequest { get; set; } = new AIBookRequest();
        public IEnumerable<Plans> AvailablePlans { get; set; } = Enumerable.Empty<Plans>();
        /// <summary>User's books for "Load a previously generated book" dropdown.</summary>
        public List<BookDropdownItem> UserBooks { get; set; } = new List<BookDropdownItem>();
    }

    public class BookDropdownItem
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
    }
}
