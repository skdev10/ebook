using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.DTO;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class BookDesignService: IBookDesignService
    {
        private readonly ApplicationDbContext _context;
        public BookDesignService(ApplicationDbContext context)
        {
            _context = context;
        }
    
        //=======================================
        // Get a single saved API response for a user and book
        //====================================
        public async Task<CoverDesignCalculator?> GetCoverDesignCalculator(int userId, int bookId)
        {
            // APIRawResponse table

            return await _context.coverDesignCalculator
                .Where(r => r.UserId == userId && r.BookId == bookId && r.isActive)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync();
        }
        //==========================================
        //   Get all saved books for a user
        //==========================================
        public async Task<IEnumerable<SavedBookCoverDesignDto>> GetSavedBooksCoversForDropdownAsync()
        {
            //if (userId == 0)
            //    throw new ArgumentException("UserId is required.");

            var query = _context.BookCoverPages;
            //    .Where(b => b.UserId == userId);

            //// Apply BookId filter ONLY if bookId != 0
            //if (bookId != 0)
            //{
            //    query = query.Where(b => b.BookId == bookId);
            //}

            return await query
                .Select(b => new SavedBookCoverDesignDto
                {
                    //UserId = b.UserId,
                    //BookId = b.BookId,
                    Title = b.Title,
                    Description=b.Description,
                    BookCoverPagePath=b.BookCoverPagePath
                })
                .ToListAsync();
        }
    }
}
