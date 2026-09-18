using FluentAssertions;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Tests;

public class IrTimingTests
{
    [Fact]
    public void Constructor_SetsDurationsAndFrequency()
    {
        var durations = new[] { 9000, 4500, 562, 562 };
        var timing = new IrTiming(durations, 38000);

        timing.Durations.Should().Equal(durations);
        timing.CarrierFrequencyHz.Should().Be(38000);
    }

    [Fact]
    public void Constructor_NullDurations_Throws()
    {
        var act = () => new IrTiming(null!, 38000);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultConstructor_HasEmptyDurations()
    {
        var timing = new IrTiming();
        timing.Durations.Should().BeEmpty();
        timing.CarrierFrequencyHz.Should().Be(38000);
        timing.RepeatCount.Should().Be(0);
        timing.RepeatGapUs.Should().Be(108000);
    }

    [Fact]
    public void TotalDurationUs_SumsAllDurations()
    {
        var timing = new IrTiming(new[] { 9000, 4500, 562, 1687, 562 }, 38000);
        timing.TotalDurationUs().Should().Be(9000 + 4500 + 562 + 1687 + 562);
    }

    [Fact]
    public void TotalDurationUs_EmptyDurations_ReturnsZero()
    {
        var timing = new IrTiming();
        timing.TotalDurationUs().Should().Be(0);
    }

    [Fact]
    public void ToProntoHex_ProducesValidFormat()
    {
        var timing = new IrTiming(new[] { 9000, 4500, 562, 562 }, 38000);
        var pronto = timing.ToProntoHex();

        pronto.Should().NotBeNullOrEmpty();
        // Pronto starts with "0000" (learned code identifier)
        pronto.Should().StartWith("0000");

        // All parts should be 4-character hex strings
        var parts = pronto.Split(' ');
        parts.Length.Should().BeGreaterThanOrEqualTo(4);
        foreach (var part in parts)
        {
            part.Should().HaveLength(4);
        }
    }

    [Fact]
    public void ToProntoHex_PadsToEvenCount()
    {
        // 3 durations (odd) should be padded to 4 (even) for pairs
        var timing = new IrTiming(new[] { 9000, 4500, 562 }, 38000);
        var pronto = timing.ToProntoHex();

        var parts = pronto.Split(' ');
        // 4 header + even data pairs
        (parts.Length - 4).Should().BeGreaterThan(0);
        ((parts.Length - 4) % 2).Should().Be(0);
    }
}
