using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EBookDashboard.Tests;

public class ChapterGeneratePersistsToChaptersTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task PromoteAsCurrentVersion_writes_draft_row_into_chapters_table()
    {
        await using var ctx = CreateContext(nameof(PromoteAsCurrentVersion_writes_draft_row_into_chapters_table));
        ctx.Books.Add(new Books
        {
            BookId = 41,
            UserId = 7,
            Title = "Generated book",
            Status = "Draft",
            CreatedAt = DateTime.UtcNow
        });
        ctx.APIRawResponse.Add(new APIRawResponse
        {
            ResponseId = 9001,
            Endpoint = "generate",
            Chapter = 2,
            Title = "The River",
            RequestData = "{}",
            ResponseData = "{\"data\":{\"content\":\"River chapter body.\"}}",
            UserId = 7,
            BookId = 41,
            StatusCode = "OK",
            Content = "River chapter body.",
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var svc = new ChapterIterationService(ctx, NullLogger<ChapterIterationService>.Instance);
        await svc.RecordSuccessfulGenerationAsync(9001);
        var ok = await svc.PromoteAsCurrentVersionAsync(7, 41, 2, 9001);

        Assert.True(ok);
        var row = await ctx.Chapters.SingleAsync(c => c.BookId == 41 && c.ChapterNumber == 2);
        Assert.Equal("The River", row.Title);
        Assert.Equal("River chapter body.", row.Content);
        Assert.Equal("Draft", row.Status);
        Assert.True(row.WordCount > 0);
    }

    [Fact]
    public async Task Finalize_upgrades_same_chapter_to_readonly()
    {
        await using var ctx = CreateContext(nameof(Finalize_upgrades_same_chapter_to_readonly));
        ctx.Books.Add(new Books
        {
            BookId = 42,
            UserId = 7,
            Title = "Generated book",
            Status = "Draft",
            CreatedAt = DateTime.UtcNow
        });
        ctx.APIRawResponse.Add(new APIRawResponse
        {
            ResponseId = 9002,
            Endpoint = "generate",
            Chapter = 1,
            Title = "Opening",
            RequestData = "{}",
            ResponseData = "{}",
            UserId = 7,
            BookId = 42,
            StatusCode = "OK",
            Content = "Opening body.",
            CreatedAt = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var svc = new ChapterIterationService(ctx, NullLogger<ChapterIterationService>.Instance);
        await svc.RecordSuccessfulGenerationAsync(9002);
        await svc.PromoteAsCurrentVersionAsync(7, 42, 1, 9002);
        var finalized = await svc.FinalizeByResponseIdAsync(7, 42, 1, 9002);

        Assert.True(finalized);
        var row = await ctx.Chapters.SingleAsync(c => c.BookId == 42 && c.ChapterNumber == 1);
        Assert.Equal("ReadOnly", row.Status);
        Assert.Equal("Opening body.", row.Content);
    }
}
