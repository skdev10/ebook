using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

public class FrontMatterChapterTests
{
    [Theory]
    [InlineData("Preface", true)]
    [InlineData("PREFACE", true)]
    [InlineData("Preface: Why this book", true)]
    [InlineData("Foreword", true)]
    [InlineData("Dedication", true)]
    [InlineData("Introduction", true)]
    [InlineData("Introduction to Mobile Technology", false)]
    [InlineData("Chapter 1: The Beginning", false)]
    [InlineData("Disadvantages of Technology", false)]
    public void IsFrontMatterSectionTitle_matches_known_sections_only(string title, bool expected)
    {
        Assert.Equal(expected, BookChapterExportHelper.IsFrontMatterSectionTitle(title));
    }

    [Fact]
    public void IsFrontMatter_title_overrides_positive_chapter_number()
    {
        Assert.True(BookChapterExportHelper.IsFrontMatter(1, "Preface"));
        Assert.False(BookChapterExportHelper.IsFrontMatter(1, "The Beginning"));
        Assert.True(BookChapterExportHelper.IsFrontMatter(0, "Anything"));
    }

    [Fact]
    public void DeduplicateFrontMatterChapters_keeps_single_preface_as_chapter_zero()
    {
        var chapters = new List<ChapterDto>
        {
            new() { ChapterNumber = 0, Title = "Preface", Content = "From book creation." },
            new() { ChapterNumber = 1, Title = "Preface", Content = "From import duplicate." },
            new() { ChapterNumber = 2, Title = "Chapter Two", Content = "Story continues." }
        };

        var cleaned = BookChapterExportHelper.DeduplicateFrontMatterChapters(chapters);

        Assert.Equal(2, cleaned.Count);
        Assert.Single(cleaned, c =>
            BookChapterExportHelper.NormalizeFrontMatterTitleKey(c.Title) == "preface");
        var preface = cleaned.First(c =>
            BookChapterExportHelper.NormalizeFrontMatterTitleKey(c.Title) == "preface");
        Assert.Equal(0, preface.ChapterNumber);
        // Both have body — either is fine; must not drop narrative chapter.
        Assert.False(string.IsNullOrWhiteSpace(preface.Content));
        Assert.Contains(cleaned, c => c.Title == "Chapter Two" && c.ChapterNumber == 2);
    }

    [Fact]
    public void DeduplicateFrontMatterChapters_prefers_imported_body_over_empty_notes()
    {
        var cleaned = BookChapterExportHelper.DeduplicateFrontMatterChapters(
        [
            new ChapterDto { ChapterNumber = 0, Title = "Preface", Content = "   " },
            new ChapterDto { ChapterNumber = 1, Title = "Preface", Content = "<p>Real preface from the PDF.</p>" },
            new ChapterDto { ChapterNumber = 1, Title = "Contents", Content = "<h2>Chapter 1</h2>" },
            new ChapterDto { ChapterNumber = 2, Title = "Getting Started", Content = "<p>Chapter one body.</p>" }
        ]);

        Assert.DoesNotContain(cleaned, c => InteriorFrontMatterBuilder.IsImportedContentsChapter(c.Title));
        var preface = Assert.Single(cleaned, c =>
            BookChapterExportHelper.NormalizeFrontMatterTitleKey(c.Title) == "preface");
        Assert.Contains("Real preface", preface.Content, StringComparison.Ordinal);
        Assert.Contains(cleaned, c => c.Title == "Getting Started");
    }

    [Fact]
    public void OrderForExport_preserves_preface_and_first_chapter_when_contents_present()
    {
        var ordered = BookChapterExportHelper.OrderForExport(
        [
            new ChapterDto { ChapterNumber = 0, Title = "Preface", Content = "<p>Lead-in essay.</p>" },
            new ChapterDto { ChapterNumber = 1, Title = "Contents", Content = "<h2>Preface</h2><h2>Chapter 1</h2>" },
            new ChapterDto { ChapterNumber = 2, Title = "Getting Started", Content = "<p>First chapter body.</p>" },
            new ChapterDto { ChapterNumber = 3, Title = "Next Steps", Content = "<p>Second chapter body.</p>" }
        ]);

        Assert.Equal(3, ordered.Count);
        Assert.Equal("Preface", ordered[0].Title);
        Assert.Contains("Lead-in", ordered[0].Content, StringComparison.Ordinal);
        Assert.Equal("Getting Started", ordered[1].Title);
        Assert.Contains("First chapter", ordered[1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public void GetPreviewStyleHeading_preface_is_not_chapter_n()
    {
        var heading = BookChapterExportHelper.GetPreviewStyleHeading("Preface", storageChapterNumber: 1, narrativeOrdinal: 1);
        Assert.Equal("Preface", heading);
        Assert.DoesNotContain("Chapter", heading, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrderForExport_lists_preface_once_before_narrative()
    {
        var ordered = BookChapterExportHelper.OrderForExport(
        [
            new ChapterDto { ChapterNumber = 1, Title = "Preface", Content = "A" },
            new ChapterDto { ChapterNumber = 0, Title = "Preface", Content = "B" },
            new ChapterDto { ChapterNumber = 2, Title = "Dawn", Content = "C" }
        ]);

        Assert.Equal(2, ordered.Count);
        Assert.Equal(0, ordered[0].ChapterNumber);
        Assert.Equal("Preface", ordered[0].Title);
        Assert.Equal("Dawn", ordered[1].Title);
    }
}
