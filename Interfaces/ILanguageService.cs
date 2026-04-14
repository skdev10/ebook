
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

public interface ILanguageService
{
    Task<IEnumerable<Language>> GetActiveLanguagesAsync();
}
public class LanguageService : ILanguageService
{
    private readonly ApplicationDbContext _context;
    public LanguageService(ApplicationDbContext context) => _context = context;

    public async Task<IEnumerable<Language>> GetActiveLanguagesAsync() =>
    await _context.Languages
                  .Where(l => l.IsActive) // no need to compare with "true"
                  .ToListAsync();

}
