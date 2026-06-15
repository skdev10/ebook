using EBookDashboard.Models;
using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

public class BookGenerationTests
{
    [Fact]
    public void BuildBookPrompt_fills_all_placeholders()
    {
        var request = new BookRequest
        {
            BookTitle = "Azad Kashmir",
            AuthorName = "Saad",
            Language = "Roman Urdu",
            ChaptersCount = 10,
            MinWordsPerChapter = 1500,
            ToneDescription = "emotional, descriptive, storytelling"
        };

        var prompt = PromptTemplates.BuildBookPrompt(request);

        Assert.Contains("Azad Kashmir", prompt);
        Assert.Contains("Saad", prompt);
        Assert.Contains("Roman Urdu", prompt);
        Assert.Contains("10", prompt);
        Assert.Contains("1500", prompt);
        Assert.Contains("emotional, descriptive, storytelling", prompt);
        Assert.DoesNotContain("{BookTitle}", prompt);
        Assert.DoesNotContain("{AuthorName}", prompt);
        Assert.DoesNotContain("{ChaptersCount}", prompt);
    }

    [Fact]
    public void Sanitize_strips_disallowed_tags_and_attributes()
    {
        const string raw = """
            ```html
            <div class="x" style="color:red">
            <h1>Title</h1>
            <p id="p1">Hello <span class="y">world</span></p>
            </div>
            ```
            """;

        var clean = BookGenerationHtmlSanitizer.Sanitize(raw);

        Assert.Contains("<h1>", clean);
        Assert.Contains("<p>", clean);
        Assert.DoesNotContain("<div", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<span", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("class=", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("style=", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("```", clean);
    }

    [Fact]
    public void Parser_splits_chapters_by_h2()
    {
        const string html = """
            <h1>Azad Kashmir</h1>
            <p>by Saad</p>
            <h2>Chapter 1 – Watan</h2>
            <p>Pehla paragraph.</p>
            <hr>
            <h2>Chapter 2 – Safar</h2>
            <p>Doosra paragraph.</p>
            <hr>
            """;

        var chapters = BookContentHtmlParser.ToChapterDtos(html);

        Assert.Equal(2, chapters.Count);
        Assert.Equal("Chapter 1 – Watan", chapters[0].Title);
        Assert.Equal("Chapter 2 – Safar", chapters[1].Title);
        Assert.Contains("Pehla paragraph", chapters[0].Content);
        Assert.Contains("Doosra paragraph", chapters[1].Content);
    }
}
