using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class FeatureCartService : IFeatureCartService
    {
        private readonly ApplicationDbContext _context;

        public FeatureCartService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<PlanFeatures>> GetAllFeaturesAsync()
        {
            return await _context.PlanFeatures
                .Where(f => f.IsActive == 1)
                .OrderBy(f => f.FeatureName)
                .ToListAsync();
        }

        public async Task<IEnumerable<AuthorPlanFeatures>> GetTempFeaturesAsync(string sessionId, string userId)
        {
            // Only return temp-cart items (Status == "temp"), not bill-related records (Status == "Pending")
            int userIdInt = 0;
            int.TryParse(userId, out userIdInt);
            
            return await _context.AuthorPlanFeaturesSet
                .Include(tf => tf.PlanFeature)
                .Where(tf => (tf.UserId == userIdInt || tf.AuthorId == userIdInt) && tf.Status == "temp")
                .ToListAsync();
        }

        public async Task<bool> AddFeatureToTempAsync(string sessionId, string userId, int featureId)
        {
            try
            {
                int userIdInt = 0;
                int.TryParse(userId, out userIdInt);
                
                // Check if feature already exists in temp cart for this user
                var existingTempFeature = await _context.AuthorPlanFeaturesSet
                    .Where(tf => (tf.UserId == userIdInt || tf.AuthorId == userIdInt) && tf.FeatureId == featureId && tf.Status == "temp")
                    .FirstOrDefaultAsync();

                if (existingTempFeature != null)
                {
                    // Feature already in cart, don't add duplicate
                    return true;
                }

                // Check if user already has this feature confirmed
                // For now, we'll skip this check since we don't have a UserFeatures table that matches this model

                // Get the feature details
                var feature = await _context.PlanFeatures
                    .Where(f => f.FeatureId == featureId)
                    .FirstOrDefaultAsync();
                    
                if (feature == null)
                {
                    return false;
                }

                // Add feature to temp cart
                var tempFeature = new AuthorPlanFeatures
                {
                    UserId = userIdInt,
                    AuthorId = userIdInt,
                    FeatureId = featureId,
                    FeatureName = feature.FeatureName,
                    Description = feature.Description,
                    FeatureRate = feature.FeatureRate,
                    Currency = feature.Currency,
                    Status = "temp",
                    IsActive = 1,
                    CreatedAt = DateTime.UtcNow
                };

                _context.AuthorPlanFeaturesSet.Add(tempFeature);
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> RemoveFeatureFromTempAsync(string sessionId, string userId, int featureId)
        {
            try
            {
                int userIdInt = 0;
                int.TryParse(userId, out userIdInt);
                
                var tempFeature = await _context.AuthorPlanFeaturesSet
                    .Where(tf => (tf.UserId == userIdInt || tf.AuthorId == userIdInt) && tf.FeatureId == featureId && tf.Status == "temp")
                    .FirstOrDefaultAsync();

                if (tempFeature != null)
                {
                    _context.AuthorPlanFeaturesSet.Remove(tempFeature);
                    await _context.SaveChangesAsync();
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<decimal> CalculateTotalTempPriceAsync(string sessionId, string userId)
        {
            int userIdInt = 0;
            int.TryParse(userId, out userIdInt);
            
            var tempFeatures = await _context.AuthorPlanFeaturesSet
                .Include(tf => tf.PlanFeature)
                .Where(tf => (tf.UserId == userIdInt || tf.AuthorId == userIdInt) && tf.Status == "temp")
                .ToListAsync();

            return tempFeatures.Sum(tf => tf.FeatureRate);
        }

        public async Task<Plans?> SuggestPlanAsync(string sessionId, string userId)
        {
            int userIdInt = 0;
            int.TryParse(userId, out userIdInt);
            
            var tempFeatures = await GetTempFeaturesAsync(sessionId, userId);
            var featureCount = tempFeatures.Count();

            // Get all plans
            var plans = await _context.Plans.OrderBy(p => p.PlanRate).ToListAsync();

            // Suggest plan based on number of features
            if (featureCount >= 7)
            {
                // Premium plan (most expensive)
                return plans.LastOrDefault();
            }
            else if (featureCount >= 4)
            {
                // Pro plan (middle)
                return plans.Skip(1).FirstOrDefault();
            }
            else if (featureCount >= 1)
            {
                // Basic plan (cheapest, but not free trial)
                return plans.Skip(1).FirstOrDefault() ?? plans.FirstOrDefault(p => p.PlanId != 1);
            }

            return null;
        }

        public async Task<bool> ConfirmPlanAndMigrateAsync(string sessionId, string userId, int planId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                int userIdInt = 0;
                int.TryParse(userId, out userIdInt);
                
                // Get the plan
                var plan = await _context.Plans.FindAsync(planId);
                if (plan == null)
                {
                    return false;
                }

                // Create author plan
                var authorPlan = new AuthorPlans
                {
                    AuthorId = userIdInt,
                    PlanId = planId,
                    StartDate = DateTime.UtcNow,
                    EndDate = DateTime.UtcNow.AddDays(plan.PlanDays),
                    IsActive = 1,
                    TrialUsed = true, // Mark trial as used since they're selecting a paid plan
                    CreatedAt = DateTime.UtcNow
                };

                _context.AuthorPlans.Add(authorPlan);
                await _context.SaveChangesAsync();

                // Get temp features
                var tempFeatures = await _context.AuthorPlanFeaturesSet
                    .Where(tf => (tf.UserId == userIdInt || tf.AuthorId == userIdInt) && tf.Status == "temp")
                    .ToListAsync();

                // Update temp features with the confirmed plan ID
                foreach (var tempFeature in tempFeatures)
                {
                    tempFeature.PlanId = planId;
                    tempFeature.Status = "confirmed";
                    tempFeature.IsActive = 1;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return true;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                return false;
            }
        }

        public async Task<bool> ClearTempAsync(string sessionId, string userId)
        {
            try
            {
                int userIdInt = 0;
                int.TryParse(userId, out userIdInt);
                
                var tempFeatures = await _context.AuthorPlanFeaturesSet
                    .Where(tf => (tf.UserId == userIdInt || tf.AuthorId == userIdInt) && tf.Status == "temp")
                    .ToListAsync();

                _context.AuthorPlanFeaturesSet.RemoveRange(tempFeatures);
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<bool> HasActivePlanAsync(string userId)
        {
            // For now, let's simplify this to just check if the user has any active plans
            // This is a temporary fix to allow login to work
            int userIdInt = 0;
            int.TryParse(userId, out userIdInt);
            
            var activePlan = await _context.AuthorPlans
                .Where(ap => ap.AuthorId == userIdInt && ap.IsActive==1 && ap.EndDate > DateTime.UtcNow)
                .FirstOrDefaultAsync();

            return activePlan != null;
        }

        public async Task<IEnumerable<AuthorPlanFeatures>> GetUserFeaturesAsync(string userId)
        {
            int userIdInt = 0;
            int.TryParse(userId, out userIdInt);
            
            return await _context.AuthorPlanFeaturesSet
                .Include(uf => uf.PlanFeature)
                .Where(uf => (uf.UserId == userIdInt || uf.AuthorId == userIdInt) && uf.Status == "confirmed")
                .ToListAsync();
        }

        public Dictionary<string, int> GetPlanMap()
        {
            // Return a dictionary mapping plan names to IDs
            return new Dictionary<string, int>
            {
                { "Basic Plan", 2 },
                { "Pro Plan", 3 },
                { "Premium Plan", 4 }
            };
        }
    }
}