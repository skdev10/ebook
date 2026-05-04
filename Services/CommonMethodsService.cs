using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    public class CommonMethodsService
    {
        private readonly ApplicationDbContext _context;

        public CommonMethodsService(ApplicationDbContext context)
        {
            _context = context;
        }
        // Make this method public so other services can use it
        public async Task<int> GenerateBookIdAsync(int? userId)
        {
            var today = DateTime.UtcNow;
            string datePart = today.ToString("yyMMdd");

            // Get today's last serial number for this user
            int lastSerialNumber = await GetTodaysLastSerialNumberAsync(userId, today);

            // Increment serial number for new book
            int newSerialNumber = lastSerialNumber + 1;

            // Combine all parts: YearMonthDay + UserId + SerialNumber
            string bookIdString = $"{datePart}{userId ?? 0}{newSerialNumber:D4}";

            return int.Parse(bookIdString);
        }
        // Make this method public so other services can use it
        public async Task<int> GetTodaysLastSerialNumberAsync(int? userId, DateTime today)
        {
            // Get all books created today for this user from APIRawResponse table
            var startOfDay = today.Date;
            var endOfDay = startOfDay.AddDays(1);

            var todaysBooks = await _context.APIRawResponse
                .Where(r => r.UserId == userId &&
                           r.CreatedAt >= startOfDay &&
                           r.CreatedAt < endOfDay &&
                           r.BookId.HasValue)
                .Select(r => r.BookId.Value)
                .ToListAsync();

            if (!todaysBooks.Any())
                return 0;

            // Extract serial numbers from BookIds and find the maximum
            var serialNumbers = todaysBooks
                .Select(bookId =>
                {
                    string bookIdStr = bookId.ToString();
                    if (bookIdStr.Length > 8) // Length of yyyyMMdd
                    {
                        // Extract serial number part (last 4 digits after userId)
                        string serialPart = bookIdStr.Substring(bookIdStr.Length - 4);
                        return int.TryParse(serialPart, out int serial) ? serial : 0;
                    }
                    return 0;
                })
                .Where(serial => serial > 0)
                .ToList();

            return serialNumbers.Any() ? serialNumbers.Max() : 0;
        }
    }
}
