using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class PlanFeaturesService : IPlanFeaturesService
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuthorBillsService _authorBillsService;
        public PlanFeaturesService(ApplicationDbContext context, IAuthorBillsService authorBillsService)
        {
            _context = context;
            _authorBillsService = authorBillsService;
        }
        // ----- Features -----
        public async Task<IEnumerable<PlanFeatures>> GetAllFeaturesAsync()
        {
            // Return all features (active and inactive) for complete admin oversight
            return await _context.PlanFeatures
                .OrderBy(f => f.FeatureId)
                .ToListAsync();
        }
        public async Task AddFeatureAsync(PlanFeatures feature)
        {
            _context.PlanFeatures.Add(feature);
            await _context.SaveChangesAsync();  // this actually commits to DB
        }
        public async Task UpdateFeatureAsync(PlanFeatures feature)
        {
            var existing = await _context.PlanFeatures.FindAsync(feature.FeatureId);
            if (existing == null) return;
            existing.FeatureName = feature.FeatureName;
            existing.Description = feature.Description;
            existing.FeatureRate = feature.FeatureRate;
            existing.UsageLimit = feature.UsageLimit;
            existing.Currency = feature.Currency;
            existing.FeatureType = feature.FeatureType;
            existing.Status = feature.Status;
            existing.IsActive = feature.IsActive;
            existing.IsUnlimited = feature.IsUnlimited;
            if (feature.PlanId.HasValue) existing.PlanId = feature.PlanId;
            await _context.SaveChangesAsync();
        }
        // ----- Author Plan Features -----
        public async Task<int> SaveAuthorPlanFeaturesAsync(int authorId, int userId, string userEmail, List<int> featureIds)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Get feature details from database
                var features = await _context.PlanFeatures
                    .Where(f => featureIds.Contains(f.FeatureId) && f.IsActive == 1)
                    .ToListAsync();

                if (!features.Any())
                {
                    throw new Exception("No valid features found");
                }

                // Calculate subtotal, tax (10%), and grand total
                var subtotal = features.Sum(f => f.FeatureRate);
                var taxAmount = Math.Round(subtotal * 0.10m, 2);
                var grandTotal = subtotal + taxAmount - 0m; // Discount = 0 for now
                var currency = features.First().Currency ?? "usd";
                
                // 1. Create AuthorBill (Master record) - TotalAmount = grand total (subtotal + tax) for Stripe
                var authorBill = new AuthorBills
                {
                    AuthorId = authorId,
                    UserId = userId,
                    UserEmail = userEmail,
                    Description = $"Plan Features Purchase - {features.Count} features",
                    CreatedAt = DateTime.Now,
                    Currency = currency,
                    Discount = 0,
                    TotalAmount = grandTotal,
                    TaxAmount = taxAmount,
                    PaymentReference = string.Empty,
                    CancelledAt = new DateTime(1990, 1, 1),
                    CancellationReason = null,
                    Status = "Pending",
                    ClosingDate = DateTime.Now.AddDays(30), // 30 days validity
                    IsActive = 1
                };

                await _context.AuthorBills.AddAsync(authorBill);
                await _context.SaveChangesAsync(); // Save to get BillId

                // 2. Create AuthorPlanFeatures records (Detail records)
                var authorPlanFeatures = new List<AuthorPlanFeatures>();

                foreach (var feature in features)
                {
                    var authorPlanFeature = new AuthorPlanFeatures
                    {
                        BillId = authorBill.BillId, // Set the foreign key
                        AuthorId = authorId,
                        UserId = userId,
                        UserEmail = userEmail,
                        FeatureId = feature.FeatureId,
                        PlanId = feature.PlanId,
                        FeatureName = feature.FeatureName,
                        Description = feature.Description,
                        FeatureRate = feature.FeatureRate,
                        Currency = feature.Currency,
                        TotalAmount = grandTotal,
                        CreatedAt = DateTime.Now,  
                        Status = "Pending",
                        IsActive = 1
                    };

                    authorPlanFeatures.Add(authorPlanFeature);
                   // totalAmount += feature.FeatureRate;
                }

                // If you want to store total amount in each record
                //foreach (var feature in authorPlanFeatures)
                //{
                //    feature.TotalAmount = totalAmount;
                //}

                await _context.AuthorPlanFeaturesSet.AddRangeAsync(authorPlanFeatures);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return authorBill.BillId; // Return the generated BillId
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // Optional: Get saved author features
        public async Task<IEnumerable<AuthorPlanFeatures>> GetAuthorPlanFeaturesAsync(int authorId)
        {
            return await _context.AuthorPlanFeaturesSet
                .Where(apf => apf.AuthorId == authorId && apf.IsActive == 1)
                .Include(apf => apf.PlanFeature)
                .Include(apf => apf.AuthorBill) // Include the bill details
                .OrderByDescending(apf => apf.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<AuthorBills>> GetAuthorBillsAsync(int authorId)
        {
            return await _context.AuthorBills.Where(b => b.AuthorId == authorId).ToListAsync();
        }

        public async Task<AuthorBills?> GetAuthorBillWithFeaturesAsync(int billId)
        {
            return await _context.AuthorBills.FindAsync(billId);
        }
    }
}
