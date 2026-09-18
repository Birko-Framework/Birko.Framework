using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.Time.Tests;

public class TimeZoneConverterTests
{
    private readonly TimeZoneConverter _converter = new();

    [Fact]
    public void Convert_ToUtc_RemovesOffset()
    {
        var input = new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.FromHours(2));

        var result = _converter.Convert(input, TimeZoneInfo.Utc);

        result.Offset.Should().Be(TimeSpan.Zero);
        result.Hour.Should().Be(12);
    }

    [Fact]
    public void ConvertToUtc_ReturnsUtc()
    {
        var input = new DateTimeOffset(2026, 6, 15, 14, 0, 0, TimeSpan.FromHours(5));

        var result = _converter.ConvertToUtc(input);

        result.Offset.Should().Be(TimeSpan.Zero);
        result.Hour.Should().Be(9);
    }

    [Fact]
    public void ConvertFromUtc_AppliesOffset()
    {
        var utc = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("UTC");

        var result = _converter.ConvertFromUtc(utc, zone);

        result.Hour.Should().Be(12);
    }

    [Fact]
    public void Convert_RoundTrip_PreservesInstant()
    {
        var original = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var utc = TimeZoneInfo.Utc;

        var converted = _converter.Convert(original, utc);
        var roundTripped = _converter.ConvertToUtc(converted);

        roundTripped.UtcDateTime.Should().Be(original.UtcDateTime);
    }

    [Fact]
    public void GetTimeZone_Utc_ReturnsUtcZone()
    {
        var zone = _converter.GetTimeZone("UTC");

        zone.Should().Be(TimeZoneInfo.Utc);
    }

    [Fact]
    public void GetTimeZone_InvalidId_Throws()
    {
        var act = () => _converter.GetTimeZone("Invalid/Zone");

        act.Should().Throw<TimeZoneNotFoundException>();
    }

    [Fact]
    public void GetTimeZone_EmptyId_Throws()
    {
        var act = () => _converter.GetTimeZone("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetAvailableTimeZones_ReturnsNonEmptyList()
    {
        var zones = _converter.GetAvailableTimeZones();

        zones.Should().NotBeEmpty();
        zones.Should().Contain(z => z.Id == "UTC");
    }

    [Fact]
    public void Convert_NullTargetZone_Throws()
    {
        var act = () => _converter.Convert(DateTimeOffset.UtcNow, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ConvertFromUtc_NullTargetZone_Throws()
    {
        var act = () => _converter.ConvertFromUtc(DateTimeOffset.UtcNow, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
