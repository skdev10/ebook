using System;
using EBookDashboard.Services;
using Xunit;

namespace EBookDashboard.Tests;

public class FriendlyDateFormatterTests
{
    [Fact]
    public void Format_renders_human_friendly_afternoon()
    {
        var dt = new DateTime(2026, 6, 18, 16, 37, 32, DateTimeKind.Utc);
        Assert.Equal("June 18, 2026 at 4:37 PM", FriendlyDateFormatter.Format(dt));
    }

    [Fact]
    public void Format_renders_midnight_as_12_am()
    {
        var dt = new DateTime(2026, 1, 1, 0, 5, 0, DateTimeKind.Utc);
        Assert.Equal("January 1, 2026 at 12:05 AM", FriendlyDateFormatter.Format(dt));
    }

    [Fact]
    public void Format_renders_noon_as_12_pm()
    {
        var dt = new DateTime(2026, 12, 31, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal("December 31, 2026 at 12:00 PM", FriendlyDateFormatter.Format(dt));
    }

    [Fact]
    public void Format_nullable_returns_fallback_when_null()
    {
        Assert.Equal("—", FriendlyDateFormatter.Format((DateTime?)null));
        Assert.Equal("n/a", FriendlyDateFormatter.Format((DateTime?)null, "n/a"));
    }

    [Fact]
    public void Format_nullable_returns_fallback_when_default_value()
    {
        Assert.Equal("—", FriendlyDateFormatter.Format((DateTime?)default(DateTime)));
    }
}
