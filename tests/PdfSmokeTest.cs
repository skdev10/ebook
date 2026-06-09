using EBookDashboard.Models.DTO;
using EBookDashboard.Services;
using EBookDashboard.Services.PdfExport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EBookDashboard.Tests;

public class BookPdfSmokeTests
{
    [Fact]
    public async Task RenderFullBookPdf_produces_valid_pdf_header()
    {
        var env = new StubEnv();
        var cfg = new ConfigurationBuilder().Build();
        var resolver = new PdfHtmlExportServiceResolver(cfg, NullLoggerFactory.Instance);
        var svc = new BookPdfService(env, NullLogger<BookPdfService>.Instance, cfg, resolver);
        var details = new BookDetailsResponseDto
        {
            Success = true,
            BookId = 1,
            BookTitle = "Love Story",
            AuthorName = "Test Author",
            Chapters =
            [
                new ChapterDto { ChapterNumber = 1, Title = "Chapter One", Content = "Hello world.\n\nSecond paragraph." }
            ]
        };
        var opt = new BookPdfExportOptions
        {
            InteriorStyle = "Classic",
            TextSize = "Large",
            LineSpacing = "1.8",
            IncludeCoverPage = false
        };
        var bytes = await svc.RenderFullBookPdfAsync(details, null, "Love Story", "Test Author", "Romance", opt, null);
        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 128);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    private sealed class StubEnv : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
