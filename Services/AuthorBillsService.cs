using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class AuthorBillsService : IAuthorBillsService
    {
        private readonly ApplicationDbContext _context;

        public AuthorBillsService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<AuthorBills?> GetBillByIdAsync(int billId)
        {
            return await _context.AuthorBills
                .Include(b => b.AuthorPlanFeatures)
                    .ThenInclude(apf => apf.PlanFeature)
                .FirstOrDefaultAsync(b => b.BillId == billId && b.IsActive==1);
        }

        public async Task<AuthorBills?> GetRecentBillByUserAsync(int userId, string userEmail)
        {
            var today = DateTime.Today;

            return await _context.AuthorBills
                .Include(b => b.AuthorPlanFeatures)
                    .ThenInclude(apf => apf.PlanFeature)
                .Where(b => b.UserId == userId &&
                           b.UserEmail == userEmail &&
                           b.CreatedAt.Date == today &&
                           b.IsActive == 1)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<AuthorBills>> GetBillsByAuthorAsync(int authorId)
        {
            return await _context.AuthorBills
                .Include(b => b.AuthorPlanFeatures)
                    .ThenInclude(apf => apf.PlanFeature)
                .Where(b => b.AuthorId == authorId && b.IsActive == 1)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }

        // Optional: Get all bills for an author
        public async Task<IEnumerable<AuthorBills>> GetAuthorBillsAsync(int authorId)
        {
            return await _context.AuthorBills
                .Where(b => b.AuthorId == authorId && b.IsActive == 1)
                .Include(b => b.AuthorPlanFeatures)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
        }
        // Optional: Get bill with features
        public async Task<AuthorBills> GetAuthorBillWithFeaturesAsync(int billId)
        {
            var bill = await _context.AuthorBills
             .Include(b => b.AuthorPlanFeatures)
             .ThenInclude(apf => apf.PlanFeature)
             .FirstOrDefaultAsync(b => b.BillId == billId && b.IsActive == 1);

            return bill ?? throw new Exception($"Bill with ID {billId} not found");
        }


        public async Task<bool> UpdateBillPaymentStatusAsync(int billId, string paymentReference, string status)
        {
            var bill = await GetBillByIdAsync(billId);
            if (bill == null) return false;

            bill.PaymentReference = paymentReference;
            bill.Status = status;

            if (status == "Paid")
            {
                bill.ClosingDate = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> CancelBillAsync(int billId, string cancellationReason)
        {
            var bill = await GetBillByIdAsync(billId);
            if (bill == null) return false;

            bill.Status = "Cancelled";
            bill.CancellationReason = cancellationReason;
            bill.CancelledAt = DateTime.Now;
            bill.IsActive = 0;

            await _context.SaveChangesAsync();
            return true;
        }
    }
}