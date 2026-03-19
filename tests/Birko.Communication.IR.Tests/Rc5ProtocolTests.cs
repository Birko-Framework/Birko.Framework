using FluentAssertions;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Tests;

public class Rc5ProtocolTests
{
    private readonly Rc5Protocol _protocol = new();

    [Fact]
    public void Encode_ProducesValidTiming()
    {
        var command = new IrCommand { Address = 0x05, Command = 0x0C, Toggle = 0 };
        var timing = _protocol.Encode(command);

        timing.Should().NotBeNull();
        timing.CarrierFrequencyHz.Should().Be(36000);
        timing.Durations.Should().NotBeEmpty();
    }

    [Fact]
    public void Encode_NullCommand_Throws()
    {
        var act = () => _protocol.Encode(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Encode_DifferentToggle_ProducesDifferentTimings()
    {
        var t0 = _protocol.Encode(new IrCommand { Address = 0x05, Command = 0x0C, Toggle = 0 });
        var t1 = _protocol.Encode(new IrCommand { Address = 0x05, Command = 0x0C, Toggle = 1 });

        t0.Durations.Should().NotEqual(t1.Durations);
    }

    [Fact]
    public void Encode_DifferentAddress_ProducesDifferentTimings()
    {
        var t1 = _protocol.Encode(new IrCommand { Address = 0x01, Command = 0x0C });
        var t2 = _protocol.Encode(new IrCommand { Address = 0x02, Command = 0x0C });

        t1.Durations.Should().NotEqual(t2.Durations);
    }

    [Fact]
    public void Encode_DifferentCommand_ProducesDifferentTimings()
    {
        var t1 = _protocol.Encode(new IrCommand { Address = 0x05, Command = 0x0C });
        var t2 = _protocol.Encode(new IrCommand { Address = 0x05, Command = 0x0D });

        t1.Durations.Should().NotEqual(t2.Durations);
    }

    [Fact]
    public void Encode_AllDurationsAreMultiplesOfHalfBit()
    {
        var timing = _protocol.Encode(new IrCommand { Address = 0x05, Command = 0x0C });

        foreach (var d in timing.Durations)
        {
            // Each duration should be a multiple of 889 (half-bit period)
            (d % 889).Should().Be(0, $"duration {d} should be multiple of 889");
        }
    }

    [Fact]
    public void Decode_NullTiming_ReturnsNull()
    {
        _protocol.Decode(null!).Should().BeNull();
    }

    [Fact]
    public void Decode_TooShort_ReturnsNull()
    {
        var timing = new IrTiming(new int[5], 36000);
        _protocol.Decode(timing).Should().BeNull();
    }

    [Fact]
    public void Decode_InvalidSlotDuration_ReturnsNull()
    {
        // Durations that don't divide cleanly into 889μs half-bits
        var timing = new IrTiming(new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200 }, 36000);
        _protocol.Decode(timing).Should().BeNull();
    }
}
