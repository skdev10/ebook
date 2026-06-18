using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EBookDashboard.Tests;

public class BookFlowStateServiceTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Theory]
    [InlineData("/Books/AIGenerateBook?bookId=5", BookFlowStateService.StepGenerate)]
    [InlineData("/BookDesign/CoverDesignCalculatorFixing?bookId=5&format=Ebook", BookFlowStateService.StepFormat)]
    [InlineData("/Dashboard/CoverDesign?bookId=5", BookFlowStateService.StepCover)]
    [InlineData("/Dashboard/Publish?bookId=5", BookFlowStateService.StepPublish)]
    [InlineData("", "")]
    [InlineData("/Some/Other/Page", "")]
    public void StepFromWorkUrl_maps_known_routes(string url, string expected)
    {
        Assert.Equal(expected, BookFlowStateService.StepFromWorkUrl(url));
    }

    [Fact]
    public async Task GetResumeStep_returns_furthest_step_after_soft_back()
    {
        await using var ctx = CreateContext(nameof(GetResumeStep_returns_furthest_step_after_soft_back));
        var svc = new BookFlowStateService(ctx);

        // Move forward to Cover, then soft-back to Format (work preserved).
        await svc.SaveStepAsync(1, BookFlowStateService.StepGenerate);
        await svc.SaveStepAsync(1, BookFlowStateService.StepFormat);
        await svc.SaveStepAsync(1, BookFlowStateService.StepCover);
        await svc.RegressStepAsync(1, BookFlowStateService.StepCover); // -> current becomes format

        var (current, _) = await svc.GetStepAsync(1);
        var (resume, _) = await svc.GetResumeStepAsync(1);

        Assert.Equal(BookFlowStateService.StepFormat, current);   // current regressed
        Assert.Equal(BookFlowStateService.StepCover, resume);     // resume stays at furthest
    }

    [Fact]
    public async Task GetResumeStep_regresses_after_destructive_back()
    {
        await using var ctx = CreateContext(nameof(GetResumeStep_regresses_after_destructive_back));
        var svc = new BookFlowStateService(ctx);

        await svc.SaveStepAsync(1, BookFlowStateService.StepFormat);
        await svc.SaveStepAsync(1, BookFlowStateService.StepCover);
        await svc.RegressAndResetAsync(1, BookFlowStateService.StepCover); // wipe cover work

        var (resume, _) = await svc.GetResumeStepAsync(1);
        Assert.Equal(BookFlowStateService.StepFormat, resume);
    }

    [Fact]
    public async Task GetResumeStep_defaults_to_generate_for_new_book()
    {
        await using var ctx = CreateContext(nameof(GetResumeStep_defaults_to_generate_for_new_book));
        var svc = new BookFlowStateService(ctx);

        var (resume, path) = await svc.GetResumeStepAsync(1);
        Assert.Equal(BookFlowStateService.StepGenerate, resume);
        Assert.Equal("ebook", path);
    }
}
