
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

public interface IPlansService
{
    Task<Plans?> GetPlanByIdAsync(int planId);
    Task<IEnumerable<Plans>> GetAllActivePlansAsync();
    Task<int> CreateAuthorPlanAsync(int authorId, int userId, int planId);
    Task<string?> GetActivePlansAsync();
}

