namespace EBookDashboard.Models.ViewModels
{
    public class BookCoversViewModel
    {
        public List<string> Categories { get; set; } = new();
        public string? SelectedCategory { get; set; }
        public List<string> ImagePaths { get; set; } = new();

    }
}
