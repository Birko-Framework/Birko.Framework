using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Time.Tests;

public class SystemDateTimeProviderTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        var provider = new SystemDateTimeProvider();
        var before = DateTime.UtcNow;
        var result = provider.UtcNow;
        var after = DateTime.UtcNow;

        result.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void OffsetUtcNow_ReturnsCurrentUtcOffset()
    {
        var provider = new SystemDateTimeProvider();
        var result = provider.OffsetUtcNow;

        result.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Today_ReturnsCurrentDate()
    {
        var provider = new SystemDateTimeProvider();
        var result = provider.Today;

        result.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }
}

public class TestDateTimeProviderTests
{
    [Fact]
    public void DefaultTime_Is2026Jan1Noon()
    {
        var provider = new TestDateTimeProvider();

        provider.UtcNow.Should().Be(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Constructor_WithInitialTime_SetsTime()
    {
        var time = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var provider = new TestDateTimeProvider(time);

        provider.OffsetUtcNow.Should().Be(time);
    }

    [Fact]
    public void SetTime_ChangesCurrentTime()
    {
        var provider = new TestDateTimeProvider();
        var newTime = new DateTimeOffset(2030, 12, 25, 8, 0, 0, TimeSpan.Zero);

        provider.SetTime(newTime);

        provider.OffsetUtcNow.Should().Be(newTime);
    }

    [Fact]
    public void Advance_MovesTimeForward()
    {
        var initial = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var provider = new TestDateTimeProvider(initial);

        provider.Advance(TimeSpan.FromHours(3));

        provider.OffsetUtcNow.Should().Be(initial.AddHours(3));
    }

    [Fact]
    public void Advance_CanMoveBackward()
    {
        var initial = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var provider = new TestDateTimeProvider(initial);

        provider.Advance(TimeSpan.FromHours(-2));

        provider.OffsetUtcNow.Should().Be(initial.AddHours(-2));
    }

    [Fact]
    public void Today_ReflectsCurrentDate()
    {
        var provider = new TestDateTimeProvider(new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero));

        provider.Today.Should().Be(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public void MultipleAdvances_Accumulate()
    {
        var provider = new TestDateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        provider.Advance(TimeSpan.FromHours(1));
        provider.Advance(TimeSpan.FromMinutes(30));

        provider.UtcNow.Should().Be(new DateTime(2026, 1, 1, 1, 30, 0, DateTimeKind.Utc));
    }
}
