using EBookDashboard.Models;
using EBookDashboard.Interfaces;

namespace EBookDashboard.Interfaces
{
    public interface IAuthorService
    {
        // ----- Author CRUD -----
        Task<IEnumerable<Authors>> GetAllAuthorsAsync();
        Task<Authors?> GetAuthorByIdAsync(int id);
        Task<Authors> CreateAuthorAsync(Authors author);
        Task<bool> UpdateAuthorAsync(Authors author);
        Task<bool> DeleteAuthorAsync(int id);

        // ----- Author Plans -----
        Task<IEnumerable<AuthorPlans>> GetPlansByAuthorAsync(int authorId);
        Task<AuthorPlans> AssignPlanToAuthorAsync(AuthorPlans plan);
        Task<bool> CancelAuthorPlanAsync(int authorPlanId, string reason);
    }
}
