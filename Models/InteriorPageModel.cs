namespace EBookDashboard.Models;

/// <summary>View model for <c>Views/Shared/_InteriorPage.cshtml</c> — mirrors <see cref="Services.InteriorPageMarkup"/>.</summary>
public sealed class InteriorPageModel
{
    public int SectionId { get; init; }
    public string RunningHeadTitle { get; init; } = "";
    public string TitleHtml { get; init; } = "";
    public string BodyHtml { get; init; } = "";
    public string BodyClass { get; init; } = "reader-page-body";
}
