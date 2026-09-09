using EBookDashboard.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace EBookDashboard.Tests;

public class ChapterTitleSuggesterTests
{
    [Fact]
    public void BuildUpstreamPayload_strips_to_documented_fields_only()
    {
        var highlights = ChapterTitleSuggester.NormalizeHighlights(null, "Artificial Intelligence", "Applications of AI", null);
        var payload = ChapterTitleSuggester.BuildUpstreamPayload("12", "243", highlights);
        Assert.Equal(3, payload.Properties().Count());
        Assert.Equal("12", payload["user_id"]?.ToString());
        Assert.Equal("243", payload["book_id"]?.ToString());
        Assert.Null(payload["book_title"]);
        Assert.Null(payload["topic"]);
        Assert.NotNull(payload["highlights"]);
        var first = (JObject)payload["highlights"]![0]!;
        Assert.Equal("chapter_name", first.Properties().ElementAt(0).Name);
        Assert.Equal("detailed_bullet_summary", first.Properties().ElementAt(1).Name);
        Assert.Equal(2, first.Properties().Count());
    }

    [Fact]
    public void NormalizeHighlights_keeps_client_summary_not_template_examples()
    {
        var highlights = ChapterTitleSuggester.NormalizeHighlights(
            new JArray
            {
                new JObject
                {
                    ["chapter_name"] = "Artificial Intelligence",
                    ["detailed_bullet_summary"] = "Book title: Artificial Intelligence. Suggest five chapter titles about this book."
                }
            },
            "Artificial Intelligence",
            null,
            null);
        var summary = highlights[0]!["detailed_bullet_summary"]!.ToString();
        Assert.Contains("Book title: Artificial Intelligence", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Introduction to", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Importance of", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildLocalSuggestions_uses_book_topic()
    {
        var titles = ChapterTitleSuggester.BuildLocalSuggestions("Artificial Intelligence Foundations", null);
        Assert.Contains(titles, t => t.Contains("Introduction to Artificial Intelligence", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(titles, t => t.Equals("Importance of AI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(titles, t => t.Equals("Applications of AI", StringComparison.OrdinalIgnoreCase));
        Assert.True(titles.Count >= 3);
    }

    [Fact]
    public void FilterRelevant_drops_unrelated_literary_titles()
    {
        var kept = ChapterTitleSuggester.FilterRelevant(
            new[] { "Cartographies of Thought", "Introduction to Artificial Intelligence", "Blueprints for the Unseen" },
            "Artificial Intelligence Foundations",
            null);
        Assert.Contains(kept, t => t.Contains("Artificial Intelligence", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(kept, t => t.Contains("Cartographies", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParseTitles_reads_chapter_titles_array()
    {
        var json = """{"status":"success","data":{"chapter_titles":["The Quiet Arithmetic of Mind","Patterns in the Loom of Logic"]}}""";
        var titles = ChapterTitleSuggester.ParseTitles(json);
        Assert.Equal(2, titles.Count);
        Assert.Equal("The Quiet Arithmetic of Mind", titles[0]);
        Assert.Equal("Patterns in the Loom of Logic", titles[1]);
    }

    [Fact]
    public void ParseTitles_reads_html_list()
    {
        var json = """{"data":{"suggest_chapter_name":"<ul><li>Importance of AI</li><li>Introduction to Artificial Intelligence</li></ul>"}}""";
        var titles = ChapterTitleSuggester.ParseTitles(json);
        Assert.Equal(2, titles.Count);
        Assert.Equal("Importance of AI", titles[0]);
        Assert.Equal("Introduction to Artificial Intelligence", titles[1]);
    }
}
