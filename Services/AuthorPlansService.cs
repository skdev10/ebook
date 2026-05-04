using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class AuthorPlansService : IAuthorPlansService
    {
        private readonly ApplicationDbContext _context;
        private readonly IPlansService _plansService;

        public AuthorPlansService(ApplicationDbContext context, IPlansService plansService)
        {
            _context = context;
            _plansService = plansService;
        }

        public async Task<int> CreateAuthorPlanAsync(int authorId, int userId, int planId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var planDetails = await _plansService.GetPlanByIdAsync(planId);
                if (planDetails == null)
                {
                    throw new Exception("Plan not found");
                }

                var authorPlan = new AuthorPlans
                {
                    AuthorId = authorId,
                    UserId = userId,
                    PlanId = planDetails.PlanId,
                    PlanName = planDetails.PlanName,
                    PlanDescription = planDetails.PlanDescription,
                    PlanRate = planDetails.PlanRate,
                    PlanDays = planDetails.PlanDays,
                    PlanHours = 0,
                    MaxEBooks = planDetails.MaxEBooks,
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(planDetails.PlanDays),
                    IsActive = 1,
                    TrialUsed = false,
                    PaymentReference = null
                };

                _context.AuthorPlans.Add(authorPlan);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return authorPlan.AuthorPlanId;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<AuthorPlans?> GetAuthorPlanByIdAsync(int authorPlanId)
        {
            return await _context.AuthorPlans
                .Include(ap => ap.Plan) // Include plan details if needed
                .FirstOrDefaultAsync(ap => ap.AuthorPlanId == authorPlanId && ap.IsActive == 1);
        }

        public async Task<IEnumerable<AuthorPlans>> GetAuthorPlansByAuthorIdAsync(int authorId)
        {
            return await _context.AuthorPlans
                .Where(ap => ap.AuthorId == authorId && ap.IsActive == 1)
                .Include(ap => ap.Plan)
                .OrderByDescending(ap => ap.StartDate)
                .ToListAsync();
        }

        public async Task<bool> UpdateAuthorPlanPaymentAsync(int authorPlanId, string paymentReference)
        {
            var authorPlan = await GetAuthorPlanByIdAsync(authorPlanId);
            if (authorPlan != null)
            {
                authorPlan.PaymentReference = paymentReference;
                await _context.SaveChangesAsync();
                return true;
            }
            return false;
        }
    }
}
