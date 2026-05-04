
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

public interface IBookPriceService
{
    Task<BookPrice?> GetByBookIdAsync(int bookId);
    Task<BookPrice> AddOrUpdateAsync(BookPrice price);
}
public class BookPriceService : IBookPriceService
{
    private readonly ApplicationDbContext _context;
    public BookPriceService(ApplicationDbContext context) => _context = context;

    public async Task<BookPrice?> GetByBookIdAsync(int bookId) =>
        await _context.BookPrice.FirstOrDefaultAsync(p => p.BookId == bookId);

    public async Task<BookPrice> AddOrUpdateAsync(BookPrice price)
    {
        var existing = await GetByBookIdAsync(price.BookId);
        if (existing == null) _context.BookPrice.Add(price);
        else _context.Entry(existing).CurrentValues.SetValues(price);

        await _context.SaveChangesAsync();
        return price;
    }
}
