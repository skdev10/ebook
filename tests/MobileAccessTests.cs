using EBookDashboard.Infrastructure;
using Xunit;

namespace EBookDashboard.Tests;

public class MobileUserAgentDetectorTests
{
    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 Safari/604.1", null)]
    [InlineData("Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/120.0.0.0 Mobile Safari/537.36", null)]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 Safari/604.1", null)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/17.0 Mobile/15E148 Safari/604.1", null)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0 Safari/537.36", "?1")]
    public void IsMobileOrTablet_detects_mobile_and_tablet_agents(string userAgent, string? clientHint)
    {
        Assert.True(MobileUserAgentDetector.IsMobileOrTablet(userAgent, clientHint));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0 Safari/537.36", null)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/17.0 Safari/605.1.15", null)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) Firefox/121.0", null)]
    public void IsMobileOrTablet_allows_desktop_agents(string userAgent, string? clientHint)
    {
        Assert.False(MobileUserAgentDetector.IsMobileOrTablet(userAgent, clientHint));
    }
}

public class MobileBlockPageRendererTests
{
    [Fact]
    public void Render_includes_configured_messages()
    {
        var options = new EBookDashboard.Models.MobileAccessOptions
        {
            Title = "Desktop Only",
            Message = "Not on mobile.",
            SubMessage = "Use a laptop."
        };

        var html = MobileBlockPageRenderer.Render(options);

        Assert.Contains("Desktop Only", html);
        Assert.Contains("Not on mobile.", html);
        Assert.Contains("Use a laptop.", html);
    }
}
