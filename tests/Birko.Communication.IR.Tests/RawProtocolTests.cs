using FluentAssertions;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Tests;

public class RawProtocolTests
{
    private readonly RawProtocol _protocol = new();

    [Fact]
    public void Name_IsRaw()
    {
        _protocol.Name.Should().Be("Raw");
    }

    [Fact]
    public void Encode_ThrowsNotSupported()
    {
        var command = new IrCommand { Address = 0, Command = 0 };
        var act = () => _protocol.Encode(command);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Encode_NullCommand_Throws()
    {
        var act = () => _protocol.Encode(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Decode_ValidTiming_ReturnsRawCommand()
    {
        var timing = new IrTiming(new[] { 1000, 2000, 3000 }, 38000);
        var decoded = _protocol.Decode(timing);

        decoded.Should().NotBeNull();
        decoded!.Protocol.Should().Be("Raw");
        decoded.BitCount.Should().Be(3); // number of durations
        decoded.RawCode.Should().NotBe(0UL);
    }

    [Fact]
    public void Decode_NullTiming_ReturnsNull()
    {
        _protocol.Decode(null!).Should().BeNull();
    }

    [Fact]
    public void Decode_EmptyDurations_ReturnsNull()
    {
        var timing = new IrTiming(Array.Empty<int>(), 38000);
        _protocol.Decode(timing).Should().BeNull();
    }

    [Fact]
    public void Decode_DifferentTimings_ProduceDifferentHashes()
    {
        var t1 = new IrTiming(new[] { 1000, 2000, 3000 }, 38000);
        var t2 = new IrTiming(new[] { 1000, 2000, 4000 }, 38000);

        var d1 = _protocol.Decode(t1);
        var d2 = _protocol.Decode(t2);

        d1!.RawCode.Should().NotBe(d2!.RawCode);
    }

    [Fact]
    public void Decode_SameTimings_ProduceSameHash()
    {
        var t1 = new IrTiming(new[] { 1000, 2000, 3000 }, 38000);
        var t2 = new IrTiming(new[] { 1000, 2000, 3000 }, 38000);

        var d1 = _protocol.Decode(t1);
        var d2 = _protocol.Decode(t2);

        d1!.RawCode.Should().Be(d2!.RawCode);
    }
}
