using System.Reflection;
using FluentAssertions;
using Birko.Communication.IR.Protocols;
using Birko.Communication.IR.Transports;

namespace Birko.Communication.IR.Tests;

/// <summary>
/// Exercises the pure line-parsing seam of <see cref="SerialIrTransport"/> (<c>ProcessReceivedLine</c>)
/// via reflection, so no real serial hardware is touched. Constructing the transport only builds a
/// <c>SerialPort</c> object (it is not opened until ConnectAsync), so this stays fully offline.
/// </summary>
public class SerialIrTransportTests
{
    private static readonly MethodInfo ProcessLine =
        typeof(SerialIrTransport).GetMethod("ProcessReceivedLine", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static (SerialIrTransport transport, List<IrTiming> received) NewTransport()
    {
        var transport = new SerialIrTransport("COM_TEST_NOHW");
        var received = new List<IrTiming>();
        transport.OnReceived += (_, t) => received.Add(t);
        return (transport, received);
    }

    private static void Feed(SerialIrTransport transport, string line) =>
        ProcessLine.Invoke(transport, new object[] { line });

    [Fact]
    public void ProcessReceivedLine_ValidRecvLine_RaisesTimingWithFrequencyAndDurations()
    {
        var (transport, received) = NewTransport();

        Feed(transport, "RECV 38000 9000,4500,560,1690,560");

        received.Should().ContainSingle();
        received[0].CarrierFrequencyHz.Should().Be(38000);
        received[0].Durations.Should().Equal(9000, 4500, 560, 1690, 560);
    }

    [Fact]
    public void ProcessReceivedLine_IgnoresNonRecvLines()
    {
        var (transport, received) = NewTransport();

        Feed(transport, "OK");
        Feed(transport, "READY");
        Feed(transport, "");

        received.Should().BeEmpty();
    }

    [Fact]
    public void ProcessReceivedLine_MissingDurations_DoesNotRaise()
    {
        var (transport, received) = NewTransport();

        Feed(transport, "RECV 38000"); // freq but no duration field

        received.Should().BeEmpty();
    }

    [Fact]
    public void ProcessReceivedLine_NonNumericFrequency_DoesNotRaise()
    {
        var (transport, received) = NewTransport();

        Feed(transport, "RECV abc 9000,4500");

        received.Should().BeEmpty();
    }

    [Fact]
    public void ProcessReceivedLine_SkipsNonNumericDurationTokens()
    {
        var (transport, received) = NewTransport();

        // Bad tokens are skipped; the valid ones still form the timing.
        Feed(transport, "RECV 36000 900, ,x,1780");

        received.Should().ContainSingle();
        received[0].CarrierFrequencyHz.Should().Be(36000);
        received[0].Durations.Should().Equal(900, 1780);
    }

    [Fact]
    public void ProcessReceivedLine_AllDurationsInvalid_DoesNotRaise()
    {
        var (transport, received) = NewTransport();

        Feed(transport, "RECV 38000 x,y,z");

        received.Should().BeEmpty();
    }

    [Fact]
    public void Ctor_NullSerial_Throws()
    {
        var act = () => new SerialIrTransport((Birko.Communication.Hardware.Ports.Serial)null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
