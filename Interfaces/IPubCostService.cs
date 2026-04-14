
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

public interface IPubCostService
{
    Task<decimal> GetTotalCostByAuthorAsync(int authorId);
}
public class PubCostService : IPubCostService
{
    private readonly ApplicationDbContext _context;
    public PubCostService(ApplicationDbContext context) => _context = context;

    public async Task<decimal> GetTotalCostByAuthorAsync(int authorId) =>
        await _context.PubCosts
                      .Where(p => p.AuthorId == authorId)
                      .SumAsync(p => p.Amount);
}
