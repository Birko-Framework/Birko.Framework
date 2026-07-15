using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class DateFormatterTests
{
    [Fact]
    public void Format_UsesShortDatePattern()
    {
        var formatter = new DateFormatter();
        var date = new DateTime(2026, 3, 18);
        var result = formatter.Format(date, CultureInfo.GetCultureInfo("en-US"));
        result.Should().Contain("3/18/2026");
    }

    [Fact]
    public void Format_WithCustomFormat()
    {
        var formatter = new DateFormatter();
        var date = new DateTime(2026, 3, 18);
        var result = formatter.Format(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        result.Should().Be("2026-03-18");
    }

    [Fact]
    public void FormatRelative_JustNow()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now, now.AddSeconds(30)).Should().Be("just now");
    }

    [Fact]
    public void FormatRelative_MinutesAgo()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddMinutes(-5), now).Should().Be("5 minutes ago");
    }

    [Fact]
    public void FormatRelative_OneMinuteAgo()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddMinutes(-1), now).Should().Be("1 minute ago");
    }

    [Fact]
    public void FormatRelative_HoursAgo()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddHours(-3), now).Should().Be("3 hours ago");
    }

    [Fact]
    public void FormatRelative_Yesterday()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddHours(-30), now).Should().Be("yesterday");
    }

    [Fact]
    public void FormatRelative_DaysAgo()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddDays(-5), now).Should().Be("5 days ago");
    }

    [Fact]
    public void FormatRelative_MonthsAgo()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddDays(-60), now).Should().Be("2 months ago");
    }

    [Fact]
    public void FormatRelative_YearsAgo()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddDays(-400), now).Should().Be("1 year ago");
    }

    [Fact]
    public void FormatRelative_Future()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddHours(3), now).Should().Be("in 3 hours");
    }

    [Fact]
    public void FormatRelative_Tomorrow()
    {
        var formatter = new DateFormatter();
        var now = DateTime.UtcNow;
        formatter.FormatRelative(now.AddHours(30), now).Should().Be("tomorrow");
    }

    [Fact]
    public void Format_CustomFormat_ThrowsForNull()
    {
        var formatter = new DateFormatter();
        var act = () => formatter.Format(DateTime.UtcNow, (string)null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FormatRelative_IsEnglishOnly_RegardlessOfCulture()
    {
        // CR-L275: relative phrasing is English-only by design; the culture parameter doesn't change it.
        var formatter = new DateFormatter();
        var now = new DateTime(2026, 1, 1, 12, 0, 0);

        var result = formatter.FormatRelative(now.AddMinutes(-5), now, CultureInfo.GetCultureInfo("sk-SK"));

        result.Should().Be("5 minutes ago");
    }
}
