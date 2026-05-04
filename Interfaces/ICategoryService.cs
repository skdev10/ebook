
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

public interface ICategoryService
{
    Task<IEnumerable<Categories>> GetAllAsync();
}
public class CategoryService : ICategoryService
{
    private readonly ApplicationDbContext _context;
    public CategoryService(ApplicationDbContext context) => _context = context;

    public async Task<IEnumerable<Categories>> GetAllAsync() =>
        await _context.Categories.ToListAsync();
}
