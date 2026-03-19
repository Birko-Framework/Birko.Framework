using FluentAssertions;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Tests;

public class IrCommandTests
{
    [Fact]
    public void ToString_Standard_ShowsProtocolAddressCommand()
    {
        var command = new IrCommand
        {
            Protocol = "NEC",
            Address = 0x04,
            Command = 0x08,
            RawCode = 0x12345678
        };

        command.ToString().Should().Contain("NEC");
        command.ToString().Should().Contain("0x4");
        command.ToString().Should().Contain("0x8");
    }

    [Fact]
    public void ToString_Repeat_ShowsRepeat()
    {
        var command = new IrCommand { Protocol = "NEC", IsRepeat = true };
        command.ToString().Should().Be("NEC REPEAT");
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var command = new IrCommand();
        command.Protocol.Should().BeEmpty();
        command.Address.Should().Be(0);
        command.Command.Should().Be(0);
        command.RawCode.Should().Be(0UL);
        command.IsRepeat.Should().BeFalse();
        command.BitCount.Should().Be(0);
        command.Toggle.Should().Be(0);
        command.ExtendedData.Should().BeNull();
    }
}
