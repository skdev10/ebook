namespace EBookDashboard.Models.DTO
{
    public class UpdatePlanFeatureRequest
    {
        public int FeatureId { get; set; }
        public string? FeatureName { get; set; }
        public string? Description { get; set; }
        public decimal FeatureRate { get; set; }
        public string? Currency { get; set; }
        public string? Status { get; set; }
        public int IsActive { get; set; } = 1;
        public int? PlanId { get; set; }
        public string? UsageLimit { get; set; }
        public string? FeatureType { get; set; }
        public bool IsUnlimited { get; set; } = true;
    }
}
