namespace EBookDashboard.Models
{
    public class SaveFeaturesRequest
    {
        public int AuthorId { get; set; }
        public int UserId { get; set; }
        public List<int> FeatureIds { get; set; } = new List<int>();
    }
}
