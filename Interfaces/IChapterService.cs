
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

public interface IChapterService
{
    Task<IEnumerable<Chapters>> GetByBookIdAsync(int bookId);
    Task<Chapters> AddAsync(Chapters chapter);
    Task PublishAsync(int chapterId);
}
public class ChapterService : IChapterService
{
    private readonly ApplicationDbContext _context;
    public ChapterService(ApplicationDbContext context) => _context = context;

    public async Task<IEnumerable<Chapters>> GetByBookIdAsync(int bookId) =>
        await _context.Chapters.Where(c => c.BookId == bookId).ToListAsync();

    public async Task<Chapters> AddAsync(Chapters chapter)
    {
        _context.Chapters.Add(chapter);
        await _context.SaveChangesAsync();
        return chapter;
    }

    public async Task PublishAsync(int chapterId)
    {
        var chapter = await _context.Chapters.FindAsync(chapterId);
        if (chapter != null)
        {
            chapter.IsPublished = true;
            chapter.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }
}
