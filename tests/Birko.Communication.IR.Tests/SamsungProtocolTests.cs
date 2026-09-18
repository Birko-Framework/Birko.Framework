using FluentAssertions;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Tests;

public class SamsungProtocolTests
{
    private readonly SamsungProtocol _protocol = new();

    [Fact]
    public void Encode_ProducesCorrectLeader()
    {
        var command = new IrCommand { Address = 0x07, Command = 0x02 };
        var timing = _protocol.Encode(command);

        timing.Durations[0].Should().Be(4500); // Samsung leader mark
        timing.Durations[1].Should().Be(4500); // Samsung leader space
    }

    [Fact]
    public void Encode_Produces67Durations()
    {
        var command = new IrCommand { Address = 0x07, Command = 0x02 };
        var timing = _protocol.Encode(command);

        timing.Durations.Should().HaveCount(67);
        timing.CarrierFrequencyHz.Should().Be(38000);
    }

    [Fact]
    public void Encode_NullCommand_Throws()
    {
        var act = () => _protocol.Encode(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Encode_Then_Decode_RoundTrip()
    {
        var original = new IrCommand { Address = 0x07, Command = 0x02 };
        var timing = _protocol.Encode(original);
        var decoded = _protocol.Decode(timing);

        decoded.Should().NotBeNull();
        decoded!.Protocol.Should().Be("Samsung");
        decoded.Address.Should().Be(0x07);
        decoded.Command.Should().Be(0x02);
        decoded.BitCount.Should().Be(32);
    }

    [Fact]
    public void Decode_NullTiming_ReturnsNull()
    {
        _protocol.Decode(null!).Should().BeNull();
    }

    [Fact]
    public void Decode_TooShort_ReturnsNull()
    {
        var timing = new IrTiming(new int[10], 38000);
        _protocol.Decode(timing).Should().BeNull();
    }

    [Fact]
    public void Decode_AddressMismatch_ReturnsNull()
    {
        // Build valid frame then corrupt address repeat
        var command = new IrCommand { Address = 0x07, Command = 0x02 };
        var timing = _protocol.Encode(command);
        var durations = (int[])timing.Durations.Clone();

        // Flip a bit in the second address byte (bits 8-15) to break addr == addr repeat
        // Bit 8 starts at index 2 + 8*2 = 18 (mark), 19 (space)
        durations[19] = durations[19] == 550 ? 1650 : 550;

        var corrupted = new IrTiming(durations, 38000);
        _protocol.Decode(corrupted).Should().BeNull();
    }

    [Fact]
    public void Decode_AllAddresses_RoundTrip()
    {
        for (uint addr = 0; addr < 256; addr += 51)
        {
            var original = new IrCommand { Address = addr, Command = 0x10 };
            var timing = _protocol.Encode(original);
            var decoded = _protocol.Decode(timing);

            decoded.Should().NotBeNull($"address 0x{addr:X2} should decode");
            decoded!.Address.Should().Be(addr);
        }
    }

    [Fact]
    public void Decode_AllCommands_RoundTrip()
    {
        for (uint cmd = 0; cmd < 256; cmd += 51)
        {
            var original = new IrCommand { Address = 0x07, Command = cmd };
            var timing = _protocol.Encode(original);
            var decoded = _protocol.Decode(timing);

            decoded.Should().NotBeNull($"command 0x{cmd:X2} should decode");
            decoded!.Command.Should().Be(cmd);
        }
    }
}
