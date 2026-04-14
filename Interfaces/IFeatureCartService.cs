using EBookDashboard.Models;

namespace EBookDashboard.Interfaces
{
    public interface IFeatureCartService
    {
        Task<IEnumerable<PlanFeatures>> GetAllFeaturesAsync();
        Task<IEnumerable<AuthorPlanFeatures>> GetTempFeaturesAsync(string sessionId, string userId);
        Task<bool> AddFeatureToTempAsync(string sessionId, string userId, int featureId);
        Task<bool> RemoveFeatureFromTempAsync(string sessionId, string userId, int featureId);
        Task<decimal> CalculateTotalTempPriceAsync(string sessionId, string userId);
        Task<Plans?> SuggestPlanAsync(string sessionId, string userId);
        Task<bool> ConfirmPlanAndMigrateAsync(string sessionId, string userId, int planId);
        Task<bool> ClearTempAsync(string sessionId, string userId);
        Task<bool> HasActivePlanAsync(string userId);
        Task<IEnumerable<AuthorPlanFeatures>> GetUserFeaturesAsync(string userId);
        Dictionary<string, int> GetPlanMap();
    }
}