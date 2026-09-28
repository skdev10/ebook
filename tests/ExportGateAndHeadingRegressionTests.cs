using EBookDashboard.Models.Options;
using EBookDashboard.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EBookDashboard.Tests;

/// <summary>Regression guards for free download + heading/page-break parity (QA hotspots).</summary>
public class ExportGateAndHeadingRegressionTests
{
    [Fact]
    public void BookPaymentOptions_defaults_to_free_export()
    {
        var opts = new BookPaymentOptions();
        Assert.False(opts.RequirePaymentForExport);
    }

    [Fact]
    public void Appsettings_json_keeps_export_free_until_stripe()
    {
        var root = FindRepoRoot();
        var cfg = new ConfigurationBuilder()
            .SetBasePath(root)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();

        Assert.False(cfg.GetValue<bool>("BookPayment:RequirePaymentForExport"));
        Assert.False(cfg.GetSection("BookPayment").GetValue<bool>("RequirePaymentForExport"));
    }

    [Fact]
    public void BookInteriorSharedCss_does_not_force_subsection_onto_own_page()
    {
        var cssPath = Path.Combine(FindRepoRoot(), "wwwroot", "css", "book-interior-shared.css");
        Assert.True(File.Exists(cssPath), cssPath);
        var css = File.ReadAllText(cssPath);

        Assert.Contains("manuscript-keep-next", css, StringComparison.Ordinal);
        Assert.Contains("break-before: auto", css, StringComparison.Ordinal);
        Assert.Contains("page-break-before: avoid", css, StringComparison.Ordinal);
        // Old bug: every h2 forced onto a fresh page (often alone).
        Assert.DoesNotContain(
            ".manuscript-root .manuscript-h2,\n[data-interior-mode=\"print\"] .manuscript-root h2.manuscript-heading {\n    break-before: page;",
            css);
        Assert.DoesNotContain("break-before: page;\n    page-break-before: always;\n    break-after: avoid;\n    page-break-after: avoid;\n}", css);
    }

    [Fact]
    public void BookReaderPaginationJs_exports_merge_heading_only_helper()
    {
        var jsPath = Path.Combine(FindRepoRoot(), "wwwroot", "js", "book-reader-pagination.js");
        Assert.True(File.Exists(jsPath), jsPath);
        var js = File.ReadAllText(jsPath);
        Assert.Contains("mergeHeadingOnlyChapterBlockPages", js, StringComparison.Ordinal);
        Assert.Contains("isHeadingOnlyPage", js, StringComparison.Ordinal);
        Assert.Contains("Never leave a page that only contains a heading", js, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Small", "10", "13.33")]
    [InlineData("Medium", "11", "14.67")]
    [InlineData("Large", "12", "16")]
    public void Classic_pt_to_px_parity(string size, string expectedPt, string expectedPx)
    {
        var ty = InteriorTypographyPresets.Resolve(new Models.DTO.BookPdfExportOptions
        {
            InteriorStyle = "Classic",
            TextSize = size,
            LineSpacing = "1.6"
        });
        Assert.Equal(expectedPt, ty.BodyFontSizePt);
        Assert.Equal(expectedPx, ty.BodyFontSizePx);
    }

    private static string FindRepoRoot([System.Runtime.CompilerServices.CallerFilePath] string? thisFile = null)
    {
        // xUnit runs with cwd = redirected msbuild-out; resolve via this source file under /tests.
        var testsDir = Path.GetDirectoryName(thisFile ?? "") ?? "";
        var root = Path.GetFullPath(Path.Combine(testsDir, ".."));
        if (File.Exists(Path.Combine(root, "newEbook.csproj")))
            return root;

        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(Path.GetFullPath(start));
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "newEbook.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate repo root (newEbook.csproj). thisFile=" + thisFile
            + " cwd=" + Directory.GetCurrentDirectory()
            + " base=" + AppContext.BaseDirectory);
    }
}
