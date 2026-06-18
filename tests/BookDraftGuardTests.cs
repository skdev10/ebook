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

    [Fact]
    public async Task FindLatestMeaningfulDraftAsync_skips_near_empty_untitled()
    {
        await using var ctx = CreateContext(nameof(FindLatestMeaningfulDraftAsync_skips_near_empty_untitled));
        ctx.Books.AddRange(
            new Books
            {
                BookId = 1,
                UserId = 10,
                Title = "Untitled Book",
                Status = "Draft",
                WordCount = 0,
                UpdatedAt = DateTime.UtcNow
            },
            new Books
            {
                BookId = 2,
                UserId = 10,
                Title = "My Real Novel",
                Status = "Draft",
                WordCount = 1200,
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            });
        await ctx.SaveChangesAsync();

        var draft = await BookDraftGuard.FindLatestMeaningfulDraftAsync(ctx, 10);
        Assert.NotNull(draft);
        Assert.Equal(2, draft!.BookId);
    }

    [Fact]
    public async Task HasGeneratedManuscriptAsync_false_for_titled_book_without_content()
    {
        await using var ctx = CreateContext(nameof(HasGeneratedManuscriptAsync_false_for_titled_book_without_content));
        ctx.Books.Add(new Books
        {
            BookId = 6,
            UserId = 10,
            Title = "My Novel",
            Status = "Draft",
            WordCount = 0,
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        Assert.False(await BookDraftGuard.HasGeneratedManuscriptAsync(ctx, 10, 6));
    }

    [Fact]
    public async Task HasGeneratedManuscriptAsync_true_when_api_response_exists()
    {
        await using var ctx = CreateContext(nameof(HasGeneratedManuscriptAsync_true_when_api_response_exists));
        ctx.Books.Add(new Books
        {
            BookId = 7,
            UserId = 10,
            Title = "My Novel",
            Status = "Draft",
            WordCount = 0,
            CreatedAt = DateTime.UtcNow
        });
        ctx.APIRawResponse.Add(new APIRawResponse
        {
            UserId = 10,
            BookId = 7,
            ResponseData = "This is generated chapter content from the AI writer.",
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        Assert.True(await BookDraftGuard.HasGeneratedManuscriptAsync(ctx, 10, 7));
    }

    [Fact]
    public async Task PurgeAllNearEmptyUntitledAsync_removes_all_placeholders()
    {
        await using var ctx = CreateContext(nameof(PurgeAllNearEmptyUntitledAsync_removes_all_placeholders));
        ctx.Books.AddRange(
            new Books { BookId = 1, UserId = 10, Title = "Untitled Book", Status = "Draft", WordCount = 0 },
            new Books { BookId = 2, UserId = 10, Title = "Untitled", Status = "Draft", WordCount = 0 },
            new Books { BookId = 3, UserId = 10, Title = "Keep Me", Status = "Draft", WordCount = 500 });
        await ctx.SaveChangesAsync();

        var removed = await BookDraftGuard.PurgeAllNearEmptyUntitledAsync(ctx, 10);
        Assert.Equal(2, removed);
        Assert.Equal(1, await ctx.Books.CountAsync(b => b.UserId == 10));
        Assert.Equal("Keep Me", (await ctx.Books.SingleAsync()).Title);
    }

    [Fact]
    public async Task CreateBookFromRequest_allows_duplicate_real_titles_as_distinct_books()
    {
        await using var ctx = CreateContext(nameof(CreateBookFromRequest_allows_duplicate_real_titles_as_distinct_books));
        var service = new BookService(ctx, chapterIterations: null!, env: null!);

        CreateBookRequest MakeRequest() => new()
        {
            UserId = 10,
            AuthorId = 10,
            CategoryId = 1,
            LanguageId = 1,
            Title = "India vs Pakistan War",
            Status = "Draft"
        };

        var first = await service.CreateBookFromRequestAsync(MakeRequest());
        var second = await service.CreateBookFromRequestAsync(MakeRequest());

        // Same real title is allowed: two separate rows, each with its own BookId.
        Assert.NotEqual(first.BookId, second.BookId);
        Assert.Equal("India vs Pakistan War", first.Title);
        Assert.Equal("India vs Pakistan War", second.Title);
        Assert.Equal(2, await ctx.Books.CountAsync(b => b.UserId == 10 && b.Title == "India vs Pakistan War"));
    }
}
