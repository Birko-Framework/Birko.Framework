using FluentAssertions;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Tests;

public class NecProtocolTests
{
    private readonly NecProtocol _protocol = new();

    // ── Encode ──

    [Fact]
    public void Encode_StandardNec_ProducesCorrectLeader()
    {
        var command = new IrCommand { Address = 0x00, Command = 0x00 };
        var timing = _protocol.Encode(command);

        timing.Durations[0].Should().Be(9000); // leader mark
        timing.Durations[1].Should().Be(4500); // leader space
    }

    [Fact]
    public void Encode_StandardNec_Produces67Durations()
    {
        // 2 (leader) + 64 (32 bits * 2) + 1 (stop) = 67
        var command = new IrCommand { Address = 0x04, Command = 0x08 };
        var timing = _protocol.Encode(command);

        timing.Durations.Should().HaveCount(67);
        timing.CarrierFrequencyHz.Should().Be(38000);
    }

    [Fact]
    public void Encode_RepeatCode_Produces3Durations()
    {
        var command = new IrCommand { IsRepeat = true };
        var timing = _protocol.Encode(command);

        timing.Durations.Should().HaveCount(3);
        timing.Durations[0].Should().Be(9000);
        timing.Durations[1].Should().Be(2250);
        timing.Durations[2].Should().Be(562);
    }

    [Fact]
    public void Encode_NullCommand_Throws()
    {
        var act = () => _protocol.Encode(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Decode ──

    [Fact]
    public void Encode_Then_Decode_RoundTrip_StandardNec()
    {
        var original = new IrCommand { Address = 0x04, Command = 0x08 };
        var timing = _protocol.Encode(original);
        var decoded = _protocol.Decode(timing);

        decoded.Should().NotBeNull();
        decoded!.Protocol.Should().Be("NEC");
        decoded.Address.Should().Be(0x04);
        decoded.Command.Should().Be(0x08);
        decoded.BitCount.Should().Be(32);
        decoded.IsRepeat.Should().BeFalse();
    }

    [Fact]
    public void Encode_Then_Decode_RoundTrip_ExtendedNec()
    {
        var extProtocol = new NecProtocol { ExtendedMode = true };
        var original = new IrCommand { Address = 0x1234, Command = 0x56 };
        var timing = extProtocol.Encode(original);
        var decoded = extProtocol.Decode(timing);

        decoded.Should().NotBeNull();
        decoded!.Protocol.Should().Be("NEC-Ext");
        decoded.Address.Should().Be(0x1234);
        decoded.Command.Should().Be(0x56);
    }

    [Fact]
    public void Decode_RepeatCode_ReturnsRepeatCommand()
    {
        var timing = new IrTiming(new[] { 9000, 2250, 562 }, 38000);
        var decoded = _protocol.Decode(timing);

        decoded.Should().NotBeNull();
        decoded!.IsRepeat.Should().BeTrue();
        decoded.Protocol.Should().Be("NEC");
    }

    [Fact]
    public void Decode_NullTiming_ReturnsNull()
    {
        _protocol.Decode(null!).Should().BeNull();
    }

    [Fact]
    public void Decode_TooShort_ReturnsNull()
    {
        var timing = new IrTiming(new[] { 9000, 4500 }, 38000);
        _protocol.Decode(timing).Should().BeNull();
    }

    [Fact]
    public void Decode_InvalidLeader_ReturnsNull()
    {
        var timing = new IrTiming(new[] { 1000, 4500, 562, 562 }, 38000);
        _protocol.Decode(timing).Should().BeNull();
    }

    [Fact]
    public void Decode_InvalidCommandComplement_ReturnsNull()
    {
        // Build a frame with invalid command complement
        var command = new IrCommand { Address = 0x00, Command = 0x00 };
        var timing = _protocol.Encode(command);

        // Corrupt the command complement bits (last 8 bits)
        // Flip a space from 0 to 1 in the complement area
        var durations = (int[])timing.Durations.Clone();
        durations[durations.Length - 3] = 1687; // change a zero space to one space

        var corrupted = new IrTiming(durations, 38000);
        _protocol.Decode(corrupted).Should().BeNull();
    }

    [Fact]
    public void Encode_MultipleAddressCommands_AllDistinct()
    {
        var t1 = _protocol.Encode(new IrCommand { Address = 0x00, Command = 0x01 });
        var t2 = _protocol.Encode(new IrCommand { Address = 0x00, Command = 0x02 });
        var t3 = _protocol.Encode(new IrCommand { Address = 0x01, Command = 0x01 });

        // Different commands/addresses produce different timings
        t1.Durations.Should().NotEqual(t2.Durations);
        t1.Durations.Should().NotEqual(t3.Durations);
    }
}
