
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class AuthorService : IAuthorService
    {
        private readonly ApplicationDbContext _context;

        public AuthorService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ----- Author CRUD -----
        public async Task<IEnumerable<Authors>> GetAllAuthorsAsync()
        {
            return await _context.Authors
                                 .Include(a => a.Books)
                                 .ToListAsync();
        }

        public async Task<Authors?> GetAuthorByIdAsync(int id)
        {
            return await _context.Authors
                                 .Include(a => a.Books)
                                 .FirstOrDefaultAsync(a => a.AuthorId == id);
        }

        public async Task<Authors> CreateAuthorAsync(Authors author)
        {
            _context.Authors.Add(author);
            await _context.SaveChangesAsync();
            return author;
        }

        public async Task<bool> UpdateAuthorAsync(Authors author)
        {
            _context.Authors.Update(author);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> DeleteAuthorAsync(int id)
        {
            var author = await _context.Authors.FindAsync(id);
            if (author == null) return false;

            _context.Authors.Remove(author);
            return await _context.SaveChangesAsync() > 0;
        }

        // ----- Author Plans -----
        public async Task<IEnumerable<AuthorPlans>> GetPlansByAuthorAsync(int authorId)
        {
            return await _context.AuthorPlans
                                 .Where(p => p.AuthorId == authorId)
                                 .ToListAsync();
        }

        public async Task<AuthorPlans> AssignPlanToAuthorAsync(AuthorPlans plan)
        {
            _context.AuthorPlans.Add(plan);
            await _context.SaveChangesAsync();
            return plan;
        }

        public async Task<bool> CancelAuthorPlanAsync(int authorPlanId, string reason)
        {
            var plan = await _context.AuthorPlans.FindAsync(authorPlanId);
            if (plan == null) return false;

            plan.CancelledAt = DateTime.UtcNow;
            plan.CancellationReason = reason;
            plan.IsActive = 0;

            _context.AuthorPlans.Update(plan);
            return await _context.SaveChangesAsync() > 0;
        }

        async Task<Authors?> IAuthorService.GetAuthorByIdAsync(int id)
        {
            return await _context.Authors.FindAsync(id);
        }

      
    }
}
