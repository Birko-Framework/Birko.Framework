using FluentAssertions;
using Birko.Communication.IR.Ports;
using Birko.Communication.IR.Protocols;

namespace Birko.Communication.IR.Tests;

public class InfraredPortTests
{
    private static InfraredPort CreatePort(FakeIrTransport transport, int carrierHz = 38000)
    {
        var settings = new InfraredSettings { Name = "test", CarrierFrequencyHz = carrierHz };
        return new InfraredPort(settings, transport);
    }

    // ── Construction ──

    [Fact]
    public void Ctor_NullTransport_Throws()
    {
        var act = () => new InfraredPort(new InfraredSettings(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── SendCommandAsync / SendRawAsync delegation ──

    [Fact]
    public async Task SendCommandAsync_EncodesAndDelegatesToTransport()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport);
        var protocol = new NecProtocol();
        var command = new IrCommand { Address = 0x04, Command = 0x08 };

        await port.SendCommandAsync(protocol, command);

        transport.Transmitted.Should().HaveCount(1);
        // The transport received exactly what the protocol encoded.
        transport.LastTiming!.Durations.Should().Equal(protocol.Encode(command).Durations);
    }

    [Fact]
    public async Task SendCommandAsync_NullProtocol_Throws()
    {
        var port = CreatePort(new FakeIrTransport());
        var act = async () => await port.SendCommandAsync(null!, new IrCommand());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendCommandAsync_NullCommand_Throws()
    {
        var port = CreatePort(new FakeIrTransport());
        var act = async () => await port.SendCommandAsync(new NecProtocol(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendRawAsync_DelegatesTimingUnchanged()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport);
        var timing = new IrTiming(new[] { 900, 450, 560, 560 }, 38000);

        await port.SendRawAsync(timing);

        transport.Transmitted.Should().ContainSingle();
        transport.LastTiming.Should().BeSameAs(timing);
    }

    [Fact]
    public async Task SendRawAsync_NullTiming_Throws()
    {
        var port = CreatePort(new FakeIrTransport());
        var act = async () => await port.SendRawAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Write (packed int32 durations) ──

    [Fact]
    public void Write_ValidPackedDurations_TransmitsThem()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport, carrierHz: 40000);
        int[] durations = { 9000, 4500, 562, 1687 };

        var bytes = durations.SelectMany(BitConverter.GetBytes).ToArray();
        port.Write(bytes);

        transport.Transmitted.Should().ContainSingle();
        transport.LastTiming!.Durations.Should().Equal(durations);
        transport.LastTiming.CarrierFrequencyHz.Should().Be(40000, "Write uses the settings carrier frequency");
    }

    [Fact]
    public void Write_LengthNotMultipleOfFour_ThrowsArgumentException()
    {
        var port = CreatePort(new FakeIrTransport());
        var act = () => port.Write(new byte[] { 1, 2, 3 }); // 3 % 4 != 0

        act.Should().Throw<ArgumentException>();
    }

    // ── HandleReceivedTiming: protocol match vs raw fallback ──

    [Fact]
    public void HandleReceivedTiming_ProtocolMatch_RaisesDecodedCommand()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport);
        port.RegisterProtocol(new NecProtocol());

        IrCommand? received = null;
        port.OnCommandReceived += (_, cmd) => received = cmd;

        // A valid NEC frame that the registered protocol will decode.
        var timing = new NecProtocol().Encode(new IrCommand { Address = 0x04, Command = 0x08 });
        transport.RaiseReceived(timing);

        received.Should().NotBeNull("a matching protocol decodes the capture");
        received!.Protocol.Should().Be("NEC");
        received.Address.Should().Be(0x04);
        received.Command.Should().Be(0x08);
        port.ReadData.Should().BeEmpty("a decoded command does not fall through to the raw buffer");
    }

    [Fact]
    public void HandleReceivedTiming_NoProtocolMatch_FallsBackToRawBuffer()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport);
        port.RegisterProtocol(new NecProtocol()); // registered, but the timing won't match

        bool commandRaised = false;
        port.OnCommandReceived += (_, _) => commandRaised = true;

        // Too short / invalid for NEC -> Decode returns null -> raw fallback.
        var timing = new IrTiming(new[] { 1000, 2000 }, 38000);
        transport.RaiseReceived(timing);

        commandRaised.Should().BeFalse("no protocol matched");
        // 2 durations * 4 bytes each packed into ReadData.
        port.ReadData.Should().HaveCount(8);
    }

    [Fact]
    public void HandleReceivedTiming_NoProtocolsRegistered_StoresRawBytes()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport);

        var timing = new IrTiming(new[] { 100, 200, 300 }, 38000);
        transport.RaiseReceived(timing);

        port.ReadData.Should().HaveCount(12); // 3 durations * 4 bytes
    }

    [Fact]
    public void RegisterProtocol_Null_Throws()
    {
        var port = CreatePort(new FakeIrTransport());
        var act = () => port.RegisterProtocol(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── Open / Close lifecycle ──

    [Fact]
    public void Open_ConnectsTransportAndReportsOpen()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport);

        port.Open();

        transport.ConnectCalls.Should().Be(1);
        transport.IsConnected.Should().BeTrue();
        port.IsOpen().Should().BeTrue("IsOpen reflects the transport connection state");
    }

    [Fact]
    public void Close_StopsReceiveAndDisconnects()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport);
        port.Open();

        port.Close();

        transport.StopReceiveCalls.Should().BeGreaterThanOrEqualTo(1);
        transport.DisconnectCalls.Should().Be(1);
        transport.IsConnected.Should().BeFalse();
        port.IsOpen().Should().BeFalse();
    }

    [Fact]
    public async Task StartLearning_StopLearning_DelegateToTransport()
    {
        var transport = new FakeIrTransport();
        var port = CreatePort(transport);

        await port.StartLearningAsync();
        await port.StopLearningAsync();

        transport.StartReceiveCalls.Should().Be(1);
        transport.StopReceiveCalls.Should().Be(1);
    }
}
