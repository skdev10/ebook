using EBookDashboard.Models;

namespace EBookDashboard.Interfaces
{
    public interface IAuthorPlansService
    {
        Task<int> CreateAuthorPlanAsync(int authorId, int userId, int planId);
        Task<AuthorPlans?> GetAuthorPlanByIdAsync(int authorPlanId);
        Task<IEnumerable<AuthorPlans>> GetAuthorPlansByAuthorIdAsync(int authorId);
        Task<bool> UpdateAuthorPlanPaymentAsync(int authorPlanId, string paymentReference);
    }
}