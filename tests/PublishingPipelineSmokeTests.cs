using System.IO.Compression;
using System.Text;
using EBookDashboard.Configuration;
using EBookDashboard.Models;
using EBookDashboard.Services;
using EBookDashboard.Services.Publishing;
using EBookDashboard.Services.Rendering;
using Xunit;

namespace EBookDashboard.Tests;

public class PublishingPipelineSmokeTests
{
    [Fact]
    public void Epub3_MimetypeFirstUncompressed_AndNavListsChapters()
    {
        var project = new Project { Title = "Smoke Book", AuthorName = "Tester", ProjectType = ProjectType.Ebook };
        var version = new ManuscriptVersion
        {
            Sections =
            [
                new BookSection { OrderIndex = 0, Title = "Chapter One", SectionKind = SectionKind.Chapter, MatterType = MatterType.Body, ContentHtml = "<p>Hello one.</p>" },
                new BookSection { OrderIndex = 1, Title = "Chapter Two", SectionKind = SectionKind.Chapter, MatterType = MatterType.Body, ContentHtml = "<p>Hello two.</p>" }
            ]
        };
        var profile = new LayoutProfile { IsEbookProfile = true, BodyFontFamily = "Georgia", BodyFontSizePt = 12 };
        var bytes = new Epub3Builder().Build(project, version, profile);
        Assert.NotEmpty(bytes);

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);
        var container = zip.GetEntry("META-INF/container.xml");
        Assert.NotNull(container);
        Assert.Equal("META-INF/container.xml", container!.FullName);
        using (var cr = new StreamReader(container.Open(), Encoding.UTF8))
        {
            var containerXml = cr.ReadToEnd();
            Assert.Contains("<rootfile", containerXml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("full-path=\"OEBPS/content.opf\"", containerXml);
        }
        var nav = zip.GetEntry("OEBPS/nav.xhtml");
        Assert.NotNull(nav);
        using var reader = new StreamReader(nav!.Open(), Encoding.UTF8);
        var navHtml = reader.ReadToEnd();
        Assert.Contains("Chapter One", navHtml);
        Assert.Contains("Chapter Two", navHtml);
    }

    [Fact]
    public void BookHtmlBuilder_MirroredGutter_LeftAndRightPages()
    {
        KdpSpecsAccessor.Current = new KdpSpecs();
        var project = new Project
        {
            Title = "Margins",
            TrimWidthIn = 6,
            TrimHeightIn = 9,
            ProjectType = ProjectType.Paperback,
            HasBleed = false
        };
        var profile = new LayoutProfile
        {
            IsEbookProfile = false,
            MarginInsideIn = 0.5,
            MarginOutsideIn = 0.25,
            MarginTopIn = 0.25,
            MarginBottomIn = 0.25
        };
        var version = new ManuscriptVersion
        {
            Sections = [new BookSection { Title = "Ch 1", SectionKind = SectionKind.Chapter, ContentHtml = "<p>Body</p>", OrderIndex = 0 }]
        };
        var calc = new KdpCalculationService(new KdpSpecs());
        var env = new FakeWebHostEnvironment { WebRootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot") };
        var html = new BookHtmlBuilder(calc, env).BuildHtml(project, version, profile);
        // Recto (:right) has gutter (inside) on left; verso (:left) has gutter on right.
        Assert.Contains("@page :right", html);
        Assert.Contains("@page :left", html);
        Assert.Contains("margin-left: 0.5in", html);  // inside on :right
        Assert.Contains("margin-right: 0.5in", html); // inside on :left
    }

    [Fact]
    public void CalculateFullCover_Paperback_6x9_200_MatchesSection2()
    {
        var calc = new KdpCalculationService(new KdpSpecs());
        var spine = calc.CalculateSpineWidthIn(200, PaperType.White, ProjectType.Paperback);
        var (w, h) = calc.CalculateFullCoverSizeIn(6, 9, spine, CoverType.PaperbackWrap);
        Assert.Equal(0.4504, spine, 4);
        Assert.Equal(12.7004, w, 4);
        Assert.Equal(9.25, h, 4);
    }

    [Fact]
    public void PrintPagePoints_6x9_Is432x648()
    {
        // 6in * 72pt = 432; 9in * 72pt = 648
        Assert.Equal(432, (int)Math.Round(6.0 * 72));
        Assert.Equal(648, (int)Math.Round(9.0 * 72));
    }

    private sealed class FakeWebHostEnvironment : Microsoft.AspNetCore.Hosting.IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
