using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Interfaces
{
    public interface IPlanFeaturesService
    {
        Task<IEnumerable<PlanFeatures>> GetAllFeaturesAsync();
        Task AddFeatureAsync(PlanFeatures feature);
        Task UpdateFeatureAsync(PlanFeatures feature);
        Task<int> SaveAuthorPlanFeaturesAsync(int authorId, int userId, string userEmail, List<int> featureIds);
        Task<IEnumerable<AuthorPlanFeatures>> GetAuthorPlanFeaturesAsync(int authorId);
        Task<IEnumerable<AuthorBills>> GetAuthorBillsAsync(int authorId);
        Task<AuthorBills?> GetAuthorBillWithFeaturesAsync(int billId);
       
    }
}
