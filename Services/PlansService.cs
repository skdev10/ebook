using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class PlansService : IPlansService
    {
        private readonly ApplicationDbContext _context;

        public PlansService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> CanPublishAsync(int planId, int currentEBooks)
        {
            var plan = await _context.Plans.FindAsync(planId);
            return plan != null && currentEBooks < plan.MaxEBooks;
        }

        public async Task<Plans?> GetPlanByIdAsync(int planId)
        {
            return await _context.Plans
                .FirstOrDefaultAsync(p => p.PlanId == planId && p.IsActive == 1);
        }

        public async Task<IEnumerable<Plans>> GetAllActivePlansAsync()
        {
            return await _context.Plans
                .Where(p => p.IsActive == 1)
                .OrderBy(p => p.PlanId)
                .ToListAsync();
        }

        // Additional Plans-specific methods
        public async Task<bool> CreatePlanAsync(Plans plan)
        {
            _context.Plans.Add(plan);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> UpdatePlanAsync(Plans plan)
        {
            _context.Plans.Update(plan);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeactivatePlanAsync(int planId)
        {
            var plan = await GetPlanByIdAsync(planId);
            if (plan != null)
            {
                plan.IsActive = 0;
                await _context.SaveChangesAsync();
                return true;
            }
            return false;
        }

        public async Task<int> CreateAuthorPlanAsync(int authorId, int userId, int planId)
        {
            return await Task.FromResult(1);
        }

        public Task<string?> GetActivePlansAsync()
        {
            return Task.FromResult<string?>("[]");
        }
    }
}