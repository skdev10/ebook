using System.Text.RegularExpressions;
using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

/// <summary>
/// The imported HTML for these Zero to One paragraphs must match pdftotext -layout word for word.
/// </summary>
public class PdfImportGroundTruthTests
{
    private const string Investors =
        "companies they work with are by definition average. Most of the differences that investors and entrepreneurs perceive every day are between relative levels of success, not between exponential dominance and failure. And since nobody wants to give up on an investment, VCs usually spend even more time on the most problematic companies than they do on the most obviously successful.";

    private const string Cosmology =
        "by a factor of 1030—a million trillion trillion. As cosmogonic epochs came and went";

    private const string Solar =
        "only 1/π as efficient as flat ones—they simply don't receive as much direct sunlight.";

    [Fact]
    public void Zero_to_one_paragraphs_match_pdftotext_layout()
    {
        var repo = FindRepo();
        var pdf = Path.Combine(repo, "test-corpus", "zero-to-one.pdf");
        Assert.True(File.Exists(pdf), pdf);

        var layout = BookIntegrityChecker.TryExtractLayoutText(pdf);
        Assert.False(string.IsNullOrWhiteSpace(layout), "pdftotext -layout did not run.");
        var layoutFlat = Flat(layout);
        Assert.Contains(Flat(Investors), layoutFlat, StringComparison.Ordinal);
        Assert.Contains(Flat(Cosmology), layoutFlat, StringComparison.Ordinal);
        Assert.Contains(Flat(Solar), layoutFlat, StringComparison.Ordinal);

        var imported = ChapterDocumentImportService.ImportUploadedDocument(File.ReadAllBytes(pdf), ".pdf");
        var html = string.Concat(imported.Chapters.Select(c => c.Body));
        var titles = string.Join("\n", imported.Chapters.Select(c => c.Title));
        var plain = Flat(BookIntegrityChecker.PlainFromHtml(html));

        Assert.Contains(Flat(Investors), plain, StringComparison.Ordinal);
        Assert.Contains(Flat(Cosmology), plain, StringComparison.Ordinal);
        Assert.Contains(Flat(Solar), plain, StringComparison.Ordinal);
        Assert.Contains("<sup>30</sup>", html, StringComparison.Ordinal);
        Assert.Contains("Will They Come?", titles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">COME?</", html, StringComparison.Ordinal);
        Assert.DoesNotContain("COME?COME?", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Come?COME?", plain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EVEN THOUGH", plain, StringComparison.Ordinal);
        Assert.Contains("Peter Thiel", plain, StringComparison.Ordinal);
        Assert.Contains("All rights reserved", plain, StringComparison.Ordinal);
        Assert.Contains("Rising Sun", plain, StringComparison.Ordinal);
        Assert.Contains("trademarks of Random House", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("randomcrown", plain, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Party Like It's 1999", Flat(titles), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1999 Party Like", Flat(titles), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-source-page=\"3\"", html, StringComparison.Ordinal);

        var independent = BookIntegrityChecker.StripLayoutFurniture(layout);
        var importReport = BookIntegrityChecker.Diff(
            BookIntegrityChecker.Tokenize(independent),
            BookIntegrityChecker.Tokenize(plain));
        Assert.True(importReport.Similarity >= 0.99, Describe(importReport));
    }

    private static string Describe(BookIntegrityChecker.Report report) =>
        $"similarity={report.Similarity:P2} deletions={report.Deletions} reorders={report.Reorders} insertions={report.Insertions} " +
        string.Join(" | ", report.Hits.Take(8).Select(h => h.Kind + ":" + h.Context));

    private static string Flat(string? text)
    {
        var s = text ?? "";
        s = s.Replace('\u00a0', ' ').Replace('’', '\'').Replace('‘', '\'').Replace('“', '"').Replace('”', '"');
        s = s.Replace('—', '-').Replace('–', '-');
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string FindRepo()
    {
        var fromEnv = Environment.GetEnvironmentVariable("EBOOK_REPO");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(Path.Combine(fromEnv, "newEbook.csproj")))
            return fromEnv;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "newEbook.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return @"c:\Users\Hp\Desktop\newEbook";
    }
}
