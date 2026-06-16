using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EBookDashboard.Tests;

public class BookDraftGuardTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Untitled")]
    [InlineData("Untitled Book")]
    public void IsPlaceholderTitle_recognizes_empty_drafts(string? title)
    {
        Assert.True(BookDraftGuard.IsPlaceholderTitle(title));
    }

    [Fact]
    public void IsPlaceholderTitle_rejects_real_titles()
    {
        Assert.False(BookDraftGuard.IsPlaceholderTitle("My Novel"));
    }

    [Fact]
    public async Task IsNearEmptyBookAsync_true_for_placeholder_without_content()
    {
        await using var ctx = CreateContext(nameof(IsNearEmptyBookAsync_true_for_placeholder_without_content));
        ctx.Books.Add(new Books
        {
            BookId = 1,
            UserId = 10,
            Title = "Untitled Book",
            Status = "Draft",
            WordCount = 0,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        Assert.True(await BookDraftGuard.IsNearEmptyBookAsync(ctx, 10, 1));
    }

    [Fact]
    public async Task IsNearEmptyBookAsync_false_when_chapter_has_content()
    {
        await using var ctx = CreateContext(nameof(IsNearEmptyBookAsync_false_when_chapter_has_content));
        ctx.Books.Add(new Books
        {
            BookId = 2,
            UserId = 10,
            Title = "Untitled Book",
            Status = "Draft",
            WordCount = 0,
            CreatedAt = DateTime.UtcNow
        });
        ctx.Chapters.Add(new Chapters
        {
            BookId = 2,
            Title = "Chapter 1",
            Content = "Enough manuscript text here.",
            ChapterNumber = 1
        });
        await ctx.SaveChangesAsync();

        Assert.False(await BookDraftGuard.IsNearEmptyBookAsync(ctx, 10, 2));
    }

    [Fact]
    public async Task FindReusableEmptyUntitledAsync_returns_newest_near_empty_draft()
    {
        await using var ctx = CreateContext(nameof(FindReusableEmptyUntitledAsync_returns_newest_near_empty_draft));
        ctx.Books.AddRange(
            new Books
            {
                BookId = 3,
                UserId = 10,
                Title = "Untitled Book",
                Status = "Draft",
                WordCount = 0,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new Books
            {
                BookId = 4,
                UserId = 10,
                Title = "Untitled",
                Status = "Draft",
                WordCount = 0,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow
            },
            new Books
            {
                BookId = 5,
                UserId = 10,
                Title = "Real Book",
                Status = "Draft",
                WordCount = 0,
                CreatedAt = DateTime.UtcNow
            });
        await ctx.SaveChangesAsync();

        var reusable = await BookDraftGuard.FindReusableEmptyUntitledAsync(ctx, 10);
        Assert.NotNull(reusable);
        Assert.Equal(4, reusable!.BookId);
    }
}
