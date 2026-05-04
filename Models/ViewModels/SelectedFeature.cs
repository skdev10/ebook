namespace EBookDashboard.Models.ViewModels
{
    public class SelectedFeature
    {
        public int FeatureId { get; set; }
        public string? FeatureName { get; set; }= string.Empty;
        public decimal FeatureRate { get; set; }
    }
}
