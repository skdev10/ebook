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
    [InlineData("/Books/Writer?bookId=5", BookFlowStateService.StepGenerate)]
    [InlineData("/BookDesign/CoverDesignCalculatorFixing?bookId=5&format=Ebook", BookFlowStateService.StepFormat)]
    [InlineData("/Books/Formatting/5", BookFlowStateService.StepFormat)]
    [InlineData("/Dashboard/CoverDesign?bookId=5", BookFlowStateService.StepCover)]
    [InlineData("/Books/Cover/5", BookFlowStateService.StepCover)]
    [InlineData("/Dashboard/Publish?bookId=5", BookFlowStateService.StepPublish)]
    [InlineData("", "")]
    [InlineData("/Some/Other/Page", "")]
    public void StepFromWorkUrl_maps_known_routes(string url, string expected)
    {
        Assert.Equal(expected, BookFlowStateService.StepFromWorkUrl(url));
    }

    [Fact]
    public void BuildResumeUrl_uses_module_routes()
    {
        using var ctx = CreateContext(nameof(BuildResumeUrl_uses_module_routes));
        var svc = new BookFlowStateService(ctx);
        Assert.Equal("/Books/Writer?bookId=9", svc.BuildResumeUrl(9, BookFlowStateService.StepGenerate, "ebook"));
        Assert.Equal("/Books/Formatting/9", svc.BuildResumeUrl(9, BookFlowStateService.StepFormat, "ebook"));
        Assert.Equal("/Books/Cover/9", svc.BuildResumeUrl(9, BookFlowStateService.StepCover, "ebook"));
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

    [Fact]
    public async Task SaveStepAsync_assigns_unique_setting_ids_on_first_insert()
    {
        await using var ctx = CreateContext(nameof(SaveStepAsync_assigns_unique_setting_ids_on_first_insert));
        var svc = new BookFlowStateService(ctx);

        await svc.SaveStepAsync(42, BookFlowStateService.StepFormat, "ebook");

        var ids = ctx.Settings.Select(s => s.SettingId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.True(ids.Count >= 3);
        var (step, flowPath) = await svc.GetStepAsync(42);
        Assert.Equal(BookFlowStateService.StepFormat, step);
        Assert.Equal("ebook", flowPath);
    }

    [Fact]
    public async Task NextSettingId_returns_1_when_settings_empty()
    {
        await using var ctx = CreateContext(nameof(NextSettingId_returns_1_when_settings_empty));
        Assert.Equal(1, await ctx.NextSettingIdAsync());
    }
}
